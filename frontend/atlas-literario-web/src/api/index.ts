import type { AxiosInstance } from 'axios'
import { createMockApi } from '@/mocks/mockApi'
import { createHttpAuthApi } from './auth'
import { createHttpAuthorsApi } from './authors'
import { createHttpBooksApi } from './books'
import { createHttpCountriesApi } from './countries'
import { http } from './http'
import { createHttpLibraryApi } from './library'
import { createHttpMapsApi } from './maps'
import { createHttpStatisticsApi } from './statistics'
import type { AtlasApi } from './types'

export function createHttpApi(client: AxiosInstance): AtlasApi {
  return {
    auth: createHttpAuthApi(client),
    books: createHttpBooksApi(client),
    library: createHttpLibraryApi(client),
    authors: createHttpAuthorsApi(client),
    countries: createHttpCountriesApi(client),
    maps: createHttpMapsApi(client),
    statistics: createHttpStatisticsApi(client),
  }
}

/** `VITE_USE_MOCKS` é substituído em build; o ramo não usado é eliminado pelo bundler. */
export const USE_MOCKS = import.meta.env.VITE_USE_MOCKS === 'true'

export const api: AtlasApi = USE_MOCKS ? createMockApi() : createHttpApi(http)

export type { AtlasApi } from './types'
export { ApiError, getErrorMessage, toApiError } from './errors'
export { tokenManager } from './tokenManager'
