import type { LocationQuery, LocationQueryRaw } from 'vue-router'
import { GEO_MISSING_VALUES } from '@/utils/libraryGeo'
import {
  READING_STATUSES,
  type LibraryGeoMissing,
  type LibraryQueryParams,
  type LibrarySort,
  type ReadingStatus,
  type SortOrder,
} from '@/types/api'

/** Filtros da biblioteca (estado da UI), refletidos na query string da URL. */
export interface LibraryFilters {
  q: string
  status: ReadingStatus[]
  genre: string
  minRating: number | null
  /** `null` = todos os livros */
  geoMissing: LibraryGeoMissing | null
  sort: LibrarySort
  order: SortOrder
}

export const LIBRARY_SORTS: readonly LibrarySort[] = [
  'createdAt',
  'title',
  'author',
  'finishedDate',
  'rating',
  'publicationYear',
]

export const DEFAULT_LIBRARY_FILTERS: Readonly<LibraryFilters> = Object.freeze({
  q: '',
  status: [],
  genre: '',
  minRating: null,
  geoMissing: null,
  sort: 'createdAt',
  order: 'desc',
})

export function defaultFilters(): LibraryFilters {
  return { ...DEFAULT_LIBRARY_FILTERS, status: [] }
}

/** Filtros → parâmetros de GET /library (omite vazios e mantém a ordem dos status). */
export function filtersToQueryParams(
  filters: LibraryFilters,
  page: number,
  pageSize: number,
): LibraryQueryParams {
  const params: LibraryQueryParams = { sort: filters.sort, order: filters.order, page, pageSize }
  const q = filters.q.trim()
  if (q) params.q = q
  if (filters.status.length) {
    params.status = READING_STATUSES.filter((s) => filters.status.includes(s))
  }
  const genre = filters.genre.trim()
  if (genre) params.genre = genre
  if (filters.minRating) params.minRating = filters.minRating
  if (filters.geoMissing) params.geoMissing = filters.geoMissing
  return params
}

type QueryValue = LocationQuery[string] | undefined

function first(value: QueryValue): string | null {
  const v = Array.isArray(value) ? value[0] : value
  return typeof v === 'string' ? v : null
}

function all(value: QueryValue): string[] {
  const list = Array.isArray(value) ? value : [value]
  return list.filter((v): v is string => typeof v === 'string')
}

/** URL → filtros, ignorando valores inválidos (a URL pode ter sido editada à mão). */
export function filtersFromRouteQuery(query: LocationQuery): LibraryFilters {
  const filters = defaultFilters()
  filters.q = first(query.q) ?? ''
  filters.status = READING_STATUSES.filter((s) => all(query.status).includes(s))
  filters.genre = first(query.genre) ?? ''
  const minRating = Number(first(query.minRating))
  filters.minRating =
    Number.isInteger(minRating) && minRating >= 1 && minRating <= 5 ? minRating : null
  const geoMissing = first(query.geoMissing)
  filters.geoMissing = GEO_MISSING_VALUES.find((value) => value === geoMissing) ?? null
  const sort = first(query.sort)
  if (sort && (LIBRARY_SORTS as readonly string[]).includes(sort))
    filters.sort = sort as LibrarySort
  const order = first(query.order)
  if (order === 'asc' || order === 'desc') filters.order = order
  return filters
}

/** Filtros → query da URL (somente o que difere do padrão, para URLs limpas). */
export function filtersToRouteQuery(filters: LibraryFilters): LocationQueryRaw {
  const query: LocationQueryRaw = {}
  if (filters.q.trim()) query.q = filters.q.trim()
  if (filters.status.length)
    query.status = READING_STATUSES.filter((s) => filters.status.includes(s))
  if (filters.genre.trim()) query.genre = filters.genre.trim()
  if (filters.minRating) query.minRating = String(filters.minRating)
  if (filters.geoMissing) query.geoMissing = filters.geoMissing
  if (filters.sort !== DEFAULT_LIBRARY_FILTERS.sort) query.sort = filters.sort
  if (filters.order !== DEFAULT_LIBRARY_FILTERS.order) query.order = filters.order
  return query
}

export function hasActiveFilters(filters: LibraryFilters): boolean {
  return !!(
    filters.q.trim() ||
    filters.status.length ||
    filters.genre.trim() ||
    filters.minRating ||
    filters.geoMissing
  )
}
