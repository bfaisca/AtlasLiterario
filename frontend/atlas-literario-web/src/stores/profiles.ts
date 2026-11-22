import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api } from '@/api'
import { toApiError, type ApiError } from '@/api/errors'
import type { PagedResult, ProfileDto, ProfileSummaryDto } from '@/types/api'
import { normalizeHandle } from '@/utils/handle'

export const PEOPLE_PAGE_SIZE = 20
export const PEOPLE_SEARCH_LIMIT = 10

export type PeopleListKind = 'followers' | 'following'

/** Lista paginada de pessoas (seguidores/seguindo de um perfil, ou as minhas: `handle = null`). */
export interface PeopleList {
  items: ProfileSummaryDto[]
  page: number
  totalPages: number
  totalCount: number
  loading: boolean
  loadingMore: boolean
  error: ApiError | null
  loaded: boolean
}

export function peopleListKey(kind: PeopleListKind, handle: string | null): string {
  return `${kind}:${handle ? normalizeHandle(handle) : '@me'}`
}

const emptyList = (): PeopleList => ({
  items: [],
  page: 0,
  totalPages: 0,
  totalCount: 0,
  loading: false,
  loadingMore: false,
  error: null,
  loaded: false,
})

/**
 * Perfis de terceiros, busca de pessoas, seguidores e seguir/deixar de seguir (contrato v1.12).
 * Todo o estado é por usuário: `reset()` (troca/encerramento de sessão) zera tudo e descarta
 * respostas em voo (geração) e a busca em andamento (AbortController).
 */
