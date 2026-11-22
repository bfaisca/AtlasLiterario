<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { storeToRefs } from 'pinia'
import { useMapsStore } from '@/stores/maps'
import { getErrorMessage } from '@/api/errors'
import type { MapDataDto, MapType } from '@/types/api'
import { MAP_TYPE_LABELS, pluralize } from '@/utils/labels'
import { fromRouteMapType, toRouteMapType } from '@/utils/mapType'
import { parseCountryQuery } from '@/utils/mapSelection'
import BaseButton from '@/components/ui/BaseButton.vue'
import CountryList from '@/components/map/CountryList.vue'
import CountryPanel from '@/components/map/CountryPanel.vue'
import MapLegend from '@/components/map/MapLegend.vue'
import MapTypeToggle from '@/components/map/MapTypeToggle.vue'
import WorldMap from '@/components/map/WorldMap.vue'
import type { CountryHoverEvent } from '@/components/map/types'

/**
 * Página do mapa. A URL é a fonte da verdade: `/map/authors|origins?country=ISO`.
 * O painel do país abre quando `country` existe no mapa carregado; caso contrário é descartado.
 */
const route = useRoute()
const router = useRouter()
const maps = useMapsStore()
const { data, errorByType, loadingByType, countryDetail, detailLoading, detailError } =
  storeToRefs(maps)

const mapType = computed<MapType>({
  get: () => fromRouteMapType(route.params.type),
  set: (value) =>
    void router.replace({
      name: 'map',
      params: { type: toRouteMapType(value) },
      query: route.query, // mantém o país selecionado quando existir nos dois mapas
    }),
})
const otherType = computed<MapType>(() => (mapType.value === 'Authors' ? 'Origins' : 'Authors'))
const current = computed(() => data.value[mapType.value] ?? null)
const error = computed(() => errorByType.value[mapType.value] ?? null)
const loading = computed(() => !!loadingByType.value[mapType.value])

const hover = ref<CountryHoverEvent | null>(null)
const stage = ref<HTMLElement | null>(null)

/** Durante a troca de mapa o desenho anterior permanece até o novo chegar (sem piscar). */
const shown = ref<MapDataDto | null>(null)
watch(
  current,
  (value) => {
    if (value) shown.value = value
  },
  { immediate: true },
)

watch(
  mapType,
  (type) => {
    hover.value = null
    void maps.fetchMap(type)
  },
  { immediate: true },
)
// Pré-carrega o outro mapa: a alternância fica instantânea e a seleção pode ser validada.
watch(current, (value) => {
  if (value) void maps.fetchMap(otherType.value, { silent: true })
})

const requestedIso = computed(() => parseCountryQuery(route.query.country))
const selectedDatum = computed(
  () => current.value?.countries.find((c) => c.iso === requestedIso.value) ?? null,
)
/** Só há seleção válida (e painel) para países explorados do mapa atual. */
const selectedIso = computed(() => selectedDatum.value?.iso ?? null)

function setSelected(iso: string | null) {
  const query = { ...route.query }
  if (iso) query.country = iso
  else delete query.country
  void router.replace({ query })
}

// Descarta `?country=` que não existe no mapa carregado (inválido ou país sem livros neste mapa).
watch([current, requestedIso], ([data, iso]) => {
  if (data && iso && !data.countries.some((c) => c.iso === iso)) setSelected(null)
})

watch(
  [mapType, selectedIso],
  ([type, iso]) => {
    if (iso) void maps.fetchCountryDetail(type, iso)
    else maps.clearCountryDetail()
  },
  { immediate: true },
)

const detail = computed(() =>
  countryDetail.value?.iso === selectedIso.value && countryDetail.value?.mapType === mapType.value
    ? countryDetail.value
    : null,
)

function onMapSelect(iso: string | null) {
  const explored = iso && current.value?.countries.some((c) => c.iso === iso)
  if (!explored) return setSelected(null)
  setSelected(iso === selectedIso.value ? null : iso)
}

const tooltipStyle = computed(() => {
  if (!hover.value) return {}
  const width = stage.value?.clientWidth ?? 0
  const { x, y } = hover.value.point
  const flip = x > width - 200
  return {
    transform: `translate(${flip ? `calc(${x - 14}px - 100%)` : `${x + 14}px`}, ${y + 14}px)`,
  }
})

const hasNoReadBooks = computed(() => current.value !== null && current.value.totalReadBooks === 0)
const unknownCopy = computed(() =>
  mapType.value === 'Authors'
    ? 'de autor sem nacionalidade conhecida'
    : 'de obra sem origem conhecida',
)

