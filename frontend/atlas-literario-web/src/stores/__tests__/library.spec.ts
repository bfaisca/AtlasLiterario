import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type { LibraryItemDto, PagedResult } from '@/types/api'
import {
  defaultFilters,
  filtersFromRouteQuery,
  filtersToQueryParams,
  filtersToRouteQuery,
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
