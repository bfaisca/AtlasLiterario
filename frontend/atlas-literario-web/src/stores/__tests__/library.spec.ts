import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type { LibraryItemDto, PagedResult } from '@/types/api'
import {
  defaultFilters,
  filtersFromRouteQuery,
  filtersToQueryParams,
  filtersToRouteQuery,
  hasActiveFilters,
} from '../libraryFilters'
import { LIBRARY_PAGE_SIZE, useLibraryStore } from '../library'
import { useMapsStore } from '../maps'

const libraryApi = vi.hoisted(() => ({
  list: vi.fn(),
  get: vi.fn(),
  add: vi.fn(),
  update: vi.fn(),
  remove: vi.fn(),
}))
vi.mock('@/api', () => ({ api: { library: libraryApi } }))

function item(id: string): LibraryItemDto {
  return { id, status: 'Read' } as LibraryItemDto
}

function page(ids: string[], pageNumber: number, totalPages: number): PagedResult<LibraryItemDto> {
  return {
    items: ids.map(item),
    page: pageNumber,
    pageSize: LIBRARY_PAGE_SIZE,
    totalCount: 30,
    totalPages,
  }
}

describe('filtros da biblioteca', () => {
  it('converte filtros em parâmetros do contrato, omitindo vazios', () => {
    const filters = {
      ...defaultFilters(),
      q: '  kafka ',
      status: ['Reading' as const, 'Read' as const],
      minRating: 4,
      sort: 'title' as const,
      order: 'asc' as const,
    }
    expect(filtersToQueryParams(filters, 2, 24)).toEqual({
      q: 'kafka',
      status: ['Reading', 'Read'],
      minRating: 4,
      sort: 'title',
      order: 'asc',
      page: 2,
      pageSize: 24,
    })
    expect(filtersToQueryParams(defaultFilters(), 1, 24)).toEqual({
      sort: 'createdAt',
      order: 'desc',
      page: 1,
      pageSize: 24,
    })
  })

  it('URL ↔ filtros: ida e volta, ignorando valores inválidos', () => {
    const filters = filtersFromRouteQuery({
      q: 'dom',
      status: ['Read', 'Invalido', 'WantToRead'],
      genre: 'Romance',
      minRating: '9',
      sort: 'hack',
      order: 'asc',
    })
    expect(filters).toEqual({
      q: 'dom',
      status: ['WantToRead', 'Read'],
      genre: 'Romance',
      minRating: null,
      geoMissing: null,
      sort: 'createdAt',
      order: 'asc',
    })
    expect(filtersToRouteQuery(filters)).toEqual({
      q: 'dom',
      status: ['WantToRead', 'Read'],
      genre: 'Romance',
      order: 'asc',
    })
    expect(filtersFromRouteQuery(filtersToRouteQuery(filters) as never)).toEqual(filters)
    expect(filtersToRouteQuery(defaultFilters())).toEqual({})
  })
})

describe('filtro de dados geográficos', () => {
  it('vai para a query da API e da URL só quando definido', () => {
    const filters = { ...defaultFilters(), geoMissing: 'origin' as const }
    expect(filtersToQueryParams(filters, 1, 24).geoMissing).toBe('origin')
    expect(filtersToQueryParams(defaultFilters(), 1, 24)).not.toHaveProperty('geoMissing')
    expect(filtersToRouteQuery(filters)).toEqual({ geoMissing: 'origin' })
    expect(filtersToRouteQuery(defaultFilters())).not.toHaveProperty('geoMissing')
  })

  it('URL ↔ filtro: aceita os quatro valores e ignora inválidos', () => {
    for (const value of ['any', 'origin', 'author', 'both'] as const) {
      expect(filtersFromRouteQuery({ geoMissing: value }).geoMissing).toBe(value)
    }
    expect(filtersFromRouteQuery({ geoMissing: 'nope' }).geoMissing).toBeNull()
    expect(filtersFromRouteQuery({}).geoMissing).toBeNull()
  })

  it('conta como filtro ativo', () => {
    expect(hasActiveFilters({ ...defaultFilters(), geoMissing: 'any' })).toBe(true)
    expect(hasActiveFilters(defaultFilters())).toBe(false)
  })
})

