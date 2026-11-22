import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type {
  AuthResponse,
  DashboardDto,
  LibraryItemDto,
  MapDataDto,
  PagedResult,
  PassportsDto,
  ProfileCountryDetailDto,
  ProfileDto,
  ProfileSummaryDto,
} from '@/types/api'
import { tokenManager } from '@/api/tokenManager'
import { useAuthStore } from '../auth'
import { useLibraryStore } from '../library'
import { useMapsStore } from '../maps'
import { useProfileMapsStore } from '../profileMaps'
import { useProfilesStore } from '../profiles'
import { useStatisticsStore } from '../statistics'

const apiMock = vi.hoisted(() => ({
  auth: { login: vi.fn(), register: vi.fn(), refresh: vi.fn(), logout: vi.fn() },
  library: { list: vi.fn() },
  maps: { get: vi.fn(), getCountryDetail: vi.fn() },
  statistics: { dashboard: vi.fn(), passports: vi.fn() },
  profiles: {
    get: vi.fn(),
    search: vi.fn(),
    meFollowers: vi.fn(),
    followers: vi.fn(),
    follow: vi.fn(),
    map: vi.fn(),
    countryDetail: vi.fn(),
  },
}))
vi.mock('@/api', () => ({ api: apiMock }))

const session = (id: string): AuthResponse => ({
  accessToken: `access-${id}`,
  accessTokenExpiresAt: '2030-01-01T00:00:00Z',
  refreshToken: `refresh-${id}`,
  user: {
    id,
    name: id,
    email: `${id}@example.com`,
    mapVisibility: 'Private',
    role: 'User',
    createdAt: '2025-01-01T00:00:00Z',
    handle: null,
    followersCount: 0,
    followingCount: 0,
  },
})

const paged = (ids: string[]): PagedResult<LibraryItemDto> => ({
  items: ids.map((id) => ({ id, status: 'Read' }) as LibraryItemDto),
  page: 1,
  pageSize: 24,
  totalCount: ids.length,
  totalPages: 1,
})
const mapData = (marker: number) => ({ mapType: 'Authors', maxBookCount: marker }) as MapDataDto
const dashboard = (marker: number) => ({ totalBooks: marker }) as unknown as DashboardDto
const passports = (marker: number) => ({ marker }) as unknown as PassportsDto

