<script setup lang="ts">
/**
 * Mapa interativo + painel do país + lista de países, a partir de uma `MapSource` abstrata.
 * Usado pela página do mapa próprio (`MapPage`) e pelo mapa de um perfil público (`profile`):
 * a lógica (`useMapView`) e a visualização são as mesmas; só mudam a fonte de dados, o painel
 * (resenhas, sem link de biblioteca) e o texto de "sem livros". Não conhece rotas nem stores.
 */
import { computed, ref } from 'vue'
import type { RouteLocationRaw } from 'vue-router'
import { getErrorMessage } from '@/api/errors'
import type { MapSource } from '@/composables/mapSource'
import { useMapView } from '@/composables/useMapView'
import type { MapStatusFilter, MapType } from '@/types/api'
import { MAP_TYPE_LABELS, pluralize } from '@/utils/labels'
import { booksCountLabel } from '@/utils/mapStatus'
import CountryList from '@/components/map/CountryList.vue'
import CountryPanel from '@/components/map/CountryPanel.vue'
import MapLegend from '@/components/map/MapLegend.vue'
import WorldMap from '@/components/map/WorldMap.vue'

const props = withDefaults(
  defineProps<{
    source: MapSource
    mapType: MapType
    status?: MapStatusFilter
    /** País pedido (ex.: `?country=`), já normalizado. */
    requestedIso: string | null
    /** Mapa de terceiros: painel com resenhas, sem links para a biblioteca. */
    profile?: boolean
    /** Link "ver na biblioteca os livros sem país" (só no mapa próprio). */
    libraryLink?: RouteLocationRaw | null
  }>(),
  { status: 'Read', profile: false, libraryLink: null },
)

const emit = defineEmits<{ select: [iso: string | null] }>()

const mapTypeRef = computed(() => props.mapType)
const statusRef = computed(() => props.status)
const requestedRef = computed(() => props.requestedIso)

const view = useMapView({
  source: props.source,
  mapType: mapTypeRef,
  status: statusRef,
  requestedIso: requestedRef,
  onSelect: (iso) => emit('select', iso),
})
const {
  current,
  shown,
  loading,
  error,
  hover,
  selectedDatum,
  selectedIso,
  detail,
  detailLoading,
  detailError,
  hasNoBooks,
} = view

const stage = ref<HTMLElement | null>(null)

const tooltipStyle = computed(() => {
  if (!hover.value) return {}
  const width = stage.value?.clientWidth ?? 0
  const { x, y } = hover.value.point
  const flip = x > width - 200
  return {
    transform: `translate(${flip ? `calc(${x - 14}px - 100%)` : `${x + 14}px`}, ${y + 14}px)`,
  }
})

const unknownCopy = computed(() =>
  props.mapType === 'Authors'
    ? 'de autor sem nacionalidade conhecida'
    : 'de obra sem origem conhecida',
)
</script>

<template>
  <div class="explorer">
    <div class="explorer__main" :class="{ 'explorer__main--panel': !!selectedDatum }">
      <div ref="stage" class="explorer__stage" data-keep-panel :aria-busy="loading">
        <WorldMap
          :data="(shown ?? current)?.countries ?? []"
          :max-book-count="(shown ?? current)?.maxBookCount ?? 0"
          :selected-iso="selectedIso"
          :label="MAP_TYPE_LABELS[mapType]"
          @country-hover="hover = $event"
          @country-select="view.onMapSelect"
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
        <div v-if="error && !current" class="explorer__notice" role="status">
          Não foi possível carregar os dados do mapa ({{ getErrorMessage(error) }}). O mapa é
          exibido sem destaques.
          <button type="button" class="link-button" @click="view.retryMap()">
            Tentar novamente
          </button>
        </div>
        <div v-if="hasNoBooks" class="explorer__empty">
          <slot name="empty" />
        </div>
        <div class="explorer__legend">
          <MapLegend :max-book-count="(shown ?? current)?.maxBookCount ?? 0" />
        </div>
      </div>

      <Transition name="panel">
        <CountryPanel
          v-if="selectedDatum"
          :map-type="mapType"
          :summary="selectedDatum"
          :detail="detail"
          :loading="detailLoading || (!detail && !detailError)"
          :error="detailError"
          @close="emit('select', null)"
          @retry="view.retryDetail()"
        />
      </Transition>
    </div>

    <aside
      v-if="current && current.unknownBookCount > 0 && !hasNoBooks"
      class="explorer__unknown"
      role="note"
    >
      <strong>
        {{ booksCountLabel(current.unknownBookCount, status) }}
        {{ unknownCopy }}.
      </strong>
      <span class="muted">
        Dado ausente não é país inexplorado: ainda não encontramos essa informação, então esses
        livros ficam de fora da contagem deste mapa até que ela seja descoberta ou corrigida.
      </span>
      <RouterLink v-if="libraryLink" :to="libraryLink">
        Ver na biblioteca os livros sem país
      </RouterLink>
    </aside>

    <CountryList
      v-if="current"
      :countries="current.countries"
      :selected-iso="selectedIso"
      @select="emit('select', $event)"
    />

    <footer class="explorer__credits">
      Fronteiras:
      <a href="https://www.naturalearthdata.com/" target="_blank" rel="noopener noreferrer"
        >Natural Earth</a
      >
      (domínio público, 1:50m). Dados de livros e autores: Google Books, Open Library e Wikidata,
      com as correções manuais registradas
      {{ profile ? 'pelo dono do perfil' : 'por você' }}. As fronteiras são simplificadas e não
      expressam posição política.
    </footer>
  </div>
</template>

<style scoped>
.explorer {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}
.link-button {
  color: var(--color-sky);
  text-decoration: underline;
  text-underline-offset: 3px;
}
.explorer__main {
  --stage-height: clamp(380px, calc(100dvh - 230px), 820px);
  position: relative;
}
/* Desktop: o painel ocupa uma coluna ao lado do mapa (o mapa reenquadra, nada fica coberto). */
@media (min-width: 900px) {
  .explorer__main--panel {
    display: grid;
    grid-template-columns: minmax(0, 1fr) 380px;
    gap: var(--space-3);
  }
}
.explorer__stage {
  position: relative;
  height: var(--stage-height);
  border-radius: var(--radius-xl);
  border: 1px solid var(--color-hairline);
  overflow: hidden;
  box-shadow: var(--shadow-lg);
}
@media (max-width: 899px) {
  .explorer__main {
    /* Em paisagem no celular o palco não passa da altura visível. */
    --stage-height: min(clamp(260px, calc(56vw + 90px), 520px), max(220px, calc(100dvh - 160px)));
  }
}
.explorer__notice {
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
.explorer__empty {
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
/* Conteúdo vindo do slot `empty` (título, texto e `.empty-actions`). */
.explorer__empty :slotted(.empty-actions) {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: var(--space-2);
}
@media (max-width: 480px) {
  .explorer__empty {
    gap: var(--space-2);
    padding: var(--space-3);
  }
  .explorer__empty :slotted(p) {
    font-size: var(--text-sm);
  }
}
.explorer__empty :slotted(h2) {
  font-size: var(--text-xl);
}
.explorer__legend {
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
.explorer__unknown {
  display: grid;
  gap: var(--space-1);
  padding: var(--space-3) var(--space-4);
  border-radius: var(--radius-md);
  background: var(--color-surface);
  border: 1px solid var(--color-hairline);
  font-size: var(--text-sm);
}
.explorer__credits {
  font-size: var(--text-xs);
  color: var(--color-text-faint);
  max-width: 80ch;
}
.explorer__credits a {
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
