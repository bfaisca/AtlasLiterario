import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { api } from '@/api'
import { toApiError, type ApiError } from '@/api/errors'
import type {
  AddToLibraryRequest,
  LibraryItemDto,
  PagedResult,
  UpdateLibraryItemRequest,
} from '@/types/api'
import { useMapsStore } from './maps'
import { useStatisticsStore } from './statistics'
import { defaultFilters, filtersToQueryParams, type LibraryFilters } from './libraryFilters'

export const LIBRARY_PAGE_SIZE = 24
/** Cache curto de páginas por combinação de filtros (invalidado após qualquer mutação). */
const CACHE_TTL_MS = 60_000

export const useLibraryStore = defineStore('library', () => {
  const filters = ref<LibraryFilters>(defaultFilters())
  const items = ref<LibraryItemDto[]>([])
  const page = ref(0)
  const totalPages = ref(0)
  const totalCount = ref(0)
  const loading = ref(false)
  const loadingMore = ref(false)
  const error = ref<ApiError | null>(null)

  const cache = new Map<string, { result: PagedResult<LibraryItemDto>; at: number }>()
  let requestSeq = 0

  const hasMore = computed(() => page.value < totalPages.value)

  async function loadPage(pageNumber: number, force = false) {
    const params = filtersToQueryParams(filters.value, pageNumber, LIBRARY_PAGE_SIZE)
    const key = JSON.stringify(params)
    const cached = cache.get(key)
    if (!force && cached && Date.now() - cached.at < CACHE_TTL_MS) return cached.result
    const result = await api.library.list(params)
    cache.set(key, { result, at: Date.now() })
    return result
  }

  /** Carrega a primeira página com os filtros atuais (respostas antigas são descartadas). */
  async function fetch(options: { force?: boolean } = {}) {
    const seq = ++requestSeq
    loading.value = true
    error.value = null
    try {
      const result = await loadPage(1, options.force)
      if (seq !== requestSeq) return
      items.value = result.items
      page.value = result.page
      totalPages.value = result.totalPages
      totalCount.value = result.totalCount
    } catch (e) {
      if (seq === requestSeq) error.value = toApiError(e)
    } finally {
      if (seq === requestSeq) loading.value = false
    }
  }

  async function setFilters(next: LibraryFilters) {
    filters.value = { ...next, status: [...next.status] }
    await fetch()
  }

  async function loadMore() {
    if (!hasMore.value || loadingMore.value) return
    const seq = requestSeq
    loadingMore.value = true
    try {
      const result = await loadPage(page.value + 1)
      if (seq !== requestSeq) return
      const known = new Set(items.value.map((i) => i.id))
      items.value = [...items.value, ...result.items.filter((i) => !known.has(i.id))]
      page.value = result.page
      totalPages.value = result.totalPages
      totalCount.value = result.totalCount
    } catch (e) {
      error.value = toApiError(e)
    } finally {
      loadingMore.value = false
    }
  }

  /** Descarta caches da biblioteca e dos dados derivados (mapas, estatísticas). */
  function invalidate() {
    cache.clear()
    useMapsStore().invalidate()
    useStatisticsStore().invalidate()
  }

  async function getItem(id: string) {
    return api.library.get(id)
  }

  async function add(request: AddToLibraryRequest) {
    const item = await api.library.add(request)
    invalidate()
    return item
  }

  async function update(id: string, request: UpdateLibraryItemRequest) {
    const item = await api.library.update(id, request)
    invalidate()
    items.value = items.value.map((i) => (i.id === id ? item : i))
    return item
  }

  async function remove(id: string) {
    await api.library.remove(id)
    invalidate()
    const before = items.value.length
    items.value = items.value.filter((i) => i.id !== id)
    if (items.value.length < before) totalCount.value = Math.max(0, totalCount.value - 1)
  }

  return {
    filters,
    items,
    page,
    totalPages,
    totalCount,
    loading,
    loadingMore,
    error,
    hasMore,
    fetch,
    setFilters,
    loadMore,
    invalidate,
    getItem,
    add,
    update,
    remove,
  }
})
