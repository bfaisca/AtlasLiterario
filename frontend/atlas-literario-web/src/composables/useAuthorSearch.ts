import { computed, onBeforeUnmount, ref } from 'vue'
import { api } from '@/api'
import { getErrorMessage, toApiError } from '@/api/errors'
import type { AuthorSuggestionDto } from '@/types/api'
import { debounce } from '@/utils/debounce'

export const AUTHOR_SEARCH_MIN_LENGTH = 2
export const AUTHOR_SEARCH_LIMIT = 8
export const AUTHOR_SEARCH_DEBOUNCE_MS = 250

/**
 * Busca de autores para o autocomplete (`GET /authors?q=&limit=`): debounce, mínimo de caracteres
 * e cancelamento da requisição anterior (a resposta de uma consulta obsoleta nunca é aplicada).
 */
export function useAuthorSearch(options: { delayMs?: number; limit?: number } = {}) {
  const delayMs = options.delayMs ?? AUTHOR_SEARCH_DEBOUNCE_MS
  const limit = options.limit ?? AUTHOR_SEARCH_LIMIT

  const suggestions = ref<AuthorSuggestionDto[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)
  /** A última consulta concluída (sucesso) — distingue "sem resultados" de "ainda não buscou". */
  const searchedQuery = ref<string | null>(null)

  let controller: AbortController | null = null

  async function run(query: string) {
    controller?.abort()
    const current = new AbortController()
    controller = current
    loading.value = true
    error.value = null
    try {
      const result = await api.authors.search(query, limit, current.signal)
      if (current.signal.aborted) return
      suggestions.value = result
      searchedQuery.value = query
    } catch (e) {
      if (current.signal.aborted) return
      suggestions.value = []
      searchedQuery.value = null
      error.value = getErrorMessage(toApiError(e))
    } finally {
      if (controller === current) loading.value = false
    }
  }

  const debouncedRun = debounce((query: string) => void run(query), delayMs)

  /** Agenda a busca (ou limpa, se o texto for curto demais). */
  function search(text: string) {
    const query = text.trim()
    if (query.length < AUTHOR_SEARCH_MIN_LENGTH) {
      clear()
      return
    }
    // Cancela já a requisição em voo: o texto mudou, a resposta dela ficou obsoleta.
    controller?.abort()
    controller = null
    loading.value = true
    debouncedRun(query)
  }

  function clear() {
    debouncedRun.cancel()
    controller?.abort()
    controller = null
    loading.value = false
    error.value = null
    suggestions.value = []
    searchedQuery.value = null
  }

  onBeforeUnmount(clear)

  return {
    suggestions,
    loading,
    error,
    searchedQuery,
    hasNoResults: computed(
      () =>
        !loading.value && !error.value && searchedQuery.value !== null && !suggestions.value.length,
    ),
    search,
    clear,
  }
}
