import type { AuthApi } from './auth'
import type { AuthorsApi } from './authors'
import type { BooksApi } from './books'
import type { CountriesApi } from './countries'
import type { LibraryApi } from './library'
import type { MapsApi } from './maps'
import type { StatisticsApi } from './statistics'

/** Fachada única de acesso à API — implementada por HTTP (Axios) ou por mocks em memória. */
export interface AtlasApi {
  auth: AuthApi
  books: BooksApi
  library: LibraryApi
  authors: AuthorsApi
  countries: CountriesApi
  maps: MapsApi
  statistics: StatisticsApi
}

export type { AuthApi, AuthorsApi, BooksApi, CountriesApi, LibraryApi, MapsApi, StatisticsApi }
