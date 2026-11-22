import { ref } from 'vue'

/**
 * Carrega os dados geográficos (Natural Earth 1:50m) de `public/geo/` via fetch:
 *  - `countries-50m.geojson`: polígonos dos países/territórios (chave `iso` = ISO alpha-3);
 *  - `country-points.geojson`: marcadores dos países/territórios pequenos demais para o polígono
 *    ser visível (opcional; sem ele o mapa funciona só com polígonos).
 * Ficam fora do bundle JS e são baixados só quando um mapa é exibido; o resultado é cacheado em
 * memória entre telas. Se o arquivo de polígonos não existir (`npm run geo:fetch` não executado),
 * o status vira `missing` e o mapa mostra um estado vazio.
 */

export const COUNTRIES_GEOJSON_URL = '/geo/countries-50m.geojson'
export const COUNTRY_POINTS_GEOJSON_URL = '/geo/country-points.geojson'

export interface CountryFeatureProperties {
  iso: string
  name: string
  namePt: string | null
}

export interface CountriesGeoJson {
  type: 'FeatureCollection'
  features: Array<{
    type: 'Feature'
    properties: CountryFeatureProperties
    geometry: { type: string; coordinates: unknown }
  }>
}

export interface CountryPointsGeoJson {
  type: 'FeatureCollection'
  features: Array<{
    type: 'Feature'
    properties: CountryFeatureProperties
    geometry: { type: 'Point'; coordinates: [number, number] }
  }>
}

export type GeoStatus = 'idle' | 'loading' | 'ready' | 'missing' | 'error'

let cached: Promise<CountriesGeoJson | null> | null = null
let cachedPoints: Promise<CountryPointsGeoJson | null> | null = null

function isFeatureCollection(value: unknown): boolean {
  return (
    typeof value === 'object' &&
    value !== null &&
    (value as { type?: unknown }).type === 'FeatureCollection' &&
    Array.isArray((value as { features?: unknown }).features)
  )
}

/** `null` = arquivo ausente/inválido (inclui o fallback HTML da SPA em dev). */
async function fetchGeoJson<T>(url: string): Promise<T | null> {
  const response = await fetch(url, {
    headers: { Accept: 'application/geo+json, application/json' },
  })
  if (response.status === 404) return null
  if (!response.ok) throw new Error(`HTTP ${response.status} ao carregar ${url}`)
  try {
    const data: unknown = await response.json()
    return isFeatureCollection(data) ? (data as T) : null
  } catch {
    return null
  }
}

export function loadCountriesGeo(url = COUNTRIES_GEOJSON_URL): Promise<CountriesGeoJson | null> {
  cached ??= fetchGeoJson<CountriesGeoJson>(url).catch((error: unknown) => {
    cached = null
    throw error
  })
  return cached
}

/** Marcadores são um reforço visual: qualquer falha vira `null` (o mapa segue só com polígonos). */
export function loadCountryPoints(
  url = COUNTRY_POINTS_GEOJSON_URL,
): Promise<CountryPointsGeoJson | null> {
  cachedPoints ??= fetchGeoJson<CountryPointsGeoJson>(url).catch((error: unknown) => {
    console.warn('[useCountriesGeo] marcadores indisponíveis', error)
    cachedPoints = null
    return null
  })
  return cachedPoints
}

export function useCountriesGeo(url = COUNTRIES_GEOJSON_URL) {
  const status = ref<GeoStatus>('idle')
  const geo = ref<CountriesGeoJson | null>(null)

  async function load() {
    status.value = 'loading'
    try {
      const data = await loadCountriesGeo(url)
      geo.value = data
      status.value = data ? 'ready' : 'missing'
    } catch {
      status.value = 'error'
    }
    return geo.value
  }

  return { status, geo, load }
}
