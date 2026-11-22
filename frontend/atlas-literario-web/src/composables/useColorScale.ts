import { computed, toValue, type MaybeRefOrGetter } from 'vue'

/**
 * Escala de cores dos mapas (docs/api-contracts.md §9 · business-rules §4).
 * intensity = n === 0 ? 0 : log1p(n) / log1p(max), interpolada linearmente em RGB
 * entre as paradas abaixo. País sem livros usa `UNEXPLORED_COLOR`.
 */

export interface ColorStop {
  at: number
  color: string
}

export const UNEXPLORED_COLOR = '#0f1a2b'

export const MAP_COLOR_STOPS: readonly ColorStop[] = [
  { at: 0, color: '#13294b' },
  { at: 0.35, color: '#1f4e8c' },
  { at: 0.65, color: '#3f86d1' },
  { at: 1, color: '#bfe0ff' },
]

export function computeIntensity(count: number, maxCount: number): number {
  if (count <= 0 || maxCount <= 0) return 0
  const value = Math.log1p(count) / Math.log1p(maxCount)
  return Math.min(1, Math.max(0, value))
}

type Rgb = [number, number, number]

export function hexToRgb(hex: string): Rgb {
  const value = hex.replace('#', '')
  const full = value.length === 3 ? [...value].map((c) => c + c).join('') : value
  const n = Number.parseInt(full, 16)
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255]
}

export function rgbToHex([r, g, b]: Rgb): string {
  return `#${[r, g, b]
    .map((c) =>
      Math.round(Math.min(255, Math.max(0, c)))
        .toString(16)
        .padStart(2, '0'),
    )
    .join('')}`
}

/** Interpolação linear por partes entre paradas ordenadas por `at`. */
export function interpolateColor(t: number, stops: readonly ColorStop[] = MAP_COLOR_STOPS): string {
  if (stops.length === 0) throw new Error('A escala precisa de ao menos uma parada.')
  const first = stops[0]!
  const last = stops[stops.length - 1]!
  if (t <= first.at) return first.color
  if (t >= last.at) return last.color
  for (let i = 1; i < stops.length; i++) {
    const upper = stops[i]!
    if (t <= upper.at) {
      const lower = stops[i - 1]!
      const local = (t - lower.at) / (upper.at - lower.at)
      const a = hexToRgb(lower.color)
      const b = hexToRgb(upper.color)
      return rgbToHex([
        a[0] + (b[0] - a[0]) * local,
        a[1] + (b[1] - a[1]) * local,
        a[2] + (b[2] - a[2]) * local,
      ])
    }
  }
  return last.color
}

export function colorForCount(count: number, maxCount: number): string {
  if (count <= 0) return UNEXPLORED_COLOR
  return interpolateColor(computeIntensity(count, maxCount))
}

/** Mistura linear de duas cores hex (t = 0 → a, t = 1 → b). */
export function mixColors(a: string, b: string, t: number): string {
  const ca = hexToRgb(a)
  const cb = hexToRgb(b)
  return rgbToHex([
    ca[0] + (cb[0] - ca[0]) * t,
    ca[1] + (cb[1] - ca[1]) * t,
    ca[2] + (cb[2] - ca[2]) * t,
  ])
}

export interface LegendTick {
  count: number
  color: string
}

/**
 * Pontos da legenda: `steps` valores inteiros distribuídos geometricamente de 1 até `maxCount`
 * (a escala é logarítmica, então espaçamento geométrico = espaçamento uniforme de cor).
 * Valores repetidos (mapas com máximo pequeno) são removidos: max=3 → 1, 2, 3.
 */
export function legendTicks(maxCount: number, steps = 5): LegendTick[] {
  if (maxCount < 1 || steps < 1) return []
  const counts = new Set<number>()
  for (let i = 0; i < steps; i++) {
    const t = steps === 1 ? 1 : i / (steps - 1)
    counts.add(Math.min(maxCount, Math.max(1, Math.round(Math.exp(t * Math.log(maxCount))))))
  }
  return [...counts]
    .sort((a, b) => a - b)
    .map((count) => ({ count, color: colorForCount(count, maxCount) }))
}

/** Versão reativa: recebe o máximo do mapa atual (ref, getter ou valor). */
export function useColorScale(maxCount: MaybeRefOrGetter<number>) {
  const max = computed(() => toValue(maxCount))
  return {
    max,
    colorFor: (count: number) => colorForCount(count, max.value),
    intensityFor: (count: number) => computeIntensity(count, max.value),
    stops: MAP_COLOR_STOPS,
    unexploredColor: UNEXPLORED_COLOR,
  }
}
