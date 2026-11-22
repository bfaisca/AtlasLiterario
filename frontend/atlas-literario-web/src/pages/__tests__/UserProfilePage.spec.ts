/* eslint-disable vue/one-component-per-file */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { defineComponent, h } from 'vue'
import { ApiError } from '@/api/errors'
import { useAuthStore } from '@/stores/auth'
import type {
  MapDataDto,
  MapType,
  ProfileCountryDetailDto,
  ProfileDto,
  ProfileSummaryDto,
} from '@/types/api'
import UserProfilePage from '../UserProfilePage.vue'

const profilesApi = vi.hoisted(() => ({
  get: vi.fn(),
  follow: vi.fn(),
  unfollow: vi.fn(),
  followers: vi.fn(),
  following: vi.fn(),
  map: vi.fn(),
  countryDetail: vi.fn(),
}))
vi.mock('@/api', () => ({ api: { profiles: profilesApi } }))

const Stub = defineComponent({ render: () => null })
const WorldMapStub = defineComponent({
  props: { selectedIso: { type: String, default: null } },
  emits: ['country-select', 'country-hover'],
  setup(props, { emit }) {
    return () =>
      h('div', { 'data-testid': 'world-map', 'data-selected': props.selectedIso ?? '' }, [
        h('button', { id: 'tap-col', onClick: () => emit('country-select', 'COL') }),
      ])
  },
})

const marina: ProfileDto = {
  handle: 'marina_lima',
  name: 'Marina Lima',
  followersCount: 5,
  readBooksCount: 7,
  isFollowing: false,
  followsYou: true,
  followingCount: 3,
  exploredCountriesAuthors: 6,
  exploredCountriesOrigins: 4,
  memberSince: '2024-03-12T10:00:00Z',
  isSelf: false,
}

const country = (iso: string, name: string) => ({
  iso,
  name,
  continent: 'SouthAmerica' as const,
  bookCount: 1,
  authorCount: 1,
})
const mapFor = (mapType: MapType): MapDataDto => ({
  mapType,
  status: 'Read',
  totalReadBooks: 7,
  exploredCountries: 1,
  maxBookCount: 1,
  unknownBookCount: 0,
  countries: [country('COL', 'Colômbia')],
})
const book = {
  bookId: 'b1',
  title: 'Cem Anos de Solidão',
  coverUrl: null,
  authors: ['Gabriel García Márquez'],
  finishedDate: '2025-01-10',
  rating: 5,
  review: 'Macondo é um país inteiro dentro de uma família.',
}
const detailFor = (mapType: MapType): ProfileCountryDetailDto => ({
  mapType,
  iso: 'COL',
  name: 'Colômbia',
  continent: 'SouthAmerica',
  bookCount: 1,
  authorCount: 1,
  books: [book],
  authors:
    mapType === 'Authors'
      ? [
          {
            authorId: 'a1',
            name: 'Gabriel García Márquez',
            countryIso: 'COL',
            countryName: 'Colômbia',
            books: [book],
          },
        ]
      : [],
  status: 'Read',
})
const follower = (handle: string): ProfileSummaryDto => ({
  handle,
  name: handle,
  followersCount: 1,
  readBooksCount: 2,
  isFollowing: false,
  followsYou: false,
})

async function setup(url: string, me: { handle: string | null } = { handle: 'leitora' }) {
  const pinia = createPinia()
  setActivePinia(pinia)
  useAuthStore().user = {
    id: 'me',
    name: 'Eu',
    email: 'e@x.com',
    mapVisibility: 'Public',
    role: 'User',
    createdAt: '2025-01-01T00:00:00Z',
    handle: me.handle,
    followersCount: 0,
    followingCount: 0,
  }
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/u/:handle', name: 'user-profile', component: UserProfilePage, props: true },
      { path: '/pessoas', name: 'people', component: Stub },
      { path: '/profile', name: 'profile', component: Stub },
      { path: '/library/:id', name: 'library-item', component: Stub },
    ],
  })
  await router.push(url)
  const wrapper = mount(UserProfilePage, {
    props: { handle: String(router.currentRoute.value.params.handle) },
    global: { plugins: [router, pinia], stubs: { WorldMap: WorldMapStub } },
    attachTo: document.body,
  })
  await flushPromises()
  return { wrapper, router }
}

