<script setup lang="ts">
import { computed, onMounted, watch } from 'vue'
import { useProfilesStore, type PeopleListKind } from '@/stores/profiles'
import BaseButton from '@/components/ui/BaseButton.vue'
import PeopleList from '@/components/people/PeopleList.vue'

/** `/me/followers` e `/me/following`: minhas listas (contas privadas já vêm omitidas da API). */
const props = defineProps<{ kind: PeopleListKind }>()
const profiles = useProfilesStore()

const list = computed(() => profiles.getList(props.kind, null))
const isFollowers = computed(() => props.kind === 'followers')

const COPY = {
  followers: {
    title: 'Seguidores',
    label: 'seus seguidores',
    empty: 'Ainda não há seguidores',
    emptyDescription:
      'Quando alguém seguir você, aparece aqui. Perfis públicos são encontrados pelo apelido.',
  },
  following: {
    title: 'Seguindo',
    label: 'quem você segue',
    empty: 'Você ainda não segue ninguém',
    emptyDescription: 'Encontre leitores na busca de pessoas para acompanhar seus mapas.',
  },
} as const
const copy = computed(() => COPY[props.kind])

onMounted(() => void profiles.fetchList(props.kind, null))
watch(
  () => props.kind,
  (kind) => void profiles.fetchList(kind, null),
)
</script>

<template>
  <div class="page">
    <header class="page-header">
      <div>
        <p class="eyebrow">
          <RouterLink :to="{ name: 'profile' }" class="back">Perfil</RouterLink>
        </p>
        <h1>{{ copy.title }}</h1>
        <p class="muted">
          {{ isFollowers ? 'Quem segue você' : 'Quem você segue' }}. Contas privadas não aparecem
          nesta lista.
        </p>
      </div>
      <BaseButton variant="secondary" :to="{ name: 'people' }">Encontrar pessoas</BaseButton>
    </header>

    <PeopleList
      :list="list"
      :label="copy.label"
      :empty-title="copy.empty"
      :empty-description="copy.emptyDescription"
      @retry="profiles.fetchList(kind, null, true)"
      @load-more="profiles.loadMore(kind, null)"
    />
  </div>
</template>

<style scoped>
.back {
  color: inherit;
}
</style>
