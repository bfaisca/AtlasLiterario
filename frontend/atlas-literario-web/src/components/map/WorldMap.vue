<script setup lang="ts">
/**
 * Mapa-múndi coroplético (MapLibre GL JS, sem tiles: sources GeoJSON locais).
 * Agnóstico de regras de negócio: recebe `data` (país → contagens) e `maxBookCount`,
 * pinta via feature-state com a escala de `useColorScale` e emite eventos de interação.
 *
 * Dois sources compartilham o `iso` como id: os polígonos (`countries`) e os marcadores
 * (`country-points`) de países/territórios pequenos demais para serem vistos como polígono.
 * Ambos recebem o MESMO estado (fill, count, hover, selected) em `setState`.
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
import {
  loadCountryPoints,
  useCountriesGeo,
  type CountryFeatureProperties,
} from '@/composables/useCountriesGeo'
import { buildFocusIndex, type FocusTarget } from '@/utils/mapFocus'
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
const POINTS_SOURCE_ID = 'country-points'
const FILL_LAYER = 'countries-fill'
const POINTS_HIT_LAYER = 'country-points-hit'
const OCEAN = '#060b14'
const BORDER = 'rgba(160,190,230,0.18)'
const SELECTED_BORDER = '#e6f1ff'
/** Mundo habitado (sem a Antártida). O `fitBounds` ajusta o zoom a qualquer proporção. */
const WORLD_BOUNDS: [[number, number], [number, number]] = [
  [-168, -56],
  [190, 83],
]
const FIT_PADDING = 8
const MAX_ZOOM = 6
/** Foco ao selecionar um país: duração e zoom máximo do enquadramento de polígonos. */
const FOCUS_DURATION_MS = 900
const FOCUS_MAX_ZOOM = 5
const FOCUS_PADDING = 64
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
/** Países com marcador: o estado de pintura é espelhado também no source de pontos. */
const pointIsos = new Set<string>()
let focusIndex = new Map<string, FocusTarget>()
/** Camadas que respondem a hover/clique, de cima para baixo (marcadores acima dos polígonos). */
let interactiveLayers: string[] = [FILL_LAYER]
let hoveredIso: string | null = null
let previousHoverEmitted = false
let resizeObserver: ResizeObserver | null = null
/** Cor atualmente pintada por país (base da transição entre mapas). */
const painted = new Map<string, string>()
let tweenFrame: number | null = null
/** Depois que o usuário move/zoom, resize e orientação não reenquadram mais o mapa. */
let userMoved = false
/** País em foco: resize/painel lateral reenquadram nele (não no mundo) até o usuário mover o mapa. */
let focusedIso: string | null = null

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
  focusedIso = null
  fit(animated && !prefersReducedMotion() ? 500 : 0)
}

function zoomBy(direction: 1 | -1) {
  userMoved = true
  focusedIso = null
  const instance = map.value
  if (!instance) return
  if (direction === 1) instance.zoomIn()
  else instance.zoomOut()
}

/** Aplica o estado a um país nos dois sources (polígono e, quando existir, marcador). */
function setState(iso: string, state: Record<string, unknown>) {
  const instance = map.value
  if (!instance) return
  instance.setFeatureState({ source: SOURCE_ID, id: iso }, state)
  if (pointIsos.has(iso)) instance.setFeatureState({ source: POINTS_SOURCE_ID, id: iso }, state)
}

function paint(iso: string, color: string, count: number) {
  setState(iso, { fill: color, count })
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
  if (props.selectedIso) setState(props.selectedIso, { selected: true })
  if (hoveredIso) setState(hoveredIso, { hover: true })
}

function setHover(iso: string | null) {
  const instance = map.value
  if (!instance || hoveredIso === iso) return
  if (hoveredIso) setState(hoveredIso, { hover: false })
  hoveredIso = iso
  if (iso) setState(iso, { hover: true })
}

function clearHover() {
  setHover(null)
  const canvas = map.value?.getCanvas()
  if (canvas) canvas.style.cursor = ''
  if (previousHoverEmitted) emit('country-hover', null)
  previousHoverEmitted = false
}

