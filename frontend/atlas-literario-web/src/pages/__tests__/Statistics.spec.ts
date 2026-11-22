import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { defineComponent, nextTick } from 'vue'
import { ApiError } from '@/api/errors'
import { useAuthStore } from '@/stores/auth'
import type { DashboardDto, PassportsDto } from '@/types/api'
import DashboardPage from '../DashboardPage.vue'
import PassportsPage from '../PassportsPage.vue'

const statsApi = vi.hoisted(() => ({ dashboard: vi.fn(), passports: vi.fn() }))
vi.mock('@/api', () => ({ api: { statistics: statsApi } }))

const Stub = defineComponent({ render: () => null })

const progress = { exploredCountries: 6, totalCountries: 250, percentage: 2.4 }
const dashboard: DashboardDto = {
  totalReadBooks: 15,
  totalAuthors: 14,
  countriesByAuthors: 9,
  countriesByOrigins: 8,
  distinctCountries: 12,
  continentsExplored: 4,
  readingNow: 2,
  wantToRead: 3,
  recentlyFinished: [
    {
      libraryItemId: 'i1',
      bookId: 'b1',
      title: 'Farol',
      coverUrl: null,
      authors: ['Virginia Woolf'],
      finishedDate: '2026-04-15',
      rating: 3,
    },
  ],
  topCountries: [
    { iso: 'BRA', name: 'Brasil', bookCount: 4, mapType: 'Authors' },
    { iso: 'FRA', name: 'França', bookCount: 2, mapType: 'Authors' },
    { iso: 'COL', name: 'Colômbia', bookCount: 3, mapType: 'Origins' },
  ],
  passportProgress: progress,
}
const datum = (iso: string, name: string, bookCount: number) => ({
  iso,
  name,
  continent: 'Europe' as const,
  bookCount,
  authorCount: 1,
})
const passports: PassportsDto = {
  authors: {
    exploredCountries: 9,
    topCountry: { iso: 'BRA', name: 'Brasil', bookCount: 4, mapType: 'Authors' },
    booksByCountry: [datum('BRA', 'Brasil', 4), datum('FRA', 'França', 2)],
    unknownBookCount: 1,
  },
  origins: { exploredCountries: 8, topCountry: null, booksByCountry: [], unknownBookCount: 0 },
  distinctCountries: 12,
  continents: [
    { continent: 'Europe', exploredCountries: 3, totalCountries: 44 },
    { continent: 'Oceania', exploredCountries: 0, totalCountries: 14 },
  ],
  timeline: [
    {
      month: '2025-01',
      booksFinished: 1,
      cumulativeBooks: 1,
      cumulativeCountriesAuthors: 1,
      cumulativeCountriesOrigins: 1,
      cumulativeDistinctCountries: 1,
    },
    {
      month: '2025-02',
      booksFinished: 0,
      cumulativeBooks: 1,
      cumulativeCountriesAuthors: 1,
      cumulativeCountriesOrigins: 1,
      cumulativeDistinctCountries: 1,
    },
    {
      month: '2025-03',
      booksFinished: 2,
      cumulativeBooks: 3,
      cumulativeCountriesAuthors: 3,
      cumulativeCountriesOrigins: 2,
      cumulativeDistinctCountries: 4,
    },
  ],
  progress,
}

function mountPage(component: typeof DashboardPage | typeof PassportsPage) {
  const pinia = createPinia()
  setActivePinia(pinia)
  useAuthStore().user = {
    id: 'u',
    name: 'Ana Souza',
    email: 'a@x.com',
    mapVisibility: 'Private',
    role: 'User',
    createdAt: '2025-01-01T00:00:00Z',
    handle: null,
    followersCount: 0,
    followingCount: 0,
  }
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: Stub },
      { path: '/library', name: 'library', component: Stub },
      { path: '/library/:id', name: 'library-item', component: Stub },
      { path: '/search', name: 'search', component: Stub },
      { path: '/map', name: 'map', component: Stub },
    ],
  })
  void router.push('/')
  return mount(component, { global: { plugins: [router, pinia] } })
}

