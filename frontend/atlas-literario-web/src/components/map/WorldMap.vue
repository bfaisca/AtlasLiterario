<script setup lang="ts">
/**
 * Mapa-múndi coroplético (MapLibre GL JS, sem tiles: um único source GeoJSON).
 * Agnóstico de regras de negócio: recebe `data` (país → contagens) e `maxBookCount`,
 * pinta via feature-state com a escala de `useColorScale` e emite eventos de interação.
 */
import 'maplibre-gl/dist/maplibre-gl.css'
import {
  getGlobalDispatcher,
  Map as MapLibreMap,
  setWorkerUrl,
  type Subscription,
  type MapLayerMouseEvent,
  type StyleSpecification,
} from 'maplibre-gl'
import workerUrl from 'maplibre-gl/dist/maplibre-gl-worker.mjs?url'
import { computed, nextTick, onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'
import { colorForCount, mixColors, UNEXPLORED_COLOR } from '@/composables/useColorScale'
import AppIcon from '@/components/ui/AppIcon.vue'
import { useCountriesGeo, type CountryFeatureProperties } from '@/composables/useCountriesGeo'
import BaseSpinner from '@/components/ui/BaseSpinner.vue'
import EmptyState from '@/components/ui/EmptyState.vue'
import type { CountryHoverEvent, MapCountryDatum } from './types'

setWorkerUrl(workerUrl)

const props = withDefaults(
  defineProps<{
    data: readonly MapCountryDatum[]
    maxBookCount: number
    selectedIso?: string | null
    interactive?: boolean
    /** Rótulo acessível do mapa */
    label?: string
  }>(),
  { selectedIso: null, interactive: true, label: 'Mapa-múndi de leituras' },
)

const emit = defineEmits<{
  'country-hover': [event: CountryHoverEvent | null]
  'country-select': [iso: string | null]
  ready: []
  error: []
}>()

const SOURCE_ID = 'countries'
const OCEAN = '#060b14'
const BORDER = 'rgba(160,190,230,0.18)'
const SELECTED_BORDER = '#e6f1ff'
/** Mundo habitado (sem a Antártida). O `fitBounds` ajusta o zoom a qualquer proporção. */
const WORLD_BOUNDS: [[number, number], [number, number]] = [
  [-168, -56],
  [190, 83],
]
const FIT_PADDING = 8
const COLOR_TRANSITION_MS = 380

const container = ref<HTMLDivElement | null>(null)
const map = shallowRef<MapLibreMap | null>(null)
/** Falha do motor de mapa: WebGL2 ausente ou worker/estilo que não carregou. */
const mapError = ref<'webgl' | 'render' | null>(null)
const LOAD_TIMEOUT_MS = 15_000
let loadTimer: ReturnType<typeof setTimeout> | null = null
let workerErrorSubscription: Subscription | null = null

const ERROR_MESSAGES = {
  webgl: 'Seu navegador não suporta WebGL2, necessário para o mapa interativo.',
  render:
    'O motor do mapa não pôde ser iniciado (falha ao carregar o worker). Tente recarregar a página.',
  geo: 'Falha ao carregar as fronteiras dos países. Tente recarregar a página.',
} as const
const { status, load } = useCountriesGeo()

const byIso = computed(() => new Map(props.data.map((d) => [d.iso, d])))
const namesByIso = new Map<string, string>()
let hoveredIso: string | null = null
let resizeObserver: ResizeObserver | null = null
/** Cor atualmente pintada por país (base da transição entre mapas). */
const painted = new Map<string, string>()
let tweenFrame: number | null = null
/** Depois que o usuário move/zoom, resize e orientação não reenquadram mais o mapa. */
let userMoved = false

const prefersReducedMotion = () =>
  typeof window !== 'undefined' && !!window.matchMedia?.('(prefers-reduced-motion: reduce)').matches

const isCoarsePointer = () =>
  typeof window !== 'undefined' && !!window.matchMedia?.('(pointer: coarse)').matches
const isTouch = () => typeof window !== 'undefined' && window.matchMedia?.('(hover: none)').matches

const STYLE: StyleSpecification = {
  version: 8,
  sources: {},
  layers: [{ id: 'ocean', type: 'background', paint: { 'background-color': OCEAN } }],
}

/**
 * Enquadra o mundo habitado no contêiner atual. O zoom mínimo acompanha o enquadramento:
 * em telas estreitas o zoom que mostra o mundo todo é menor que o mínimo fixo e cortaria as Américas.
 */
function fit(duration = 0) {
  const instance = map.value
  if (!instance) return
  const camera = instance.cameraForBounds(WORLD_BOUNDS, { padding: FIT_PADDING })
  if (camera?.zoom !== undefined) instance.setMinZoom(Math.max(-3, camera.zoom - 0.3))
  instance.fitBounds(WORLD_BOUNDS, { padding: FIT_PADDING, duration })
}

/** Enquadra o mundo no contêiner atual; o zoom mínimo acompanha o enquadramento. */
function recenter(animated = true) {
  const instance = map.value
  if (!instance) return
  userMoved = false
  fit(animated && !prefersReducedMotion() ? 500 : 0)
}

function zoomBy(direction: 1 | -1) {
  userMoved = true
  const instance = map.value
  if (!instance) return
  if (direction === 1) instance.zoomIn()
  else instance.zoomOut()
}

function paint(iso: string, color: string, count: number) {
  map.value?.setFeatureState({ source: SOURCE_ID, id: iso }, { fill: color, count })
  if (color === UNEXPLORED_COLOR && count === 0) painted.delete(iso)
  else painted.set(iso, color)
}

/**
 * Recolore via feature-state (o source nunca é recriado). Quando os dados mudam
 * (ex.: Autores ↔ Origens) as cores são interpoladas, exceto com `prefers-reduced-motion`.
 */
function applyData() {
  const instance = map.value
  if (!instance?.getSource(SOURCE_ID)) return
  if (tweenFrame !== null) cancelAnimationFrame(tweenFrame)
  tweenFrame = null

  const target = new Map(
    props.data.map((d) => [
      d.iso,
      { color: colorForCount(d.bookCount, props.maxBookCount), count: d.bookCount },
    ]),
  )
  const from = new Map(painted)
  const isos = new Set([...from.keys(), ...target.keys()])
  const animate = from.size > 0 && !prefersReducedMotion()

  const step = (t: number) => {
    for (const iso of isos) {
      const to = target.get(iso)
      const toColor = to?.color ?? UNEXPLORED_COLOR
      const fromColor = from.get(iso) ?? UNEXPLORED_COLOR
      paint(iso, t >= 1 ? toColor : mixColors(fromColor, toColor, t), to?.count ?? 0)
    }
  }

  if (!animate) {
    step(1)
  } else {
    const startedAt = performance.now()
    const tick = (now: number) => {
      const t = Math.min(1, (now - startedAt) / COLOR_TRANSITION_MS)
      step(t * (2 - t)) // ease-out
      tweenFrame = t < 1 ? requestAnimationFrame(tick) : null
    }
    tweenFrame = requestAnimationFrame(tick)
  }
  if (props.selectedIso)
    instance.setFeatureState({ source: SOURCE_ID, id: props.selectedIso }, { selected: true })
  if (hoveredIso) instance.setFeatureState({ source: SOURCE_ID, id: hoveredIso }, { hover: true })
}

function setHover(iso: string | null) {
  const instance = map.value
  if (!instance || hoveredIso === iso) return
  if (hoveredIso) instance.setFeatureState({ source: SOURCE_ID, id: hoveredIso }, { hover: false })
  hoveredIso = iso
  if (iso) instance.setFeatureState({ source: SOURCE_ID, id: iso }, { hover: true })
}

function hoverEvent(event: MapLayerMouseEvent): CountryHoverEvent | null {
  const feature = event.features?.[0]
  const iso = typeof feature?.id === 'string' ? feature.id : null
  if (!iso) return null
  const datum = byIso.value.get(iso) ?? null
  return {
    iso,
    name: datum?.name ?? namesByIso.get(iso) ?? iso,
    datum,
    point: { x: event.point.x, y: event.point.y },
  }
}

async function initMap() {
  const geo = await load()
  // O contêiner só fica visível (v-show) depois do próximo render: sem isso o mapa nasceria
  // com tamanho 0 e o enquadramento inicial sairia errado (Américas cortadas em retrato).
  await nextTick()
  if (!geo || !container.value) return

  for (const feature of geo.features) {
    const p: CountryFeatureProperties = feature.properties
    namesByIso.set(p.iso, p.namePt ?? p.name)
  }

  let instance: MapLibreMap
  try {
    instance = new MapLibreMap({
      container: container.value,
      style: STYLE,
      center: [12, 22],
      zoom: 0.9,
      minZoom: -3, // ajustado a cada enquadramento (ver `fit`)
      maxZoom: 6,
      renderWorldCopies: false,
      dragRotate: false,
      pitchWithRotate: false,
      touchPitch: false,
      interactive: props.interactive,
      // No toque, um dedo rola a página (o mapa fica entre a lista e o cabeçalho); dois dedos movem o mapa.
      cooperativeGestures: props.interactive && isCoarsePointer(),
      locale: {
        'CooperativeGesturesHandler.MobileHelpText': 'Use dois dedos para mover o mapa',
        'CooperativeGesturesHandler.WindowsHelpText': 'Use Ctrl + rolagem para ampliar o mapa',
        'CooperativeGesturesHandler.MacHelpText': 'Use ⌘ + rolagem para ampliar o mapa',
      },
      attributionControl: { compact: true, customAttribution: 'Natural Earth' },
      // Necessário para exportar o canvas como imagem (compartilhamento).
      canvasContextAttributes: { preserveDrawingBuffer: true, antialias: true },
    })
  } catch {
    mapError.value = 'webgl' // WebGL2 indisponível
    return
  }
  map.value = instance
  instance.touchZoomRotate.disableRotation()

  // Erros antes do 'load' (ex.: estilo inválido) deixariam o mapa em branco.
  let loaded = false
  const fail = (error: unknown) => {
    if (mapError.value) return
    console.error('[WorldMap] falha ao iniciar o mapa', error)
    mapError.value = 'render'
    emit('error')
  }
  instance.on('error', (event) => {
    if (!loaded) fail(event.error)
  })
  // A falha do worker (ex.: .mjs servido com MIME errado) é emitida no dispatcher global, não no
  // mapa, e pode ocorrer DEPOIS do 'load' (o estilo vazio não precisa do worker; o GeoJSON sim).
  workerErrorSubscription = getGlobalDispatcher().on('error', (event) => fail(event.error))
  // Rede de segurança: se o GeoJSON não carregar a tempo (precisa do worker), mostra o erro.
  loadTimer = setTimeout(() => {
    const ready = !!instance.getSource(SOURCE_ID) && instance.isSourceLoaded(SOURCE_ID)
    if (!ready) fail(new Error('Tempo esgotado ao carregar o mapa'))
  }, LOAD_TIMEOUT_MS)

  instance.on('load', () => {
    loaded = true
    // promoteId: o código ISO vira o id da feature (usado em setFeatureState).
    const source = { type: 'geojson', data: geo, promoteId: 'iso' }
    instance.addSource(SOURCE_ID, source as unknown as Parameters<MapLibreMap['addSource']>[1])
    instance.addLayer({
      id: 'countries-fill',
      type: 'fill',
      source: SOURCE_ID,
      paint: {
        'fill-color': ['to-color', ['coalesce', ['feature-state', 'fill'], UNEXPLORED_COLOR]],
        'fill-opacity': ['case', ['boolean', ['feature-state', 'hover'], false], 1, 0.94],
      },
    })
    instance.addLayer({
      id: 'countries-border',
      type: 'line',
      source: SOURCE_ID,
      paint: { 'line-color': BORDER, 'line-width': 0.5 },
    })
    instance.addLayer({
      id: 'countries-highlight',
      type: 'line',
      source: SOURCE_ID,
      paint: {
        'line-color': SELECTED_BORDER,
        'line-width': 1.5,
        'line-opacity': [
          'case',
          ['boolean', ['feature-state', 'selected'], false],
          1,
          ['boolean', ['feature-state', 'hover'], false],
          0.55,
          0,
        ],
      },
    })
    applyData()
    // Enquadra o mundo habitado em qualquer proporção de tela (inclusive retrato no mobile).
    fit(0)
    instance.on('movestart', (event) => {
      if (event.originalEvent) userMoved = true
    })
    emit('ready')
  })

  if (props.interactive) {
    instance.on('mousemove', 'countries-fill', (event) => {
      if (isTouch()) return
      const hover = hoverEvent(event)
      setHover(hover?.iso ?? null)
      instance.getCanvas().style.cursor = hover ? 'pointer' : ''
      emit('country-hover', hover)
    })
    instance.on('mouseleave', 'countries-fill', () => {
      setHover(null)
      instance.getCanvas().style.cursor = ''
      emit('country-hover', null)
    })
    instance.on('click', (event) => {
      const features = instance.queryRenderedFeatures(event.point, { layers: ['countries-fill'] })
      const iso = typeof features[0]?.id === 'string' ? features[0].id : null
      emit('country-select', iso)
      // Em toque não há hover: o toque seleciona (borda + painel); sem tooltip.
      if (isTouch()) setHover(null)
    })
  }

  // Resize/orientação: reenquadra enquanto o usuário não tiver movido o mapa.
  resizeObserver = new ResizeObserver(() => {
    instance.resize()
    if (!userMoved) fit(0)
  })
  resizeObserver.observe(container.value)
}

watch(() => [props.data, props.maxBookCount] as const, applyData)

watch(
  () => props.selectedIso,
  (iso, previous) => {
    const instance = map.value
    if (!instance?.getSource(SOURCE_ID)) return
    if (previous) instance.setFeatureState({ source: SOURCE_ID, id: previous }, { selected: false })
    if (iso) instance.setFeatureState({ source: SOURCE_ID, id: iso }, { selected: true })
  },
)

onMounted(initMap)

onBeforeUnmount(() => {
  if (loadTimer) clearTimeout(loadTimer)
  if (tweenFrame !== null) cancelAnimationFrame(tweenFrame)
  workerErrorSubscription?.unsubscribe()
  resizeObserver?.disconnect()
  map.value?.remove()
  map.value = null
})

defineExpose({
  /** Canvas WebGL (preserveDrawingBuffer) para composição da imagem de compartilhamento. */
  getCanvas: (): HTMLCanvasElement | null => map.value?.getCanvas() ?? null,
  recenter,
})
</script>

<template>
  <div class="world-map" role="region" :aria-label="label">
    <div v-show="status === 'ready' && !mapError" ref="container" class="world-map__canvas" />

    <div
      v-if="interactive && status === 'ready' && !mapError"
      class="world-map__controls"
      role="group"
      aria-label="Controles do mapa"
    >
      <button type="button" aria-label="Ampliar" title="Ampliar" @click="zoomBy(1)">
        <AppIcon name="plus" :size="18" />
      </button>
      <button type="button" aria-label="Reduzir" title="Reduzir" @click="zoomBy(-1)">
        <AppIcon name="minus" :size="18" />
      </button>
      <button type="button" aria-label="Recentrar mapa" title="Recentrar" @click="recenter()">
        <AppIcon name="recenter" :size="18" />
      </button>
    </div>

    <div v-if="status === 'loading' || status === 'idle'" class="world-map__state">
      <BaseSpinner size="lg" label="Carregando mapa" />
    </div>
    <div v-else-if="status === 'missing'" class="world-map__state">
      <EmptyState
        icon="globe"
        title="O mapa-múndi ainda não está disponível"
        description="O arquivo de fronteiras dos países não foi encontrado. Execute “npm run geo:fetch” no frontend para baixá-lo (Natural Earth, domínio público)."
      />
    </div>
    <div v-else-if="status === 'error' || mapError" class="world-map__state">
      <EmptyState
        icon="alert"
        title="Não foi possível exibir o mapa"
        :description="ERROR_MESSAGES[mapError ?? 'geo']"
      />
    </div>
  </div>
</template>

<style scoped>
.world-map {
  position: relative;
  width: 100%;
  height: 100%;
  min-height: 280px;
  background: var(--map-ocean);
  border-radius: inherit;
  overflow: hidden;
}
.world-map__canvas {
  position: absolute;
  inset: 0;
}
.world-map__state {
  position: absolute;
  inset: 0;
  display: grid;
  place-items: center;
  padding: var(--space-5);
}
.world-map__controls {
  position: absolute;
  top: var(--space-3);
  left: var(--space-3);
  display: flex;
  flex-direction: column;
  border-radius: var(--radius-md);
  overflow: hidden;
  background: rgb(6 11 20 / 0.82);
  border: 1px solid var(--color-border);
  backdrop-filter: blur(6px);
}
.world-map__controls button {
  display: grid;
  place-items: center;
  width: 36px;
  height: 36px;
  color: var(--color-text-muted);
  transition:
    background-color var(--duration-base) var(--ease-out),
    color var(--duration-base) var(--ease-out);
}
.world-map__controls button + button {
  border-top: 1px solid var(--color-hairline);
}
.world-map__controls button:hover {
  background: var(--color-surface-2);
  color: var(--color-text);
}
.world-map__controls button:focus-visible {
  outline-offset: -2px;
}
.world-map :deep(.maplibregl-cooperative-gesture-screen) {
  font-family: var(--font-body);
}
.world-map :deep(.maplibregl-ctrl-attrib) {
  background: rgb(6 11 20 / 0.7);
  color: var(--color-text-faint);
  font-size: 10px;
}
.world-map :deep(.maplibregl-ctrl-attrib a) {
  color: var(--color-text-muted);
}
.world-map :deep(.maplibregl-ctrl-attrib-button) {
  filter: invert(0.8);
}
</style>