/** País sob o cursor: marcador (acima) ou polígono. */
function isoAt(point: { x: number; y: number }): string | null {
  const instance = map.value
  if (!instance) return null
  // As camadas só existem depois do 'load' + addLayer; o mouse pode se mover antes disso
  // (primeira carga mais lenta) e queryRenderedFeatures lançaria erro por camada inexistente.
  const layers = interactiveLayers.filter((id) => instance.getLayer(id))
  if (!layers.length) return null
  const feature = instance.queryRenderedFeatures([point.x, point.y], { layers })[0]
  return typeof feature?.id === 'string' ? feature.id : null
}

function hoverAt(point: { x: number; y: number }): CountryHoverEvent | null {
  const iso = isoAt(point)
  if (!iso) return null
  const datum = byIso.value.get(iso) ?? null
  return { iso, name: datum?.name ?? namesByIso.get(iso) ?? iso, datum, point }
}

/**
 * Leva a câmera ao país selecionado: ponto (ilhas/territórios minúsculos) com zoom fixo, ou
 * enquadramento do território principal com padding e zoom máximo. Respeita reduced-motion.
 */
function focusCountry(iso: string, animated = true) {
  const instance = map.value
  const target = focusIndex.get(iso)
  if (!instance || !target) return
  userMoved = true // resize/orientação não devem desfazer o foco
  focusedIso = iso
  instance.resize() // o painel do país pode ter mudado o tamanho do contêiner
  const duration = animated && !prefersReducedMotion() ? FOCUS_DURATION_MS : 0
  if (target.kind === 'point') {
    instance.flyTo({ center: target.center, zoom: Math.min(target.zoom, MAX_ZOOM), duration })
    return
  }
  const { clientWidth, clientHeight } = instance.getContainer()
  const padding = Math.max(
    8,
    Math.min(FOCUS_PADDING, Math.floor(Math.min(clientWidth, clientHeight) / 4)),
  )
  instance.fitBounds(target.bounds, { padding, maxZoom: FOCUS_MAX_ZOOM, duration })
}

// Expressões de feature-state reutilizadas pelas camadas de marcadores.
const stateFlag = (key: string) => ['boolean', ['feature-state', key], false]
const EXPLORED = ['>', ['coalesce', ['feature-state', 'count'], 0], 0]
const EMPHASIZED = ['any', stateFlag('hover'), stateFlag('selected')]
const POINT_STROKE = 'rgba(160,190,230,0.4)'

/** Raio em px: interpola com o zoom; países explorados e em destaque ficam maiores. */
function pointRadius(unexplored: [number, number], explored: [number, number]) {
  const at = (index: 0 | 1) => [
    '+',
    ['case', EXPLORED, explored[index], unexplored[index]],
    ['case', EMPHASIZED, 1.5, 0],
  ]
  return ['interpolate', ['linear'], ['zoom'], 0, at(0), MAX_ZOOM, at(1)]
}

/**
 * Marcadores acima dos polígonos. Explorados: círculo na cor da escala, contorno claro e raio
 * maior; não explorados: ponto pequeno e discreto. Uma camada invisível maior amplia o alvo de
 * hover/clique dos pontos pequenos.
 */
function addPointLayers(instance: MapLibreMap, points: unknown) {
  instance.addSource(POINTS_SOURCE_ID, {
    type: 'geojson',
    data: points,
    promoteId: 'iso',
  } as unknown as Parameters<MapLibreMap['addSource']>[1])
  instance.addLayer({
    id: POINTS_HIT_LAYER,
    type: 'circle',
    source: POINTS_SOURCE_ID,
    paint: { 'circle-radius': 9, 'circle-color': '#000000', 'circle-opacity': 0.01 },
  })
  instance.addLayer({
    id: 'country-points',
    type: 'circle',
    source: POINTS_SOURCE_ID,
    paint: {
      'circle-color': ['to-color', ['coalesce', ['feature-state', 'fill'], UNEXPLORED_COLOR]],
      'circle-radius': pointRadius([1.8, 3.2], [4.5, 7]) as never,
      'circle-opacity': ['case', EXPLORED, 1, 0.85] as never,
      'circle-stroke-color': [
        'case',
        EMPHASIZED,
        SELECTED_BORDER,
        EXPLORED,
        '#cfe6ff',
        POINT_STROKE,
      ] as never,
      'circle-stroke-width': ['case', EXPLORED, 1.25, 0.75] as never,
    },
  })
  interactiveLayers = [POINTS_HIT_LAYER, FILL_LAYER]
}

