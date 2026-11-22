import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { defineComponent } from 'vue'
import { ApiError } from '@/api/errors'
import { useAuthStore } from '@/stores/auth'
import type { ProfileSummaryDto } from '@/types/api'
import PeoplePage from '../PeoplePage.vue'

const profilesApi = vi.hoisted(() => ({ search: vi.fn(), follow: vi.fn(), unfollow: vi.fn() }))
vi.mock('@/api', () => ({ api: { profiles: profilesApi } }))

const Stub = defineComponent({ render: () => null })

const person = (handle: string, extra: Partial<ProfileSummaryDto> = {}): ProfileSummaryDto => ({
  handle,
  name: handle.replace('_', ' '),
  followersCount: 4,
  readBooksCount: 12,
  isFollowing: false,
  followsYou: false,
  ...extra,
})

function setup() {
  const pinia = createPinia()
  setActivePinia(pinia)
  useAuthStore().user = {
    id: 'me',
    name: 'Eu',
    email: 'e@x.com',
    mapVisibility: 'Private',
    role: 'User',
    createdAt: '2025-01-01T00:00:00Z',
    handle: 'eu_mesmo',
    followersCount: 0,
    followingCount: 0,
  }
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: Stub },
      { path: '/u/:handle', name: 'user-profile', component: Stub },
      { path: '/me/followers', name: 'me-followers', component: Stub },
      { path: '/me/following', name: 'me-following', component: Stub },
    ],
  })
  void router.push('/')
  return mount(PeoplePage, { global: { plugins: [router, pinia] } })
}

const type = (wrapper: ReturnType<typeof setup>, text: string) =>
  wrapper.find('[data-testid="people-search"]').setValue(text)

describe('PeoplePage (busca de pessoas)', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.clearAllMocks()
  })
  afterEach(() => vi.useRealTimers())

  it('antes de digitar orienta o uso e não chama a API', async () => {
    const wrapper = setup()
    expect(wrapper.text()).toContain('Comece a digitar')
    await type(wrapper, 'm')
    await vi.advanceTimersByTimeAsync(1000)
    expect(profilesApi.search).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('Comece a digitar')
  })

  it('busca após 250 ms de pausa e mostra cartões com nome, @apelido, seguidores e livros lidos', async () => {
    profilesApi.search.mockResolvedValue([person('marina_lima', { name: 'Marina Lima' })])
    const wrapper = setup()
    await type(wrapper, 'mar')
    await vi.advanceTimersByTimeAsync(200)
    expect(profilesApi.search).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(60)
    await flushPromises()

    expect(profilesApi.search).toHaveBeenCalledWith('mar', 10, expect.any(AbortSignal))
    const card = wrapper.find('[data-testid="person-card"]')
    expect(card.text()).toContain('Marina Lima')
    expect(card.text()).toContain('@marina_lima')
    expect(card.text()).toContain('4 seguidores')
    expect(card.text()).toContain('12 livros lidos')
    expect(card.find('a').attributes('href')).toBe('/u/marina_lima')
  })

  it('rótulo do botão: Seguir, Seguindo e Seguir de volta (+ selo "Segue você")', async () => {
    profilesApi.search.mockResolvedValue([
      person('nova'),
      person('amiga', { isFollowing: true }),
      person('de_volta', { followsYou: true }),
    ])
    const wrapper = setup()
    await type(wrapper, 'am')
    await vi.advanceTimersByTimeAsync(260)
    await flushPromises()

    const cards = wrapper.findAll('[data-testid="person-card"]')
    const labels = cards.map((c) => c.find('[data-testid="follow-button"]').text())
    expect(labels).toEqual(['Seguir', 'Seguindo', 'Seguir de volta'])
    expect(cards[2]!.text()).toContain('Segue você')
    expect(cards[0]!.text()).not.toContain('Segue você')
  })

  it('clicar em Seguir muda para Seguindo imediatamente e chama a API', async () => {
    profilesApi.search.mockResolvedValue([person('nova')])
    profilesApi.follow.mockResolvedValue(undefined)
    const wrapper = setup()
    await type(wrapper, 'no')
    await vi.advanceTimersByTimeAsync(260)
    await flushPromises()

    const button = wrapper.find('[data-testid="follow-button"]')
    await button.trigger('click')
    expect(button.text()).toBe('Seguindo')
    await flushPromises()
    expect(profilesApi.follow).toHaveBeenCalledWith('nova')
    expect(wrapper.find('[data-testid="person-card"]').text()).toContain('5 seguidores')
  })

  it('"sem resultados" explica que perfis privados não aparecem', async () => {
    profilesApi.search.mockResolvedValue([])
    const wrapper = setup()
    await type(wrapper, 'zzz')
    await vi.advanceTimersByTimeAsync(260)
    await flushPromises()
    expect(wrapper.text()).toContain('Ninguém encontrado')
    expect(wrapper.text()).toContain('Perfis privados não aparecem')
  })

  it('erro da busca mostra a mensagem da camada central', async () => {
    profilesApi.search.mockRejectedValue(new ApiError({ status: 429, title: 'x' }))
    const wrapper = setup()
    await type(wrapper, 'mar')
    await vi.advanceTimersByTimeAsync(260)
    await flushPromises()
    expect(wrapper.find('[role="alert"]').text()).toContain('Muitas tentativas')
  })

  it('o próprio usuário em uma lista não tem botão de seguir', async () => {
    profilesApi.search.mockResolvedValue([person('eu_mesmo'), person('outra')])
    const wrapper = setup()
    await type(wrapper, 'eu')
    await vi.advanceTimersByTimeAsync(260)
    await flushPromises()
    const cards = wrapper.findAll('[data-testid="person-card"]')
    expect(cards[0]!.find('[data-testid="follow-button"]').exists()).toBe(false)
    expect(cards[1]!.find('[data-testid="follow-button"]').exists()).toBe(true)
  })
})