describe('UserProfilePage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    profilesApi.get.mockResolvedValue(marina)
    profilesApi.map.mockImplementation(async (_h: string, type: MapType) => mapFor(type))
    profilesApi.countryDetail.mockImplementation(async (_h: string, type: MapType) =>
      detailFor(type),
    )
    profilesApi.followers.mockResolvedValue({
      items: [follower('ana'), follower('bruno')],
      page: 1,
      pageSize: 20,
      totalCount: 2,
      totalPages: 1,
    })
    profilesApi.following.mockResolvedValue({
      items: [follower('carla')],
      page: 1,
      pageSize: 20,
      totalCount: 1,
      totalPages: 1,
    })
  })
  afterEach(() => {
    document.body.innerHTML = ''
  })

  describe('cabeçalho', () => {
    it('mostra nome, @apelido, desde, contagens e "segue você"', async () => {
      const { wrapper } = await setup('/u/marina_lima')
      expect(profilesApi.get).toHaveBeenCalledWith('marina_lima')
      expect(wrapper.find('h1').text()).toBe('Marina Lima')
      expect(wrapper.text()).toContain('@marina_lima')
      expect(wrapper.text()).toContain('Leitor desde')
      expect(wrapper.find('[data-testid="follows-you"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="count-followers"]').text()).toBe('5')
      expect(wrapper.find('[data-testid="count-following"]').text()).toBe('3')
      expect(wrapper.find('dl').text()).toContain('Livros lidos')
      expect(wrapper.find('dl').text()).toContain('7')
      expect(wrapper.find('dl').text()).toContain('Países · autores')
    })

    it('quem já me segue tem o botão "Seguir de volta"; seguir é otimista e confirma', async () => {
      let finish!: () => void
      profilesApi.follow.mockReturnValue(new Promise<void>((res) => (finish = res)))
      const { wrapper } = await setup('/u/marina_lima')
      const button = wrapper.find('[data-testid="follow-button"]')
      expect(button.text()).toBe('Seguir de volta')

      await button.trigger('click')
      // otimista: antes da resposta
      expect(button.text()).toBe('Seguindo')
      expect(wrapper.find('[data-testid="count-followers"]').text()).toBe('6')
      finish()
      await flushPromises()

      expect(profilesApi.follow).toHaveBeenCalledWith('marina_lima')
      expect(button.text()).toBe('Seguindo')
      expect(button.attributes('aria-pressed')).toBe('true')
    })

    it('falha ao seguir reverte o botão e a contagem', async () => {
      profilesApi.follow.mockRejectedValue(new ApiError({ status: 500, title: 'x' }))
      const { wrapper } = await setup('/u/marina_lima')
      await wrapper.find('[data-testid="follow-button"]').trigger('click')
      await flushPromises()
      expect(wrapper.find('[data-testid="follow-button"]').text()).toBe('Seguir de volta')
      expect(wrapper.find('[data-testid="count-followers"]').text()).toBe('5')
    })

    it('seguindo → "Seguindo" e clicar deixa de seguir', async () => {
      profilesApi.get.mockResolvedValue({ ...marina, isFollowing: true })
      profilesApi.unfollow.mockResolvedValue(undefined)
      const { wrapper } = await setup('/u/marina_lima')
      const button = wrapper.find('[data-testid="follow-button"]')
      expect(button.text()).toBe('Seguindo')
      await button.trigger('click')
      await flushPromises()
      expect(profilesApi.unfollow).toHaveBeenCalledWith('marina_lima')
      expect(button.text()).toBe('Seguir de volta')
    })

    it('o próprio perfil não tem botão de seguir', async () => {
      profilesApi.get.mockResolvedValue({ ...marina, handle: 'leitora', isSelf: true })
      const { wrapper } = await setup('/u/leitora')
      expect(wrapper.find('[data-testid="follow-button"]').exists()).toBe(false)
      expect(wrapper.text()).toContain('Este é o seu perfil')
    })
  })

  describe('perfil privado ou inexistente (404 profile.not_found)', () => {
    it('mostra estado vazio amigável, sem cabeçalho nem mapa', async () => {
      profilesApi.get.mockRejectedValue(
        new ApiError({ status: 404, title: 'x', code: 'profile.not_found' }),
      )
      const { wrapper } = await setup('/u/bia_prado')
      const empty = wrapper.find('[data-testid="profile-not-found"]')
      expect(empty.exists()).toBe(true)
      expect(empty.text()).toContain('Perfil não encontrado ou privado')
      expect(wrapper.find('[data-testid="follow-button"]').exists()).toBe(false)
      expect(wrapper.find('[data-testid="world-map"]').exists()).toBe(false)
      expect(profilesApi.map).not.toHaveBeenCalled()
      expect(empty.find('a').attributes('href')).toBe('/pessoas')
    })

    it('outros erros mostram a mensagem e permitem tentar de novo', async () => {
      profilesApi.get.mockRejectedValueOnce(new ApiError({ status: 501, title: 'x' }))
      const { wrapper } = await setup('/u/marina_lima')
      expect(wrapper.find('[role="alert"]').text()).toContain('ainda não está disponível')
      profilesApi.get.mockResolvedValueOnce(marina)
      await wrapper.find('[role="alert"] button').trigger('click')
      await flushPromises()
      expect(wrapper.find('h1').text()).toBe('Marina Lima')
    })
  })

  describe('mapa de terceiros (reusa WorldMap e o painel)', () => {
    it('não tem filtro de status e carrega o mapa de autores por padrão', async () => {
      const { wrapper } = await setup('/u/marina_lima')
      expect(profilesApi.map).toHaveBeenCalledWith('marina_lima', 'Authors')
      expect(wrapper.find('[data-testid="world-map"]').exists()).toBe(true)
      expect(wrapper.text()).not.toContain('Quero ler')
      expect(wrapper.find('[role="tab"][aria-selected="true"]').text()).toBe('Mapa de autores')
    })

    it('autores: painel com cards expansíveis, livros com nota e resenha, sem link de biblioteca', async () => {
      const { wrapper, router } = await setup('/u/marina_lima')
      await wrapper.find('#tap-col').trigger('click')
      await flushPromises()

      expect(router.currentRoute.value.query.country).toBe('COL')
      expect(profilesApi.countryDetail).toHaveBeenCalledWith('marina_lima', 'Authors', 'COL')
      const panel = wrapper.find('[data-testid="country-panel"]')
      expect(panel.text()).toContain('Colômbia')
      const card = panel.find('[data-testid="author-card"]')
      expect(card.text()).toContain('Gabriel García Márquez')
      await card.find('[data-testid="author-card-toggle"]').trigger('click')
      expect(card.text()).toContain('Cem Anos de Solidão')
      expect(card.text()).toContain('Lido em')
      // resenha sob demanda
      const toggle = card.find('[data-testid="review-toggle"]')
      expect(toggle.text()).toBe('Ler resenha')
      await toggle.trigger('click')
      expect(card.find('[data-testid="review-text"]').text()).toContain('Macondo é um país inteiro')
      // nada que leve à biblioteca do usuário (não é a biblioteca de quem vê)
      expect(panel.find('a').exists()).toBe(false)
      expect(panel.html()).not.toContain('/library/')
    })

    it('obras: aba pela URL; lista de livros com resenha', async () => {
      const { wrapper } = await setup('/u/marina_lima?tab=origins&country=COL')
      expect(profilesApi.map).toHaveBeenCalledWith('marina_lima', 'Origins')
      expect(profilesApi.countryDetail).toHaveBeenCalledWith('marina_lima', 'Origins', 'COL')
      const panel = wrapper.find('[data-testid="country-panel"]')
      expect(panel.find('[data-testid="author-card"]').exists()).toBe(false)
      expect(panel.text()).toContain('Cem Anos de Solidão')
      expect(panel.find('[data-testid="review-toggle"]').exists()).toBe(true)
      expect(panel.html()).not.toContain('/library/')
    })

    it('trocar de aba muda o mapa e limpa o país selecionado', async () => {
      const { wrapper, router } = await setup('/u/marina_lima?country=COL')
      expect(wrapper.find('[data-testid="country-panel"]').exists()).toBe(true)
      const tabs = wrapper.findAll('[role="tab"]')
      await tabs[1]!.trigger('click')
      await flushPromises()
      expect(router.currentRoute.value.query).toEqual({ tab: 'origins' })
      expect(wrapper.find('[data-testid="country-panel"]').exists()).toBe(false)
      expect(tabs[1]!.attributes('aria-selected')).toBe('true')
    })

    it('perfil sem livros lidos: mapa em branco com mensagem', async () => {
      profilesApi.map.mockImplementation(async (_h: string, type: MapType) => ({
        ...mapFor(type),
        totalReadBooks: 0,
        exploredCountries: 0,
        countries: [],
      }))
      const { wrapper } = await setup('/u/ana_vazia')
      expect(wrapper.text()).toContain('Nenhum livro lido ainda')
      expect(wrapper.text()).toContain('ainda não marcou livros como lidos')
    })

    it('erro ao carregar o mapa mostra aviso com a mensagem central e permite repetir', async () => {
      profilesApi.map.mockRejectedValue(new ApiError({ status: 501, title: 'x' }))
      const { wrapper } = await setup('/u/marina_lima')
      expect(wrapper.text()).toContain('Este recurso ainda não está disponível.')
      expect(wrapper.text()).toContain('Tentar novamente')
    })
  })

  describe('seguidores e seguindo', () => {
    it('só carregam ao abrir a aba; mostram cartões com botão de seguir', async () => {
      const { wrapper } = await setup('/u/marina_lima')
      expect(profilesApi.followers).not.toHaveBeenCalled()

      await wrapper.findAll('[role="tab"]')[2]!.trigger('click')
      await flushPromises()

      expect(profilesApi.followers).toHaveBeenCalledWith('marina_lima', { page: 1, pageSize: 20 })
      const cards = wrapper.findAll('[data-testid="person-card"]')
      expect(cards.map((c) => c.find('.person__handle').text())).toEqual(['@ana', '@bruno'])
      expect(cards[0]!.find('[data-testid="follow-button"]').text()).toBe('Seguir')
      expect(cards[0]!.find('a').attributes('href')).toBe('/u/ana')

      await wrapper.findAll('[role="tab"]')[3]!.trigger('click')
      await flushPromises()
      expect(profilesApi.following).toHaveBeenCalledWith('marina_lima', { page: 1, pageSize: 20 })
      expect(wrapper.findAll('[data-testid="person-card"]')).toHaveLength(1)
    })

    it('abas navegam por setas do teclado (padrão ARIA)', async () => {
      const { wrapper, router } = await setup('/u/marina_lima')
      await wrapper.findAll('[role="tab"]')[0]!.trigger('keydown', { key: 'ArrowRight' })
      await flushPromises()
      expect(router.currentRoute.value.query.tab).toBe('origins')
      await wrapper.findAll('[role="tab"]')[1]!.trigger('keydown', { key: 'End' })
      await flushPromises()
      expect(router.currentRoute.value.query.tab).toBe('following')
    })
  })
})