const summary = (handle: string): ProfileSummaryDto => ({
  handle,
  name: handle,
  followersCount: 1,
  readBooksCount: 1,
  isFollowing: false,
  followsYou: false,
})
const profileDto = (handle: string): ProfileDto => ({
  ...summary(handle),
  followingCount: 0,
  exploredCountriesAuthors: 1,
  exploredCountriesOrigins: 1,
  memberSince: '2024-01-01T00:00:00Z',
  isSelf: false,
})
const paged1 = (items: ProfileSummaryDto[]): PagedResult<ProfileSummaryDto> => ({
  items,
  page: 1,
  pageSize: 20,
  totalCount: items.length,
  totalPages: 1,
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

async function populate() {
  apiMock.library.list.mockResolvedValue(paged(['a1', 'a2']))
  apiMock.maps.get.mockResolvedValue(mapData(1))
  apiMock.statistics.dashboard.mockResolvedValue(dashboard(1))
  apiMock.statistics.passports.mockResolvedValue(passports(1))
  const library = useLibraryStore()
  await library.setFilters({ ...library.filters, q: 'kafka' })
  await useMapsStore().fetchMap('Authors')
  const stats = useStatisticsStore()
  await stats.fetchDashboard()
  await stats.fetchPassports()
  await populatePeople()
}

/** Dados de perfis/seguidores/busca do usuário atual (não podem vazar para a próxima conta). */
async function populatePeople() {
  apiMock.profiles.get.mockResolvedValue(profileDto('marina_lima'))
  apiMock.profiles.search.mockResolvedValue([summary('marina_lima')])
  apiMock.profiles.meFollowers.mockResolvedValue(paged1([summary('joaoribeiro')]))
  apiMock.profiles.map.mockResolvedValue(mapData(1))
  apiMock.profiles.countryDetail.mockResolvedValue({ iso: 'BRA' } as ProfileCountryDetailDto)
  const profiles = useProfilesStore()
  await profiles.fetchProfile('marina_lima')
  await profiles.search('mar')
  await profiles.fetchList('followers', null)
  const profileMaps = useProfileMapsStore()
  await profileMaps.fetchMap('marina_lima', 'Authors')
  await profileMaps.fetchCountryDetail('marina_lima', 'Authors', 'BRA')
}

function expectEmpty() {
  const library = useLibraryStore()
  const maps = useMapsStore()
  const stats = useStatisticsStore()
  expect(library.items).toEqual([])
  expect(library.totalCount).toBe(0)
  expect(library.page).toBe(0)
  expect(library.filters.q).toBe('')
  expect(maps.data).toEqual({})
  expect(maps.countryDetail).toBeNull()
  expect(stats.dashboard).toBeNull()
  expect(stats.passports).toBeNull()
  const profiles = useProfilesStore()
  expect(profiles.profile).toBeNull()
  expect(profiles.profileError).toBeNull()
  expect(profiles.searchResults).toEqual([])
  expect(profiles.searchedQuery).toBeNull()
  expect(profiles.lists).toEqual({})
  expect(profiles.followPending).toEqual({})
  const profileMaps = useProfileMapsStore()
  expect(profileMaps.data).toEqual({})
  expect(profileMaps.countryDetail).toBeNull()
}

describe('reset dos dados por usuário na troca de sessão', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    tokenManager.clear()
    localStorage.clear()
    vi.clearAllMocks()
  })

  it('logout zera biblioteca, mapas, estatísticas, perfis e seguidores', async () => {
    apiMock.auth.login.mockResolvedValue(session('u1'))
    apiMock.auth.logout.mockResolvedValue(undefined)
    const auth = useAuthStore()
    await auth.login({ email: 'u1@example.com', password: 'x' })
    await populate()
    expect(useLibraryStore().items).toHaveLength(2)

    await auth.logout()

    expectEmpty()
  })

  it('sessão expirada (clearSession) zera os dados', async () => {
    apiMock.auth.login.mockResolvedValue(session('u1'))
    const auth = useAuthStore()
    await auth.login({ email: 'u1@example.com', password: 'x' })
    await populate()

    auth.clearSession()

    expectEmpty()
  })

  it('login com outro usuário não mostra dados do anterior e recarrega do servidor', async () => {
    apiMock.auth.login.mockResolvedValueOnce(session('u1')).mockResolvedValueOnce(session('u2'))
    const auth = useAuthStore()
    await auth.login({ email: 'u1@example.com', password: 'x' })
    await populate()

    // troca direta, sem logout intermediário
    await auth.login({ email: 'u2@example.com', password: 'x' })
    expectEmpty()

    apiMock.library.list.mockResolvedValue(paged(['b1']))
    const library = useLibraryStore()
    await library.fetch()
    expect(library.items.map((i) => i.id)).toEqual(['b1'])
    // cache do usuário anterior não é reaproveitado
    expect(apiMock.library.list).toHaveBeenCalledTimes(2)
  })

  it('refresh de token do mesmo usuário não zera as stores', async () => {
    apiMock.auth.login.mockResolvedValue(session('u1'))
    const auth = useAuthStore()
    await auth.login({ email: 'u1@example.com', password: 'x' })
    await populate()

    // o cliente HTTP renova o token e notifica o auth store
    tokenManager.applyAuthResponse({ ...session('u1'), accessToken: 'novo' })

    expect(auth.user?.id).toBe('u1')
    expect(useLibraryStore().items).toHaveLength(2)
    expect(Object.keys(useMapsStore().data)).toHaveLength(1)
    expect(useStatisticsStore().dashboard).not.toBeNull()
    expect(useProfilesStore().searchResults).toHaveLength(1)
    expect(useProfilesStore().getList('followers', null).items).toHaveLength(1)
    expect(Object.keys(useProfileMapsStore().data)).toHaveLength(1)
  })

  it('refresh que devolve outro usuário zera as stores', async () => {
    apiMock.auth.login.mockResolvedValue(session('u1'))
    const auth = useAuthStore()
    await auth.login({ email: 'u1@example.com', password: 'x' })
    await populate()

    tokenManager.applyAuthResponse(session('u2'))

    expectEmpty()
  })

  describe('respostas em voo do usuário anterior são ignoradas', () => {
    it('biblioteca (fetch e cache)', async () => {
      const slow = deferred<PagedResult<LibraryItemDto>>()
      apiMock.library.list.mockReturnValueOnce(slow.promise)
      const library = useLibraryStore()
      const pending = library.fetch()

      useAuthStore().clearSession()
      slow.resolve(paged(['velho']))
      await pending

      expect(library.items).toEqual([])
      expect(library.totalCount).toBe(0)
      expect(library.loading).toBe(false)
      // a resposta antiga não pode ter ido para o cache
      apiMock.library.list.mockResolvedValueOnce(paged(['novo']))
      await library.fetch()
      expect(library.items.map((i) => i.id)).toEqual(['novo'])
    })

    it('mapas (lista e detalhe do país)', async () => {
      const slowMap = deferred<MapDataDto>()
      const slowDetail = deferred<never>()
      apiMock.maps.get.mockReturnValueOnce(slowMap.promise)
      apiMock.maps.getCountryDetail.mockReturnValueOnce(slowDetail.promise)
      const maps = useMapsStore()
      const a = maps.fetchMap('Authors')
      const b = maps.fetchCountryDetail('Authors', 'FRA')

      useAuthStore().clearSession()
      slowMap.resolve(mapData(9))
      slowDetail.resolve({ iso: 'FRA' } as never)
      await Promise.all([a, b])

      expect(maps.data).toEqual({})
      expect(maps.countryDetail).toBeNull()
      expect(maps.detailLoading).toBe(false)
      // nada ficou em cache: nova consulta chama a API de novo
      apiMock.maps.get.mockResolvedValueOnce(mapData(2))
      await maps.fetchMap('Authors')
      expect(maps.data['Authors:Read']?.maxBookCount).toBe(2)
    })

    it('estatísticas (dashboard e passaportes)', async () => {
      const slowDash = deferred<DashboardDto>()
      const slowPass = deferred<PassportsDto>()
      apiMock.statistics.dashboard.mockReturnValueOnce(slowDash.promise)
      apiMock.statistics.passports.mockReturnValueOnce(slowPass.promise)
      const stats = useStatisticsStore()
      const a = stats.fetchDashboard()
      const b = stats.fetchPassports()

      useAuthStore().clearSession()
      slowDash.resolve(dashboard(9))
      slowPass.resolve(passports(9))
      await Promise.all([a, b])

      expect(stats.dashboard).toBeNull()
      expect(stats.passports).toBeNull()
      expect(stats.dashboardLoading).toBe(false)
      expect(stats.passportsLoading).toBe(false)
    })

    it('perfis: perfil, busca e listas de seguidores', async () => {
      const slowProfile = deferred<ProfileDto>()
      const slowSearch = deferred<ProfileSummaryDto[]>()
      const slowList = deferred<PagedResult<ProfileSummaryDto>>()
      apiMock.profiles.get.mockReturnValueOnce(slowProfile.promise)
      apiMock.profiles.search.mockReturnValueOnce(slowSearch.promise)
      apiMock.profiles.meFollowers.mockReturnValueOnce(slowList.promise)
      const profiles = useProfilesStore()
      const a = profiles.fetchProfile('antigo')
      const b = profiles.search('an')
      const c = profiles.fetchList('followers', null)

      useAuthStore().clearSession() // troca de conta
      slowProfile.resolve(profileDto('antigo'))
      slowSearch.resolve([summary('antigo')])
      slowList.resolve(paged1([summary('antigo')]))
      await Promise.all([a, b, c])

      expect(profiles.profile).toBeNull()
      expect(profiles.profileLoading).toBe(false)
      expect(profiles.searchResults).toEqual([])
      expect(profiles.searchLoading).toBe(false)
      expect(profiles.searchedQuery).toBeNull()
      expect(profiles.lists).toEqual({})
    })

    it('perfis: a busca em andamento é abortada na troca de conta', async () => {
      let signal!: AbortSignal
      apiMock.profiles.search.mockImplementationOnce((_q: string, _l: number, s: AbortSignal) => {
        signal = s
        return new Promise(() => undefined)
      })
      void useProfilesStore().search('an')
      useAuthStore().clearSession()
      expect(signal.aborted).toBe(true)
    })

    it('perfis: seguir em voo não reescreve estado da conta nova ao falhar', async () => {
      const call = deferred<void>()
      apiMock.profiles.search.mockResolvedValueOnce([summary('marina_lima')])
      apiMock.profiles.follow.mockReturnValueOnce(call.promise)
      const profiles = useProfilesStore()
      await profiles.search('ma')
      const pending = profiles.toggleFollow('marina_lima').catch(() => undefined)

      useAuthStore().clearSession()
      call.reject(new Error('falhou'))
      await pending

      expect(profiles.searchResults).toEqual([])
      expect(profiles.followPending).toEqual({})
    })

    it('mapas de perfis (mapa e detalhe do país)', async () => {
      const slowMap = deferred<MapDataDto>()
      const slowDetail = deferred<ProfileCountryDetailDto>()
      apiMock.profiles.map.mockReturnValueOnce(slowMap.promise)
      apiMock.profiles.countryDetail.mockReturnValueOnce(slowDetail.promise)
      const maps = useProfileMapsStore()
      const a = maps.fetchMap('marina_lima', 'Authors')
      const b = maps.fetchCountryDetail('marina_lima', 'Authors', 'FRA')

      useAuthStore().clearSession()
      slowMap.resolve(mapData(9))
      slowDetail.resolve({ iso: 'FRA' } as ProfileCountryDetailDto)
      await Promise.all([a, b])

      expect(maps.data).toEqual({})
      expect(maps.countryDetail).toBeNull()
      expect(maps.detailLoading).toBe(false)
      apiMock.profiles.map.mockResolvedValueOnce(mapData(2))
      await maps.fetchMap('marina_lima', 'Authors')
      expect(maps.data['marina_lima:Authors']?.maxBookCount).toBe(2)
    })
  })
})
