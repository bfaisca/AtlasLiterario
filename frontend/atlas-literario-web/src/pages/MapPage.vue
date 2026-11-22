<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useOwnMapSource } from '@/composables/mapSource'
import type { MapStatusFilter, MapType } from '@/types/api'
import { MAP_TYPE_LABELS, pluralize } from '@/utils/labels'
import { fromRouteMapType, toRouteMapType } from '@/utils/mapType'
import { booksCountLabel, mapStatusToQuery, parseMapStatusQuery } from '@/utils/mapStatus'
import { parseCountryQuery } from '@/utils/mapSelection'
import BaseButton from '@/components/ui/BaseButton.vue'
import MapExplorer from '@/components/map/MapExplorer.vue'
import MapStatusToggle from '@/components/map/MapStatusToggle.vue'
import MapTypeToggle from '@/components/map/MapTypeToggle.vue'

/**
 * Página do mapa próprio. A URL é a fonte da verdade: `/map/authors|origins?country=ISO&status=`.
 * O mapa, o painel do país e a lista ficam em `MapExplorer` (compartilhado com o mapa de perfis
 * públicos); aqui ficam a rota, os controles (tipo e status) e o cabeçalho.
 */
const route = useRoute()
const router = useRouter()
const source = useOwnMapSource()

const mapType = computed<MapType>({
  get: () => fromRouteMapType(route.params.type),
  set: (value) =>
    void router.replace({
      name: 'map',
      params: { type: toRouteMapType(value) },
      query: route.query, // mantém o país selecionado quando existir nos dois mapas
    }),
})

/** `?status=` (omitido = Lido). Trocar o status mantém tipo e país selecionado. */
const status = computed<MapStatusFilter>({
  get: () => parseMapStatusQuery(route.query.status),
  set: (value) => {
    const query = { ...route.query }
    const param = mapStatusToQuery(value)
    if (param) query.status = param
    else delete query.status
    void router.replace({ query })
  },
})
const isRead = computed(() => status.value === 'Read')
const current = computed(() => source.data(mapType.value, status.value))
const loading = computed(() => source.loading(mapType.value, status.value))
const requestedIso = computed(() => parseCountryQuery(route.query.country))

function setSelected(iso: string | null) {
  const query = { ...route.query }
  if (iso) query.country = iso
  else delete query.country
  void router.replace({ query })
}

const libraryLink = computed(() => ({
  name: 'library',
  query: {
    ...(status.value === 'All' ? {} : { status: status.value }),
    geoMissing: mapType.value === 'Authors' ? 'author' : 'origin',
  },
}))

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
            {{ booksCountLabel(current.totalReadBooks, status) }}
          </template>
          <template v-else-if="loading">Carregando…</template>
          <template v-if="current?.countries.length">
            ·
            <button type="button" class="link-button" @click="goToList">Ver lista de países</button>
          </template>
        </p>
      </div>
      <div class="map-page__controls" data-keep-panel>
        <MapStatusToggle v-model="status" />
        <MapTypeToggle v-model="mapType" />
      </div>
    </header>

    <p v-if="!isRead" class="map-page__scope" data-testid="status-scope-note" role="status">
      Visualizando livros que ainda não foram lidos: o Passaporte e as estatísticas contam apenas
      livros lidos.
    </p>

    <MapExplorer
      :source="source"
      :map-type="mapType"
      :status="status"
      :requested-iso="requestedIso"
      :library-link="libraryLink"
      @select="setSelected"
    >
      <template #empty>
        <template v-if="isRead">
          <h2>Nenhum livro lido ainda</h2>
          <p class="muted">
            Os países se acendem conforme você conclui leituras. Marque um livro como “Lido” para
            começar a colorir o mapa.
          </p>
        </template>
        <template v-else>
          <h2>Nenhum livro neste filtro</h2>
          <p class="muted">Experimente outro status para ver os países de outras leituras.</p>
        </template>
        <div class="empty-actions">
          <BaseButton :to="{ name: 'library' }">Ir para a biblioteca</BaseButton>
          <BaseButton variant="secondary" :to="{ name: 'search' }">Buscar livros</BaseButton>
        </div>
      </template>
    </MapExplorer>
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
.map-page__controls {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: flex-end;
  gap: var(--space-2);
  max-width: 100%;
}
.map-page__scope {
  padding: var(--space-2) var(--space-3);
  border-radius: var(--radius-md);
  background: var(--color-brass-soft);
  border: 1px solid rgb(227 191 124 / 0.25);
  color: var(--color-brass-strong);
  font-size: var(--text-xs);
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
.empty-actions :deep(.btn) {
  flex: 1 1 auto;
}
</style>
