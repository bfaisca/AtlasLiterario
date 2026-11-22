import { onBeforeUnmount, ref } from 'vue'
import { storeToRefs } from 'pinia'
import { useProfilesStore } from '@/stores/profiles'
import { debounce } from '@/utils/debounce'

export const PEOPLE_SEARCH_MIN_LENGTH = 2
export const PEOPLE_SEARCH_DEBOUNCE_MS = 250

/**
 * Busca de pessoas (`GET /profiles?q=`): debounce de 250 ms, mínimo de 2 caracteres (após trim) e
 * cancelamento da requisição obsoleta (o store aborta a anterior; a resposta velha nunca é aplicada).
 * Os resultados vivem no store de perfis, que é zerado na troca de conta.
 */
export function usePeopleSearch(options: { delayMs?: number } = {}) {
  const store = useProfilesStore()
  const { searchResults, searchLoading, searchError, searchedQuery } = storeToRefs(store)
  const text = ref('')

  const run = debounce(
    (query: string) => void store.search(query),
    options.delayMs ?? PEOPLE_SEARCH_DEBOUNCE_MS,
  )

  /** Chamar a cada digitação. */
  function onInput(value: string) {
    text.value = value
    const query = value.trim()
    run.cancel()
    if (query.length < PEOPLE_SEARCH_MIN_LENGTH) {
      store.clearSearch()
      return
    }
    // O texto mudou: a requisição em voo (se houver) ficou obsoleta.
    store.cancelSearchInFlight()
    run(query)
  }

  function clear() {
    run.cancel()
    store.clearSearch()
    text.value = ''
  }

  onBeforeUnmount(() => {
    run.cancel()
    store.clearSearch()
  })

  return {
    text,
    results: searchResults,
    loading: searchLoading,
    error: searchError,
    searchedQuery,
    onInput,
    clear,
  }
}
