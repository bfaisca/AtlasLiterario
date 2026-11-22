import type { AxiosInstance } from 'axios'
import type { CountryDto } from '@/types/api'

export interface CountriesApi {
  /** Lista completa (cacheável); a implementação HTTP mantém cache em memória. */
  list(): Promise<CountryDto[]>
}

export function createHttpCountriesApi(http: AxiosInstance): CountriesApi {
  let cache: Promise<CountryDto[]> | null = null
  return {
    list() {
      cache ??= http
        .get<CountryDto[]>('/countries')
        .then((response) => response.data)
        .catch((error: unknown) => {
          cache = null
          throw error
        })
      return cache
    },
  }
}
