import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { ApiError } from '@/api/errors'
import type { PagedResult, ProfileDto, ProfileSummaryDto } from '@/types/api'
import { peopleListKey, useProfilesStore } from '../profiles'

const profilesApi = vi.hoisted(() => ({
  search: vi.fn(),
  get: vi.fn(),
  follow: vi.fn(),
  unfollow: vi.fn(),
  followers: vi.fn(),
  following: vi.fn(),
  meFollowers: vi.fn(),
  meFollowing: vi.fn(),
}))
vi.mock('@/api', () => ({ api: { profiles: profilesApi } }))

const person = (handle: string, extra: Partial<ProfileSummaryDto> = {}): ProfileSummaryDto => ({
  handle,
  name: handle.toUpperCase(),
  followersCount: 5,
  readBooksCount: 10,
  isFollowing: false,
  followsYou: false,
  ...extra,
})
const fullProfile = (handle: string, extra: Partial<ProfileDto> = {}): ProfileDto => ({
  ...person(handle),
  followingCount: 1,
  exploredCountriesAuthors: 3,
  exploredCountriesOrigins: 2,
  memberSince: '2024-01-01T00:00:00Z',
  isSelf: false,
  ...extra,
})
const paged = (
  items: ProfileSummaryDto[],
  page = 1,
  totalPages = 1,
): PagedResult<ProfileSummaryDto> => ({
  items,
  page,
  pageSize: 20,
  totalCount: items.length,
  totalPages,
})

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