function goToList() {
  const title = document.getElementById('paises-explorados')
  if (!title) return
  title.tabIndex = -1
  title.scrollIntoView({ behavior: 'smooth', block: 'start' })
  title.focus({ preventScroll: true })
}
</script>

<template>
  <div class="map-page">
    <header class="map-page__header">
      <div>
        <p class="eyebrow">Mapa literário</p>
        <h1>{{ MAP_TYPE_LABELS[mapType] }}</h1>
        <p class="muted map-page__summary" aria-live="polite">
          <template v-if="current">
            {{ pluralize(current.exploredCountries, 'país explorado', 'países explorados') }} ·
            {{ pluralize(current.totalReadBooks, 'livro lido', 'livros lidos') }}
          </template>
          <template v-else-if="loading">Carregando…</template>
          <template v-if="current?.countries.length">
            ·
            <button type="button" class="link-button" @click="goToList">Ver lista de países</button>
          </template>
        </p>
      </div>
      <MapTypeToggle v-model="mapType" data-keep-panel />
    </header>

    <div class="map-page__main" :class="{ 'map-page__main--panel': !!selectedDatum }">
      <div ref="stage" class="map-page__stage" data-keep-panel :aria-busy="loading">
        <WorldMap
          :data="(shown ?? current)?.countries ?? []"
          :max-book-count="(shown ?? current)?.maxBookCount ?? 0"
          :selected-iso="selectedIso"
          :label="MAP_TYPE_LABELS[mapType]"
          @country-hover="hover = $event"
          @country-select="onMapSelect"
        />
        <div v-if="hover" class="tooltip" :style="tooltipStyle" role="status">
          <strong>{{ hover.name }}</strong>
          <span v-if="hover.datum">
            {{ pluralize(hover.datum.bookCount, 'livro', 'livros') }}
            <template v-if="hover.datum.authorCount">
              · {{ pluralize(hover.datum.authorCount, 'autor', 'autores') }}
            </template>
          </span>
          <span v-else class="muted">Ainda não explorado</span>
        </div>
        <div v-if="error && !current" class="map-page__notice" role="status">
          Não foi possível carregar seus dados de leitura ({{ getErrorMessage(error) }}). O mapa é
          exibido sem destaques.
          <button
            type="button"
            class="link-button"
            @click="maps.fetchMap(mapType, { force: true })"
          >
            Tentar novamente
          </button>
        </div>
        <div v-if="hasNoReadBooks" class="map-page__empty">
          <h2>Nenhum livro lido ainda</h2>
          <p class="muted">
            Os países se acendem conforme você conclui leituras. Marque um livro como “Lido” para
            começar a colorir o mapa.
          </p>
          <div class="map-page__empty-actions">
            <BaseButton :to="{ name: 'library' }">Ir para a biblioteca</BaseButton>
            <BaseButton variant="secondary" :to="{ name: 'search' }">Buscar livros</BaseButton>
          </div>
        </div>
        <div class="map-page__legend">
          <MapLegend :max-book-count="(shown ?? current)?.maxBookCount ?? 0" />
        </div>
      </div>

      <Transition name="panel">
        <CountryPanel
          v-if="selectedDatum"
          :summary="selectedDatum"
          :detail="detail"
          :loading="detailLoading || (!detail && !detailError)"
          :error="detailError"
          @close="setSelected(null)"
          @retry="maps.fetchCountryDetail(mapType, selectedDatum.iso, true)"
        />
      </Transition>
    </div>

    <aside
      v-if="current && current.unknownBookCount > 0 && !hasNoReadBooks"
      class="map-page__unknown"
      role="note"
    >
      <strong>
        {{ pluralize(current.unknownBookCount, 'livro lido', 'livros lidos') }}
        {{ unknownCopy }}.
      </strong>
      <span class="muted">
        Dado ausente não é país inexplorado: ainda não encontramos essa informação, então esses
        livros ficam de fora da contagem deste mapa até que ela seja descoberta ou corrigida.
      </span>
      <RouterLink :to="{ name: 'library', query: { status: 'Read' } }">
        Ver livros lidos na biblioteca
      </RouterLink>
    </aside>

    <CountryList
      v-if="current"
      :countries="current.countries"
      :selected-iso="selectedIso"
      @select="setSelected"
    />

    <footer class="map-page__credits">
      Fronteiras:
      <a href="https://www.naturalearthdata.com/" target="_blank" rel="noopener noreferrer"
        >Natural Earth</a
      >
      (domínio público, 1:110m). Dados de livros e autores: Google Books, Open Library e Wikidata,
      com as correções manuais registradas por você. As fronteiras são simplificadas e não expressam
      posição política.
    </footer>
  </div>
