import { ref } from 'vue'

/**
 * Carrega o GeoJSON dos países (Natural Earth 1:110m) de `public/geo/` via fetch.
 * Fica fora do bundle JS (≈ 190 kB) e é baixado só quando um mapa é exibido; o resultado
 * é cacheado em memória entre telas. Se o arquivo não existir (`npm run geo:fetch` não
 * executado), o status vira `missing` e o mapa mostra um estado vazio.
 */

export const COUNTRIES_GEOJSON_URL = '/geo/countries-110m.geojson'

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

export type GeoStatus = 'idle' | 'loading' | 'ready' | 'missing' | 'error'

let cached: Promise<CountriesGeoJson | null> | null = null

function isCountriesGeoJson(value: unknown): value is CountriesGeoJson {
  return (
    typeof value === 'object' &&
    value !== null &&
    (value as { type?: unknown }).type === 'FeatureCollection' &&
    Array.isArray((value as { features?: unknown }).features)
  )
}

/** `null` = arquivo ausente/inválido (inclui o fallback HTML da SPA em dev). */
async function fetchGeoJson(url: string): Promise<CountriesGeoJson | null> {
  const response = await fetch(url, {
    headers: { Accept: 'application/geo+json, application/json' },
  })
  if (response.status === 404) return null
  if (!response.ok) throw new Error(`HTTP ${response.status} ao carregar ${url}`)
  try {
    const data: unknown = await response.json()
    return isCountriesGeoJson(data) ? data : null
  } catch {
    return null
  }
}

export function loadCountriesGeo(url = COUNTRIES_GEOJSON_URL): Promise<CountriesGeoJson | null> {
  cached ??= fetchGeoJson(url).catch((error: unknown) => {
    cached = null
    throw error
  })
  return cached
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