async function initMap() {
  const [geo, points] = await Promise.all([load(), loadCountryPoints()])
  // O contêiner só fica visível (v-show) depois do próximo render: sem isso o mapa nasceria
  // com tamanho 0 e o enquadramento inicial sairia errado (Américas cortadas em retrato).
  await nextTick()
  if (!geo || !container.value) return

  for (const feature of geo.features) {
    const p: CountryFeatureProperties = feature.properties
    namesByIso.set(p.iso, p.namePt ?? p.name)
  }
  for (const feature of points?.features ?? []) {
    const p = feature.properties
    pointIsos.add(p.iso)
    if (!namesByIso.has(p.iso)) namesByIso.set(p.iso, p.namePt ?? p.name)
  }
  focusIndex = buildFocusIndex(geo, points)

  let instance: MapLibreMap
  try {
    instance = new MapLibreMap({
      container: container.value,
      style: STYLE,
      center: [12, 22],
      zoom: 0.9,
      minZoom: -3, // ajustado a cada enquadramento (ver `fit`)
      maxZoom: MAX_ZOOM,
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
      id: FILL_LAYER,
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
    if (points) addPointLayers(instance, points)
    applyData()
    // Enquadra o mundo habitado em qualquer proporção de tela (inclusive retrato no mobile).
    fit(0)
    instance.on('movestart', (event) => {
      if (event.originalEvent) {
        userMoved = true
        focusedIso = null
      }
    })
    if (props.selectedIso) focusCountry(props.selectedIso, false)
    emit('ready')
  })

  if (props.interactive) {
    // Um único tratamento para polígonos e marcadores: o marcador (acima) vence o polígono.
    instance.on('mousemove', (event: MapLayerMouseEvent) => {
      if (isTouch()) return
      const hover = hoverAt(event.point)
      setHover(hover?.iso ?? null)
      instance.getCanvas().style.cursor = hover ? 'pointer' : ''
      if (hover || previousHoverEmitted) emit('country-hover', hover)
      previousHoverEmitted = !!hover
    })
    instance.getCanvas().addEventListener('mouseleave', clearHover)
    instance.on('click', (event) => {
      const iso = isoAt(event.point)
      emit('country-select', iso)
      // Em toque não há hover: o toque seleciona (borda + painel); sem tooltip.
      if (isTouch()) setHover(null)
    })
  }

  // Resize/orientação: reenquadra enquanto o usuário não tiver movido o mapa.
  resizeObserver = new ResizeObserver(() => {
    instance.resize()
    if (focusedIso) {
      if (!instance.isMoving()) focusCountry(focusedIso, false)
    } else if (!userMoved) fit(0)
  })
  resizeObserver.observe(container.value)
}

watch(() => [props.data, props.maxBookCount] as const, applyData)

watch(
  () => props.selectedIso,
  (iso, previous) => {
    const instance = map.value
    if (!instance?.getSource(SOURCE_ID)) return
    if (previous) setState(previous, { selected: false })
    if (iso) {
      setState(iso, { selected: true })
      // Depois do próximo render: o painel lateral altera a largura do mapa.
      void nextTick(() => {
        if (props.selectedIso === iso) focusCountry(iso)
      })
    }
  },
)

onMounted(initMap)

onBeforeUnmount(() => {
  if (loadTimer) clearTimeout(loadTimer)
  if (tweenFrame !== null) cancelAnimationFrame(tweenFrame)
  workerErrorSubscription?.unsubscribe()
  resizeObserver?.disconnect()
  map.value?.getCanvas().removeEventListener('mouseleave', clearHover)
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