</template>

<style scoped>
.map-page {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
  padding: var(--space-5);
}
@media (max-width: 899px) {
  .map-page {
    padding: var(--space-4);
  }
}
.map-page__header {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  justify-content: space-between;
  gap: var(--space-3);
}
.map-page__summary {
  margin-top: var(--space-1);
  font-size: var(--text-sm);
}
.link-button {
  color: var(--color-sky);
  text-decoration: underline;
  text-underline-offset: 3px;
}
.map-page__main {
  --stage-height: clamp(380px, calc(100dvh - 230px), 820px);
  position: relative;
}
/* Desktop: o painel ocupa uma coluna ao lado do mapa (o mapa reenquadra, nada fica coberto). */
@media (min-width: 900px) {
  .map-page__main--panel {
    display: grid;
    grid-template-columns: minmax(0, 1fr) 380px;
    gap: var(--space-3);
  }
}
.map-page__stage {
  position: relative;
  height: var(--stage-height);
  border-radius: var(--radius-xl);
  border: 1px solid var(--color-hairline);
  overflow: hidden;
  box-shadow: var(--shadow-lg);
}
@media (max-width: 899px) {
  .map-page__main {
    /* Em paisagem no celular o palco não passa da altura visível. */
    --stage-height: min(clamp(260px, calc(56vw + 90px), 520px), max(220px, calc(100dvh - 160px)));
  }
}
.map-page__notice {
  position: absolute;
  top: var(--space-3);
  left: 50%;
  transform: translateX(-50%);
  width: max-content;
  max-width: calc(100% - 2 * var(--space-4));
  padding: var(--space-2) var(--space-4);
  border-radius: var(--radius-md);
  background: rgb(12 22 38 / 0.92);
  border: 1px solid rgb(242 196 109 / 0.35);
  color: var(--color-warning);
  font-size: var(--text-sm);
}
.map-page__empty {
  position: absolute;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
  width: min(420px, calc(100% - 2 * var(--space-4)));
  display: grid;
  gap: var(--space-3);
  padding: var(--space-5);
  text-align: center;
  border-radius: var(--radius-lg);
  background: rgb(10 18 32 / 0.92);
  border: 1px solid var(--color-border);
  box-shadow: var(--shadow-lg);
}
.map-page__empty-actions :deep(.btn) {
  flex: 1 1 auto;
}
@media (max-width: 480px) {
  .map-page__empty {
    gap: var(--space-2);
    padding: var(--space-3);
  }
  .map-page__empty p {
    font-size: var(--text-sm);
  }
}
.map-page__empty h2 {
  font-size: var(--text-xl);
}
.map-page__empty-actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: var(--space-2);
}
.map-page__legend {
  position: absolute;
  left: var(--space-4);
  bottom: var(--space-4);
  max-width: calc(100% - 2 * var(--space-4));
}
.tooltip {
  position: absolute;
  top: 0;
  left: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: var(--space-2) var(--space-3);
  border-radius: var(--radius-md);
  background: rgb(12 22 38 / 0.95);
  border: 1px solid var(--color-border);
  box-shadow: var(--shadow-md);
  font-size: var(--text-sm);
  pointer-events: none;
  white-space: nowrap;
  transition: transform 60ms linear;
}
.tooltip strong {
  font-family: var(--font-display);
  font-weight: 500;
  font-size: var(--text-base);
}
.map-page__unknown {
  display: grid;
  gap: var(--space-1);
  padding: var(--space-3) var(--space-4);
  border-radius: var(--radius-md);
  background: var(--color-surface);
  border: 1px solid var(--color-hairline);
  font-size: var(--text-sm);
}
.map-page__credits {
  font-size: var(--text-xs);
  color: var(--color-text-faint);
  max-width: 80ch;
}
.map-page__credits a {
  color: var(--color-text-muted);
}

.panel-enter-active,
.panel-leave-active {
  transition:
    transform var(--duration-slow, 280ms) var(--ease-out),
    opacity var(--duration-base) var(--ease-out);
}
.panel-enter-from,
.panel-leave-to {
  opacity: 0;
  transform: translateX(24px);
}
@media (max-width: 899px) {
  .panel-enter-from,
  .panel-leave-to {
    transform: translateY(32px);
  }
}
@media (prefers-reduced-motion: reduce) {
  .panel-enter-active,
  .panel-leave-active {
    transition: none;
  }
}
</style>
