import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { api } from '@/api'
import { toApiError, type ApiError } from '@/api/errors'
import type { CountryDto } from '@/types/api'

export function normalizeSearch(text: string): string {
  return text
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .toLowerCase()
    .trim()
}

/** Busca por nome em português (prioritário), nome em inglês ou código ISO. */
export function searchCountries(
  countries: readonly CountryDto[],
  term: string,
  limit = 50,
): CountryDto[] {
  const q = normalizeSearch(term)
  if (!q) return []
  const scored = countries
    .map((country) => {
      const pt = normalizeSearch(country.namePt)
      const en = normalizeSearch(country.name)
      const iso = country.iso.toLowerCase()
      let score = -1
      if (pt.startsWith(q)) score = 0
      else if (iso === q) score = 1
      else if (pt.includes(q)) score = 2
      else if (en.includes(q)) score = 3
      return { country, score }
    })
    .filter((entry) => entry.score >= 0)
  scored.sort(
    (a, b) => a.score - b.score || a.country.namePt.localeCompare(b.country.namePt, 'pt-BR'),
  )
  return scored.slice(0, limit).map((entry) => entry.country)
}

/** Tabela de países (GET /countries), carregada uma única vez por sessão. */
export const useCountriesStore = defineStore('countries', () => {
  const list = ref<CountryDto[]>([])
  const error = ref<ApiError | null>(null)
  const loading = ref(false)
  let loadPromise: Promise<void> | null = null

  const byIso = computed(() => new Map(list.value.map((c) => [c.iso, c])))

  function load(): Promise<void> {
    loadPromise ??= (async () => {
      loading.value = true
      error.value = null
      try {
        list.value = [...(await api.countries.list())].sort((a, b) =>
          a.namePt.localeCompare(b.namePt, 'pt-BR'),
        )
      } catch (e) {
        error.value = toApiError(e)
        loadPromise = null
      } finally {
        loading.value = false
      }
    })()
    return loadPromise
  }

  const search = (term: string, limit?: number) => searchCountries(list.value, term, limit)
  const nameOf = (iso: string) => byIso.value.get(iso)?.namePt ?? iso

  return { list, byIso, error, loading, load, search, nameOf }
})
