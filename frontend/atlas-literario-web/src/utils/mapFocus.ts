import type { CountriesGeoJson, CountryPointsGeoJson } from '@/composables/useCountriesGeo'

/** Retângulo [sudoeste, nordeste] em graus: [[oeste, sul], [leste, norte]]. */
export type BoundsTuple = [[number, number], [number, number]]

/** Para onde a câmera vai ao selecionar um país (dados puros, independentes da biblioteca de mapa). */
export type FocusTarget =
  | { kind: 'point'; center: [number, number]; zoom: number }
  | { kind: 'bounds'; bounds: BoundsTuple }

/** Zoom ao focar um país/território representado por marcador (ilhas pequenas). */
export const POINT_FOCUS_ZOOM = 4.5
/** Uma parte só entra no enquadramento se seu bbox tiver ao menos esta fração da maior parte. */
const MAIN_PART_MIN_RATIO = 0.1

type Position = number[]

function* polygonsOf(geometry: { type: string; coordinates: unknown }): Generator<Position[][]> {
  if (geometry.type === 'Polygon') yield geometry.coordinates as Position[][]
  else if (geometry.type === 'MultiPolygon') yield* geometry.coordinates as Position[][][]
}

function boundsOfRing(ring: Position[]): BoundsTuple {
  let west = Infinity
  let south = Infinity
  let east = -Infinity
  let north = -Infinity
  for (const [lng, lat] of ring) {
    if (lng! < west) west = lng!
    if (lng! > east) east = lng!
    if (lat! < south) south = lat!
    if (lat! > north) north = lat!
  }
  return [
    [west, south],
    [east, north],
  ]
}

const boxArea = ([[w, s], [e, n]]: BoundsTuple) => (e - w) * (n - s)

/**
 * Enquadramento do território principal: une as partes cujo bbox é relevante perto da maior.
 * Ilhas distantes (Havaí, Ilhas Cook…) não puxam a câmera para o oceano; sem isso os EUA ou a
 * Rússia (que cruza o antimeridiano) abririam o mapa-múndi inteiro.
 */
export function mainBounds(geometry: { type: string; coordinates: unknown }): BoundsTuple | null {
  const parts = [...polygonsOf(geometry)]
    .map((polygon) => boundsOfRing(polygon[0] ?? []))
    .filter(([[w, s], [e, n]]) => Number.isFinite(w + s + e + n))
  if (!parts.length) return null
  const largest = Math.max(...parts.map(boxArea))
  const kept = parts.filter((part) => boxArea(part) >= largest * MAIN_PART_MIN_RATIO)
  return [
    [Math.min(...kept.map((p) => p[0][0])), Math.min(...kept.map((p) => p[0][1]))],
    [Math.max(...kept.map((p) => p[1][0])), Math.max(...kept.map((p) => p[1][1]))],
  ]
}

/**
 * Índice `iso` → alvo de foco. Países com marcador (pequenos) recebem foco em ponto, com zoom
 * fixo; os demais, em bounds do território principal (o componente aplica padding e `maxZoom`).
 */
export function buildFocusIndex(
  polygons: Pick<CountriesGeoJson, 'features'>,
  points: Pick<CountryPointsGeoJson, 'features'> | null,
): Map<string, FocusTarget> {
  const index = new Map<string, FocusTarget>()
  for (const feature of polygons.features) {
    const bounds = mainBounds(feature.geometry)
    if (bounds) index.set(feature.properties.iso, { kind: 'bounds', bounds })
  }
  for (const feature of points?.features ?? []) {
    index.set(feature.properties.iso, {
      kind: 'point',
      center: feature.geometry.coordinates,
      zoom: POINT_FOCUS_ZOOM,
    })
  }
  return index
}