describe('profiles store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  describe('perfil', () => {
    it('carrega o perfil e normaliza o apelido', async () => {
      profilesApi.get.mockResolvedValue(fullProfile('marina_lima'))
      const store = useProfilesStore()
      await store.fetchProfile('@Marina_Lima ')
      expect(profilesApi.get).toHaveBeenCalledWith('marina_lima')
      expect(store.profile?.handle).toBe('marina_lima')
    })

    it('404 profile.not_found (perfil privado) vira erro, sem perfil', async () => {
      profilesApi.get.mockRejectedValue(
        new ApiError({ status: 404, title: 'x', code: 'profile.not_found' }),
      )
      const store = useProfilesStore()
      await store.fetchProfile('bia_prado')
      expect(store.profile).toBeNull()
      expect(store.profileError?.code).toBe('profile.not_found')
      expect(store.profileLoading).toBe(false)
    })

    it('só vale a última solicitação (resposta de outro perfil é descartada)', async () => {
      const slow = deferred<ProfileDto>()
      profilesApi.get.mockReturnValueOnce(slow.promise)
      profilesApi.get.mockResolvedValueOnce(fullProfile('joaoribeiro'))
      const store = useProfilesStore()
      const first = store.fetchProfile('marina_lima')
      await store.fetchProfile('joaoribeiro')
      slow.resolve(fullProfile('marina_lima'))
      await first
      expect(store.profile?.handle).toBe('joaoribeiro')
    })
  })

  describe('busca', () => {
    it('chama a API com limite 10 e AbortSignal e guarda a consulta concluída', async () => {
      profilesApi.search.mockResolvedValue([person('marina_lima')])
      const store = useProfilesStore()
      await store.search(' mari ')
      expect(profilesApi.search).toHaveBeenCalledWith('mari', 10, expect.any(AbortSignal))
      expect(store.searchResults.map((p) => p.handle)).toEqual(['marina_lima'])
      expect(store.searchedQuery).toBe('mari')
    })

    it('nova busca cancela a anterior: a resposta velha nunca é aplicada', async () => {
      const old = deferred<ProfileSummaryDto[]>()
      let oldSignal!: AbortSignal
      profilesApi.search.mockImplementationOnce((_q: string, _l: number, signal: AbortSignal) => {
        oldSignal = signal
        return old.promise
      })
      profilesApi.search.mockResolvedValueOnce([person('novo')])
      const store = useProfilesStore()
      const first = store.search('ma')
      await store.search('mar')
      expect(oldSignal.aborted).toBe(true)
      old.resolve([person('velho')])
      await first
      expect(store.searchResults.map((p) => p.handle)).toEqual(['novo'])
      expect(store.searchedQuery).toBe('mar')
      expect(store.searchLoading).toBe(false)
    })

    it('erro da API é exposto e limpa os resultados', async () => {
      profilesApi.search.mockRejectedValue(new ApiError({ status: 502, title: 'x' }))
      const store = useProfilesStore()
      await store.search('ma')
      expect(store.searchError?.status).toBe(502)
      expect(store.searchResults).toEqual([])
    })

    it('clearSearch aborta a requisição em voo e zera o estado', async () => {
      const pending = deferred<ProfileSummaryDto[]>()
      let signal!: AbortSignal
      profilesApi.search.mockImplementationOnce((_q: string, _l: number, s: AbortSignal) => {
        signal = s
        return pending.promise
      })
      const store = useProfilesStore()
      const run = store.search('ma')
      store.clearSearch()
      pending.resolve([person('x')])
      await run
      expect(signal.aborted).toBe(true)
      expect(store.searchResults).toEqual([])
      expect(store.searchLoading).toBe(false)
    })
  })

  describe('seguir / deixar de seguir (otimista)', () => {
    async function withSearch(...people: ProfileSummaryDto[]) {
      profilesApi.search.mockResolvedValue(people)
      const store = useProfilesStore()
      await store.search('ma')
      return store
    }

    it('seguir atualiza de imediato (antes da API) e confirma', async () => {
      const call = deferred<void>()
      profilesApi.follow.mockReturnValue(call.promise)
      const store = await withSearch(person('marina_lima', { followersCount: 5 }))

      const pending = store.toggleFollow('marina_lima')
      // otimista: já mudou, com a API ainda em voo
      expect(store.searchResults[0]).toMatchObject({ isFollowing: true, followersCount: 6 })
      expect(store.followPending.marina_lima).toBe(true)
      call.resolve()
      await pending

      expect(profilesApi.follow).toHaveBeenCalledWith('marina_lima')
      expect(store.searchResults[0]).toMatchObject({ isFollowing: true, followersCount: 6 })
      expect(store.followPending.marina_lima).toBeUndefined()
    })

    it('deixar de seguir chama DELETE e reduz a contagem', async () => {
      profilesApi.unfollow.mockResolvedValue(undefined)
      const store = await withSearch(
        person('marina_lima', { isFollowing: true, followersCount: 5 }),
      )
      await store.toggleFollow('marina_lima')
      expect(profilesApi.unfollow).toHaveBeenCalledWith('marina_lima')
      expect(store.searchResults[0]).toMatchObject({ isFollowing: false, followersCount: 4 })
    })

    it('rollback em erro: volta ao estado anterior e repassa o erro', async () => {
      profilesApi.follow.mockRejectedValue(new ApiError({ status: 500, title: 'x' }))
      const store = await withSearch(person('marina_lima', { followersCount: 5 }))

      await expect(store.toggleFollow('marina_lima')).rejects.toMatchObject({ status: 500 })

      expect(store.searchResults[0]).toMatchObject({ isFollowing: false, followersCount: 5 })
      expect(store.followPending.marina_lima).toBeUndefined()
    })

    it('seguir a si mesmo (400 follow.self) é revertido e o código chega ao chamador', async () => {
      profilesApi.follow.mockRejectedValue(
        new ApiError({ status: 400, title: 'x', code: 'follow.self' }),
      )
      const store = await withSearch(person('eu_mesmo'))
      await expect(store.toggleFollow('eu_mesmo')).rejects.toMatchObject({ code: 'follow.self' })
      expect(store.searchResults[0]?.isFollowing).toBe(false)
    })

    it('perfil que virou privado (404 ao seguir) também é revertido', async () => {
      profilesApi.follow.mockRejectedValue(
        new ApiError({ status: 404, title: 'x', code: 'profile.not_found' }),
      )
      const store = await withSearch(person('bia_prado'))
      await expect(store.toggleFollow('bia_prado')).rejects.toMatchObject({
        code: 'profile.not_found',
      })
      expect(store.searchResults[0]?.isFollowing).toBe(false)
    })

    it('atualiza todas as cópias: cabeçalho do perfil, busca e listas', async () => {
      profilesApi.follow.mockResolvedValue(undefined)
      profilesApi.get.mockResolvedValue(fullProfile('marina_lima', { followersCount: 5 }))
      profilesApi.search.mockResolvedValue([person('marina_lima', { followersCount: 5 })])
      profilesApi.followers.mockResolvedValue(paged([person('marina_lima', { followersCount: 5 })]))
      const store = useProfilesStore()
      await store.fetchProfile('marina_lima')
      await store.search('ma')
      await store.fetchList('followers', 'outro')

      await store.toggleFollow('marina_lima')

      expect(store.profile).toMatchObject({ isFollowing: true, followersCount: 6 })
      expect(store.searchResults[0]).toMatchObject({ isFollowing: true, followersCount: 6 })
      expect(store.getList('followers', 'outro').items[0]).toMatchObject({
        isFollowing: true,
        followersCount: 6,
      })
    })

    it('cliques repetidos enquanto pendente são ignorados', async () => {
      const call = deferred<void>()
      profilesApi.follow.mockReturnValue(call.promise)
      const store = await withSearch(person('marina_lima'))
      const first = store.toggleFollow('marina_lima')
      await store.toggleFollow('marina_lima')
      call.resolve()
      await first
      expect(profilesApi.follow).toHaveBeenCalledTimes(1)
      expect(profilesApi.unfollow).not.toHaveBeenCalled()
    })

    it('apelido desconhecido (nenhuma cópia em memória) não chama a API', async () => {
      const store = useProfilesStore()
      await store.toggleFollow('ninguem')
      expect(profilesApi.follow).not.toHaveBeenCalled()
    })
  })

  describe('listas', () => {
    it('seguidores de um perfil usam /profiles/{handle}/followers; as minhas usam /me/*', async () => {
      profilesApi.followers.mockResolvedValue(paged([person('a')]))
      profilesApi.meFollowing.mockResolvedValue(paged([person('b')]))
      const store = useProfilesStore()
      await store.fetchList('followers', 'Marina_Lima')
      await store.fetchList('following', null)
      expect(profilesApi.followers).toHaveBeenCalledWith('marina_lima', { page: 1, pageSize: 20 })
      expect(profilesApi.meFollowing).toHaveBeenCalledWith({ page: 1, pageSize: 20 })
      expect(store.getList('followers', 'marina_lima').items[0]?.handle).toBe('a')
      expect(store.getList('following', null).items[0]?.handle).toBe('b')
      expect(Object.keys(store.lists)).toEqual([
        peopleListKey('followers', 'marina_lima'),
        peopleListKey('following', null),
      ])
    })

    it('carregar mais acrescenta a próxima página sem duplicar', async () => {
      profilesApi.meFollowers.mockResolvedValueOnce(paged([person('a'), person('b')], 1, 2))
      profilesApi.meFollowers.mockResolvedValueOnce(paged([person('b'), person('c')], 2, 2))
      const store = useProfilesStore()
      await store.fetchList('followers', null)
      await store.loadMore('followers', null)
      expect(profilesApi.meFollowers).toHaveBeenLastCalledWith({ page: 2, pageSize: 20 })
      expect(store.getList('followers', null).items.map((p) => p.handle)).toEqual(['a', 'b', 'c'])
      // última página: nada mais a carregar
      await store.loadMore('followers', null)
      expect(profilesApi.meFollowers).toHaveBeenCalledTimes(2)
    })

    it('lista já carregada não é buscada de novo, exceto com force', async () => {
      profilesApi.meFollowers.mockResolvedValue(paged([person('a')]))
      const store = useProfilesStore()
      await store.fetchList('followers', null)
      await store.fetchList('followers', null)
      expect(profilesApi.meFollowers).toHaveBeenCalledTimes(1)
      await store.fetchList('followers', null, true)
      expect(profilesApi.meFollowers).toHaveBeenCalledTimes(2)
    })

    it('404 na lista de um perfil vira erro da lista', async () => {
      profilesApi.followers.mockRejectedValue(
        new ApiError({ status: 404, title: 'x', code: 'profile.not_found' }),
      )
      const store = useProfilesStore()
      await store.fetchList('followers', 'bia_prado')
      expect(store.getList('followers', 'bia_prado').error?.code).toBe('profile.not_found')
      expect(store.getList('followers', 'bia_prado').loading).toBe(false)
    })
  })
})