describe('DashboardPage', () => {
  beforeEach(() => vi.clearAllMocks())

  it('mostra spinner e depois os números do contrato (livros, autores, países, progresso)', async () => {
    statsApi.dashboard.mockResolvedValue(dashboard)
    const wrapper = mountPage(DashboardPage)
    await nextTick()
    expect(wrapper.find('[role="status"]').exists()).toBe(true)
    await flushPromises()

    expect(wrapper.find('h1').text()).toBe('Olá, Ana.')
    const tiles = wrapper.find('[role="group"]').text()
    expect(tiles).toContain('15')
    expect(tiles).toContain('14 autores')
    expect(tiles).toContain('2,4% do mundo')
    expect(tiles).toContain('2')
    expect(tiles).toContain('3 na lista de desejos')
    expect(wrapper.text()).toContain('Farol')
  })

  it('"países com mais livros" são agrupados por mapa', async () => {
    statsApi.dashboard.mockResolvedValue(dashboard)
    const wrapper = mountPage(DashboardPage)
    await flushPromises()
    const groups = wrapper.findAll('.top-group')
    expect(groups).toHaveLength(2)
    expect(groups[0]!.text()).toContain('Nacionalidades dos autores')
    expect(groups[0]!.text()).toContain('Brasil')
    expect(groups[0]!.text()).toContain('França')
    expect(groups[1]!.text()).toContain('Origem das obras')
    expect(groups[1]!.text()).toContain('Colômbia')
  })

  it('estado vazio (sem leituras concluídas) tem mensagens amigáveis', async () => {
    statsApi.dashboard.mockResolvedValue({
      ...dashboard,
      totalReadBooks: 0,
      recentlyFinished: [],
      topCountries: [],
    })
    const wrapper = mountPage(DashboardPage)
    await flushPromises()
    expect(wrapper.text()).toContain('Nenhuma leitura concluída ainda.')
    expect(wrapper.text()).toContain('Conclua leituras para ver seus países.')
  })

  it('erro 501 usa a mensagem da camada central, com role="alert", e permite tentar de novo', async () => {
    statsApi.dashboard.mockRejectedValueOnce(new ApiError({ status: 501, title: 'x' }))
    const wrapper = mountPage(DashboardPage)
    await flushPromises()
    const alert = wrapper.find('[role="alert"]')
    expect(alert.text()).toContain('Não foi possível carregar o painel')
    expect(alert.text()).toContain('Este recurso ainda não está disponível.')

    statsApi.dashboard.mockResolvedValueOnce(dashboard)
    await alert.find('button').trigger('click')
    await flushPromises()
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('Farol')
  })
})

describe('PassportsPage', () => {
  beforeEach(() => vi.clearAllMocks())

  it('resumo, continentes com progressbar acessível, rankings por mapa e evolução', async () => {
    statsApi.passports.mockResolvedValue(passports)
    const wrapper = mountPage(PassportsPage)
    await flushPromises()

    expect(wrapper.find('[role="group"]').text()).toContain('12')
    expect(wrapper.find('[role="group"]').text()).toContain('1 livro sem nacionalidade conhecida')
    expect(wrapper.find('[role="group"]').text()).toContain('2,4%')

    const bars = wrapper.findAll('[role="progressbar"]')
    expect(bars).toHaveLength(2)
    expect(bars[0]!.attributes('aria-valuenow')).toBe('3')
    expect(bars[0]!.attributes('aria-valuemax')).toBe('44')
    expect(bars[0]!.attributes('aria-valuetext')).toBe('3 de 44 países')

    const rankings = wrapper.findAll('.rankings section')
    expect(rankings[0]!.text()).toContain('Brasil')
    expect(rankings[0]!.text()).toContain('França')
    expect(rankings[1]!.text()).toContain('Nenhum país identificado neste mapa ainda.')

    const chart = wrapper.find('svg[role="img"]')
    expect(chart.attributes('aria-label')).toContain('3 livros lidos')
    expect(chart.attributes('aria-label')).toContain('4 países explorados')
    expect(wrapper.findAll('tbody tr')).toHaveLength(3)
  })

  it('passaporte em branco mostra estado vazio com ação', async () => {
    statsApi.passports.mockResolvedValue({
      authors: { exploredCountries: 0, topCountry: null, booksByCountry: [], unknownBookCount: 0 },
      origins: { exploredCountries: 0, topCountry: null, booksByCountry: [], unknownBookCount: 0 },
      distinctCountries: 0,
      continents: [],
      timeline: [],
      progress: { exploredCountries: 0, totalCountries: 250, percentage: 0 },
    } satisfies PassportsDto)
    const wrapper = mountPage(PassportsPage)
    await flushPromises()
    expect(wrapper.text()).toContain('Seu passaporte ainda está em branco')
    expect(wrapper.find('[role="progressbar"]').exists()).toBe(false)
  })

  it('erro mostra a mensagem central com "Tentar novamente"; erro do painel não vaza para cá', async () => {
    statsApi.passports.mockRejectedValueOnce(new ApiError({ status: 501, title: 'x' }))
    const wrapper = mountPage(PassportsPage)
    await flushPromises()
    expect(wrapper.find('[role="alert"]').text()).toContain(
      'Este recurso ainda não está disponível.',
    )

    statsApi.passports.mockResolvedValueOnce(passports)
    await wrapper.find('[role="alert"] button').trigger('click')
    await flushPromises()
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('Continentes')
  })
})