export const useProfilesStore = defineStore('profiles', () => {
  // ---- perfil aberto
  const profile = ref<ProfileDto | null>(null)
  const profileLoading = ref(false)
  const profileError = ref<ApiError | null>(null)

  // ---- busca
  const searchResults = ref<ProfileSummaryDto[]>([])
  const searchLoading = ref(false)
  const searchError = ref<ApiError | null>(null)
  /** Última consulta concluída com sucesso (distingue "sem resultados" de "ainda não buscou"). */
  const searchedQuery = ref<string | null>(null)

  // ---- listas
  const lists = ref<Record<string, PeopleList>>({})

  // ---- seguir
  const followPending = ref<Record<string, boolean>>({})

  let generation = 0
  let profileSeq = 0
  let searchController: AbortController | null = null
  const listSeq = new Map<string, number>()

  // ------------------------------------------------------------ perfil

  async function fetchProfile(handle: string) {
    const normalized = normalizeHandle(handle)
    const gen = generation
    const seq = ++profileSeq
    if (profile.value?.handle !== normalized) profile.value = null
    profileLoading.value = true
    profileError.value = null
    try {
      const result = await api.profiles.get(normalized)
      if (gen === generation && seq === profileSeq) profile.value = result
    } catch (e) {
      if (gen === generation && seq === profileSeq) {
        profile.value = null
        profileError.value = toApiError(e)
      }
    } finally {
      if (gen === generation && seq === profileSeq) profileLoading.value = false
    }
  }

  // ------------------------------------------------------------ busca

  /** Cancela a requisição em voo e limpa os resultados. */
  function clearSearch() {
    searchController?.abort()
    searchController = null
    searchResults.value = []
    searchLoading.value = false
    searchError.value = null
    searchedQuery.value = null
  }

  /** O texto mudou: aborta a requisição em voo (resposta obsoleta) e aguarda a próxima consulta. */
  function cancelSearchInFlight() {
    searchController?.abort()
    searchController = null
    searchLoading.value = true
  }

  /** Busca por apelido/nome (já com debounce e mínimo de caracteres aplicados por quem chama). */
  async function search(query: string) {
    const q = query.trim()
    searchController?.abort()
    const controller = new AbortController()
    searchController = controller
    const gen = generation
    const isCurrent = () => gen === generation && searchController === controller
    searchLoading.value = true
    searchError.value = null
    try {
      const result = await api.profiles.search(q, PEOPLE_SEARCH_LIMIT, controller.signal)
      if (!isCurrent() || controller.signal.aborted) return
      searchResults.value = result
      searchedQuery.value = q
    } catch (e) {
      if (!isCurrent() || controller.signal.aborted) return
      searchResults.value = []
      searchedQuery.value = null
      searchError.value = toApiError(e)
    } finally {
      if (isCurrent()) searchLoading.value = false
    }
  }

  // ------------------------------------------------------------ listas

  function getList(kind: PeopleListKind, handle: string | null): PeopleList {
    return lists.value[peopleListKey(kind, handle)] ?? emptyList()
  }

  function fetchPage(kind: PeopleListKind, handle: string | null, page: number) {
    const params = { page, pageSize: PEOPLE_PAGE_SIZE }
    if (handle) {
      const normalized = normalizeHandle(handle)
      return kind === 'followers'
        ? api.profiles.followers(normalized, params)
        : api.profiles.following(normalized, params)
    }
    return kind === 'followers'
      ? api.profiles.meFollowers(params)
      : api.profiles.meFollowing(params)
  }

  async function loadList(
    kind: PeopleListKind,
    handle: string | null,
    options: { more?: boolean; force?: boolean } = {},
  ) {
    const key = peopleListKey(kind, handle)
    const current = lists.value[key] ?? emptyList()
    if (options.more ? current.loadingMore || current.page >= current.totalPages : false) return
    if (!options.more && current.loaded && !options.force && !current.error) return
    const gen = generation
    const seq = (listSeq.get(key) ?? 0) + 1
    listSeq.set(key, seq)
    const isCurrent = () => gen === generation && listSeq.get(key) === seq
    const patch = (next: Partial<PeopleList>) => {
      lists.value = { ...lists.value, [key]: { ...(lists.value[key] ?? emptyList()), ...next } }
    }
    patch(options.more ? { loadingMore: true, error: null } : { loading: true, error: null })
    try {
      const result: PagedResult<ProfileSummaryDto> = await fetchPage(
        kind,
        handle,
        options.more ? current.page + 1 : 1,
      )
      if (!isCurrent()) return
      const known = new Set(options.more ? current.items.map((i) => i.handle) : [])
      patch({
        items: options.more
          ? [...current.items, ...result.items.filter((i) => !known.has(i.handle))]
          : result.items,
        page: result.page,
        totalPages: result.totalPages,
        totalCount: result.totalCount,
        loaded: true,
        loading: false,
        loadingMore: false,
      })
    } catch (e) {
      if (isCurrent()) patch({ error: toApiError(e), loading: false, loadingMore: false })
    }
  }

  const fetchList = (kind: PeopleListKind, handle: string | null, force = false) =>
    loadList(kind, handle, { force })
  const loadMore = (kind: PeopleListKind, handle: string | null) =>
    loadList(kind, handle, { more: true })

  // ------------------------------------------------------------ seguir

  /** Aplica `fn` a todas as cópias em memória do perfil (cabeçalho, busca e listas). */
  function patchEverywhere(
    handle: string,
    fn: (value: { isFollowing: boolean; followersCount: number }) => {
      isFollowing: boolean
      followersCount: number
    },
  ) {
    if (profile.value?.handle === handle) profile.value = { ...profile.value, ...fn(profile.value) }
    searchResults.value = searchResults.value.map((p) =>
      p.handle === handle ? { ...p, ...fn(p) } : p,
    )
    const next: Record<string, PeopleList> = {}
    for (const [key, list] of Object.entries(lists.value)) {
      next[key] = {
        ...list,
        items: list.items.map((p) => (p.handle === handle ? { ...p, ...fn(p) } : p)),
      }
    }
    lists.value = next
  }

  function knownState(handle: string): { isFollowing: boolean } | null {
    if (profile.value?.handle === handle) return profile.value
    const fromSearch = searchResults.value.find((p) => p.handle === handle)
    if (fromSearch) return fromSearch
    for (const list of Object.values(lists.value)) {
      const found = list.items.find((p) => p.handle === handle)
      if (found) return found
    }
    return null
  }

  /**
   * Segue/deixa de seguir de forma otimista: a interface muda de imediato e volta ao estado
   * anterior se a API falhar (o erro é repassado a quem chamou, que o exibe).
   */
  async function toggleFollow(rawHandle: string) {
    const handle = normalizeHandle(rawHandle)
    const state = knownState(handle)
    if (!state || followPending.value[handle]) return
    const follow = !state.isFollowing
    const delta = follow ? 1 : -1
    const gen = generation
    followPending.value = { ...followPending.value, [handle]: true }
    patchEverywhere(handle, (v) => ({
      isFollowing: follow,
      followersCount: Math.max(0, v.followersCount + delta),
    }))
    try {
      if (follow) await api.profiles.follow(handle)
      else await api.profiles.unfollow(handle)
    } catch (e) {
      if (gen === generation) {
        patchEverywhere(handle, (v) => ({
          isFollowing: !follow,
          followersCount: Math.max(0, v.followersCount - delta),
        }))
      }
      throw toApiError(e)
    } finally {
      if (gen === generation) {
        const { [handle]: _done, ...rest } = followPending.value
        followPending.value = rest
      }
    }
  }

  /** Troca/encerramento de sessão: zera tudo e descarta respostas em voo do usuário anterior. */
  function reset() {
    generation++
    profileSeq++
    searchController?.abort()
    searchController = null
    listSeq.clear()
    profile.value = null
    profileLoading.value = false
    profileError.value = null
    searchResults.value = []
    searchLoading.value = false
    searchError.value = null
    searchedQuery.value = null
    lists.value = {}
    followPending.value = {}
  }

  return {
    profile,
    profileLoading,
    profileError,
    searchResults,
    searchLoading,
    searchError,
    searchedQuery,
    lists,
    followPending,
    fetchProfile,
    search,
    clearSearch,
    cancelSearchInFlight,
    getList,
    fetchList,
    loadMore,
    toggleFollow,
    reset,
  }
})
