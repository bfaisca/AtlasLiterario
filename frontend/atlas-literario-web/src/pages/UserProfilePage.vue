<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { storeToRefs } from 'pinia'
import { getErrorMessage } from '@/api/errors'
import { useProfileMapSource } from '@/composables/mapSource'
import { useProfilesStore } from '@/stores/profiles'
import type { MapType } from '@/types/api'
import { formatDate } from '@/utils/format'
import { normalizeHandle } from '@/utils/handle'
import { MAP_TYPE_LABELS, pluralize } from '@/utils/labels'
import { parseCountryQuery } from '@/utils/mapSelection'
import BaseBadge from '@/components/ui/BaseBadge.vue'
import BaseButton from '@/components/ui/BaseButton.vue'
import BaseSpinner from '@/components/ui/BaseSpinner.vue'
import EmptyState from '@/components/ui/EmptyState.vue'
import FollowButton from '@/components/people/FollowButton.vue'
import PeopleList from '@/components/people/PeopleList.vue'
import MapExplorer from '@/components/map/MapExplorer.vue'

/**
 * Perfil público de outra pessoa: cabeçalho, mapas completos (autores e obras, com resenhas),
 * seguidores e seguindo. `/u/:handle?tab=authors|origins|followers|following&country=ISO`.
 * 404 `profile.not_found` cobre inexistente e não público (não revela existência).
 */
const props = defineProps<{ handle: string }>()

const route = useRoute()
const router = useRouter()
const profiles = useProfilesStore()
const { profile, profileLoading, profileError } = storeToRefs(profiles)

const handle = computed(() => normalizeHandle(props.handle))
const source = useProfileMapSource(handle)

type TabId = 'authors' | 'origins' | 'followers' | 'following'
const TABS: ReadonlyArray<{ id: TabId; label: string }> = [
  { id: 'authors', label: 'Mapa de autores' },
  { id: 'origins', label: 'Mapa de obras' },
  { id: 'followers', label: 'Seguidores' },
  { id: 'following', label: 'Seguindo' },
]

const tab = computed<TabId>(() => {
  const raw = Array.isArray(route.query.tab) ? route.query.tab[0] : route.query.tab
  return TABS.find((t) => t.id === raw)?.id ?? 'authors'
})
const mapType = computed<MapType>(() => (tab.value === 'origins' ? 'Origins' : 'Authors'))
const isMapTab = computed(() => tab.value === 'authors' || tab.value === 'origins')
const requestedIso = computed(() => parseCountryQuery(route.query.country))

function setTab(id: TabId) {
  // Trocar de aba limpa o país selecionado (o painel pertence ao mapa da aba).
  void router.replace({ query: { ...(id === 'authors' ? {} : { tab: id }) } })
}
function setSelected(iso: string | null) {
  const query = { ...route.query }
  if (iso) query.country = iso
  else delete query.country
  void router.replace({ query })
}

/** Setas/Home/End movem entre as abas (padrão WAI-ARIA de tabs com ativação automática). */
const tabButtons = ref<HTMLElement[]>([])
async function onTabKeydown(event: KeyboardEvent, index: number) {
  const last = TABS.length - 1
  const next =
    event.key === 'ArrowRight'
      ? (index + 1) % TABS.length
      : event.key === 'ArrowLeft'
        ? (index - 1 + TABS.length) % TABS.length
        : event.key === 'Home'
          ? 0
          : event.key === 'End'
            ? last
            : null
  if (next === null) return
  event.preventDefault()
  setTab(TABS[next]!.id)
  await nextTick()
  tabButtons.value[next]?.focus()
}

const followersList = computed(() => profiles.getList('followers', handle.value))
const followingList = computed(() => profiles.getList('following', handle.value))

watch(handle, (value) => void profiles.fetchProfile(value), { immediate: true })
// Listas só carregam quando a aba é aberta (e o perfil existe).
watch(
  [() => profile.value?.handle, tab],
  ([loaded, current]) => {
    if (!loaded) return
    if (current === 'followers' || current === 'following') {
      void profiles.fetchList(current, handle.value)
    }
  },
  { immediate: true },
)

const notFound = computed(() => profileError.value?.code === 'profile.not_found')
const initials = computed(() =>
  (profile.value?.name ?? '')
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part.charAt(0).toLocaleUpperCase('pt-BR'))
    .join(''),
)
const mapCurrent = computed(() => source.data(mapType.value, 'Read'))
</script>

