import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { ApiError } from '@/api/errors'
import type { DashboardDto, PassportsDto } from '@/types/api'
import { useStatisticsStore } from '../statistics'

const statsApi = vi.hoisted(() => ({ dashboard: vi.fn(), passports: vi.fn() }))
vi.mock('@/api', () => ({ api: { statistics: statsApi } }))

const dash = (n: number) => ({ totalReadBooks: n }) as unknown as DashboardDto
const pass = (n: number) => ({ distinctCountries: n }) as unknown as PassportsDto

describe('statistics store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('o erro do painel não aparece nos passaportes (estados independentes)', async () => {
    statsApi.dashboard.mockRejectedValue(new ApiError({ status: 501, title: 'x' }))
    statsApi.passports.mockResolvedValue(pass(3))
    const store = useStatisticsStore()

    await store.fetchDashboard()
    await store.fetchPassports()

    expect(store.dashboardError?.status).toBe(501)
    expect(store.dashboard).toBeNull()
    expect(store.passportsError).toBeNull()
    expect(store.passports?.distinctCountries).toBe(3)
    expect(store.dashboardLoading).toBe(false)
    expect(store.passportsLoading).toBe(false)
  })

  it('tentar de novo limpa o erro e carrega', async () => {
    statsApi.dashboard.mockRejectedValueOnce(new ApiError({ status: 501, title: 'x' }))
    statsApi.dashboard.mockResolvedValueOnce(dash(4))
    const store = useStatisticsStore()
    await store.fetchDashboard()
    expect(store.dashboardError).not.toBeNull()

    await store.fetchDashboard()

    expect(store.dashboardError).toBeNull()
    expect(store.dashboard?.totalReadBooks).toBe(4)
  })

  it('resposta antiga (fora de ordem) não sobrescreve a mais recente', async () => {
    let resolveOld!: (value: DashboardDto) => void
    statsApi.dashboard.mockReturnValueOnce(new Promise((res) => (resolveOld = res)))
    statsApi.dashboard.mockResolvedValueOnce(dash(2))
    const store = useStatisticsStore()
    const old = store.fetchDashboard()
    await store.fetchDashboard()
    resolveOld(dash(1))
    await old
    expect(store.dashboard?.totalReadBooks).toBe(2)
  })

  it('invalidate descarta os números; reset zera erros e carregamentos', async () => {
    statsApi.dashboard.mockResolvedValue(dash(1))
    statsApi.passports.mockRejectedValue(new ApiError({ status: 500, title: 'x' }))
    const store = useStatisticsStore()
    await store.fetchDashboard()
    await store.fetchPassports()
    store.invalidate()
    expect(store.dashboard).toBeNull()
    expect(store.passportsError).not.toBeNull()
    store.reset()
    expect(store.passportsError).toBeNull()
  })
})