describe('library store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    Object.values(libraryApi).forEach((fn) => fn.mockReset())
  })

  it('setFilters busca a primeira página com os parâmetros dos filtros', async () => {
    libraryApi.list.mockResolvedValue(page(['a', 'b'], 1, 2))
    const store = useLibraryStore()

    await store.setFilters({ ...defaultFilters(), status: ['Read'], genre: 'Romance' })

    expect(libraryApi.list).toHaveBeenCalledWith({
      status: ['Read'],
      genre: 'Romance',
      sort: 'createdAt',
      order: 'desc',
      page: 1,
      pageSize: LIBRARY_PAGE_SIZE,
    })
    expect(store.items.map((i) => i.id)).toEqual(['a', 'b'])
    expect(store.hasMore).toBe(true)
  })

  it('setFilters com geoMissing envia o parâmetro e volta à página 1', async () => {
    libraryApi.list.mockResolvedValue(page(['a'], 1, 3))
    const store = useLibraryStore()
    await store.setFilters(defaultFilters())
    libraryApi.list.mockResolvedValue({ ...page(['a'], 2, 3), page: 2 })
    await store.loadMore()
    expect(store.page).toBe(2)

    libraryApi.list.mockResolvedValue(page(['x'], 1, 1))
    await store.setFilters({ ...defaultFilters(), geoMissing: 'both' })
    expect(libraryApi.list).toHaveBeenLastCalledWith(
      expect.objectContaining({ geoMissing: 'both', page: 1 }),
    )
    expect(store.page).toBe(1)
    expect(store.items.map((i) => i.id)).toEqual(['x'])
  })

  it('loadMore acrescenta a próxima página sem duplicar', async () => {
    libraryApi.list
      .mockResolvedValueOnce(page(['a', 'b'], 1, 2))
      .mockResolvedValueOnce(page(['b', 'c'], 2, 2))
    const store = useLibraryStore()
    await store.fetch()
    await store.loadMore()

    expect(libraryApi.list).toHaveBeenLastCalledWith(expect.objectContaining({ page: 2 }))
    expect(store.items.map((i) => i.id)).toEqual(['a', 'b', 'c'])
    expect(store.hasMore).toBe(false)
  })

  it('usa cache para a mesma consulta e o invalida após mutações', async () => {
    libraryApi.list.mockResolvedValue(page(['a'], 1, 1))
    libraryApi.add.mockResolvedValue(item('new'))
    const store = useLibraryStore()
    const maps = useMapsStore()
    const invalidateMaps = vi.spyOn(maps, 'invalidate')

    await store.fetch()
    await store.fetch()
    expect(libraryApi.list).toHaveBeenCalledTimes(1)

    await store.add({ bookId: 'b1', status: 'WantToRead' })
    expect(invalidateMaps).toHaveBeenCalled()

    await store.fetch()
    expect(libraryApi.list).toHaveBeenCalledTimes(2)
  })

  it('remove tira o item da lista e invalida o cache', async () => {
    libraryApi.list.mockResolvedValue(page(['a', 'b'], 1, 1))
    libraryApi.remove.mockResolvedValue(undefined)
    const store = useLibraryStore()
    await store.fetch()

    await store.remove('a')

    expect(libraryApi.remove).toHaveBeenCalledWith('a')
    expect(store.items.map((i) => i.id)).toEqual(['b'])
    await store.fetch()
    expect(libraryApi.list).toHaveBeenCalledTimes(2)
  })

  it('descarta respostas antigas quando os filtros mudam rapidamente', async () => {
    let resolveSlow: (value: PagedResult<LibraryItemDto>) => void = () => {}
    libraryApi.list
      .mockReturnValueOnce(new Promise((resolve) => (resolveSlow = resolve)))
      .mockResolvedValueOnce(page(['fast'], 1, 1))
    const store = useLibraryStore()

    const slow = store.setFilters({ ...defaultFilters(), q: 'a' })
    await store.setFilters({ ...defaultFilters(), q: 'ab' })
    resolveSlow(page(['slow'], 1, 1))
    await slow

    expect(store.items.map((i) => i.id)).toEqual(['fast'])
  })
})