<template>
  <div class="page">
    <div v-if="profileLoading && !profile" class="center"><BaseSpinner size="lg" /></div>

    <div v-else-if="notFound" data-testid="profile-not-found">
      <EmptyState
        icon="user"
        title="Perfil não encontrado ou privado"
        description="Este apelido não existe ou a pessoa não deixou o perfil público."
      >
        <BaseButton :to="{ name: 'people' }">Buscar pessoas</BaseButton>
      </EmptyState>
    </div>

    <div v-else-if="profileError" role="alert">
      <EmptyState
        icon="alert"
        title="Não foi possível carregar o perfil"
        :description="getErrorMessage(profileError)"
      >
        <BaseButton variant="secondary" @click="profiles.fetchProfile(handle)">
          Tentar novamente
        </BaseButton>
      </EmptyState>
    </div>

    <template v-else-if="profile">
      <header class="head">
        <span class="head__avatar" aria-hidden="true">{{ initials }}</span>
        <div class="head__main">
          <p class="eyebrow">Perfil público</p>
          <h1>{{ profile.name }}</h1>
          <p class="head__handle">
            @{{ profile.handle }}
            <BaseBadge v-if="profile.followsYou" tone="sky" data-testid="follows-you"
              >Segue você</BaseBadge
            >
          </p>
          <p class="muted head__since">Leitor desde {{ formatDate(profile.memberSince) }}</p>
        </div>
        <div class="head__action">
          <FollowButton
            v-if="!profile.isSelf"
            :handle="profile.handle"
            :is-following="profile.isFollowing"
            :follows-you="profile.followsYou"
          />
          <BaseButton v-else variant="secondary" size="sm" :to="{ name: 'profile' }">
            Este é o seu perfil
          </BaseButton>
        </div>
      </header>

      <dl class="counts" aria-label="Números do perfil">
        <div>
          <dt>Seguidores</dt>
          <dd class="numeric" data-testid="count-followers">{{ profile.followersCount }}</dd>
        </div>
        <div>
          <dt>Seguindo</dt>
          <dd class="numeric" data-testid="count-following">{{ profile.followingCount }}</dd>
        </div>
        <div>
          <dt>Livros lidos</dt>
          <dd class="numeric">{{ profile.readBooksCount }}</dd>
        </div>
        <div>
          <dt>Países · autores</dt>
          <dd class="numeric">{{ profile.exploredCountriesAuthors }}</dd>
        </div>
        <div>
          <dt>Países · obras</dt>
          <dd class="numeric">{{ profile.exploredCountriesOrigins }}</dd>
        </div>
      </dl>

      <div class="tabs" role="tablist" aria-label="Seções do perfil">
        <button
          v-for="(item, index) in TABS"
          :id="`perfil-tab-${item.id}`"
          :key="item.id"
          :ref="(el) => (tabButtons[index] = el as HTMLElement)"
          type="button"
          role="tab"
          class="tab"
          :class="{ 'is-active': tab === item.id }"
          :aria-selected="tab === item.id"
          aria-controls="perfil-painel"
          :tabindex="tab === item.id ? 0 : -1"
          @click="setTab(item.id)"
          @keydown="onTabKeydown($event, index)"
        >
          {{ item.label }}
        </button>
      </div>

      <section
        id="perfil-painel"
        role="tabpanel"
        :aria-labelledby="`perfil-tab-${tab}`"
        class="panel"
      >
        <template v-if="isMapTab">
          <p class="muted summary" aria-live="polite">
            <template v-if="mapCurrent">
              {{ MAP_TYPE_LABELS[mapType] }} ·
              {{ pluralize(mapCurrent.exploredCountries, 'país explorado', 'países explorados') }} ·
              {{ pluralize(mapCurrent.totalReadBooks, 'livro lido', 'livros lidos') }}
            </template>
            <template v-else>{{ MAP_TYPE_LABELS[mapType] }}</template>
          </p>
          <MapExplorer
            :key="handle"
            :source="source"
            :map-type="mapType"
            :requested-iso="requestedIso"
            profile
            @select="setSelected"
          >
            <template #empty>
              <h2>Nenhum livro lido ainda</h2>
              <p class="muted">
                {{ profile.name }} ainda não marcou livros como lidos, então o mapa está em branco.
              </p>
            </template>
          </MapExplorer>
        </template>

        <PeopleList
          v-else-if="tab === 'followers'"
          :list="followersList"
          :label="`seguidores de @${profile.handle}`"
          empty-title="Nenhum seguidor público ainda"
          empty-description="Só aparecem seguidores com perfil público."
          @retry="profiles.fetchList('followers', handle, true)"
          @load-more="profiles.loadMore('followers', handle)"
        />
        <PeopleList
          v-else
          :list="followingList"
          :label="`quem @${profile.handle} segue`"
          empty-title="Não segue ninguém com perfil público"
          empty-description="Só aparecem pessoas com perfil público."
          @retry="profiles.fetchList('following', handle, true)"
          @load-more="profiles.loadMore('following', handle)"
        />
      </section>
    </template>
  </div>
</template>

<style scoped>
.center {
  display: grid;
  place-items: center;
  padding: var(--space-8);
}
.head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--space-4);
}
.head__avatar {
  display: grid;
  place-items: center;
  width: 72px;
  height: 72px;
  border-radius: var(--radius-pill);
  background: var(--color-brass-soft);
  color: var(--color-brass);
  font-family: var(--font-display);
  font-size: var(--text-2xl);
}
.head__main {
  flex: 1;
  min-width: 0;
}
.head__main h1 {
  margin-top: var(--space-1);
  overflow-wrap: anywhere;
}
.head__handle {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--space-2);
  color: var(--color-sky);
}
.head__since {
  font-size: var(--text-sm);
}
.counts {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(130px, 1fr));
  gap: var(--space-3);
  padding: var(--space-3) 0;
  border-block: 1px solid var(--color-hairline);
}
.counts dt {
  font-size: var(--text-xs);
  letter-spacing: var(--tracking-caps);
  text-transform: uppercase;
  color: var(--color-text-faint);
}
.counts dd {
  font-family: var(--font-display);
  font-size: var(--text-2xl);
}
.tabs {
  display: flex;
  gap: var(--space-1);
  overflow-x: auto;
  border-bottom: 1px solid var(--color-hairline);
}
.tab {
  padding: var(--space-2) var(--space-4);
  color: var(--color-text-muted);
  border-bottom: 2px solid transparent;
  white-space: nowrap;
  cursor: pointer;
}
.tab:hover {
  color: var(--color-text);
}
.tab.is-active {
  color: var(--color-brass-strong);
  border-bottom-color: var(--color-brass);
}
.tab:focus-visible {
  outline: 2px solid var(--color-sky);
  outline-offset: -2px;
  border-radius: var(--radius-sm);
}
.panel {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}
.summary {
  font-size: var(--text-sm);
}
</style>
