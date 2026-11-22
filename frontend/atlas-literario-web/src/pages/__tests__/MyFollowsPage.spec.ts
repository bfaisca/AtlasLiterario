import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { defineComponent } from 'vue'
import { ApiError } from '@/api/errors'
import type { PagedResult, ProfileSummaryDto } from '@/types/api'
import type { PeopleListKind } from '@/stores/profiles'
import MyFollowsPage from '../MyFollowsPage.vue'

const profilesApi = vi.hoisted(() => ({
  meFollowers: vi.fn(),
  meFollowing: vi.fn(),
  follow: vi.fn(),
  unfollow: vi.fn(),
}))
vi.mock('@/api', () => ({ api: { profiles: profilesApi } }))

const Stub = defineComponent({ render: () => null })
const person = (handle: string, extra: Partial<ProfileSummaryDto> = {}): ProfileSummaryDto => ({
  handle,
  name: handle,
  followersCount: 1,
  readBooksCount: 1,
  isFollowing: false,
  followsYou: false,
  ...extra,
})
const page = (
  items: ProfileSummaryDto[],
  pageNumber = 1,
  totalPages = 1,
): PagedResult<ProfileSummaryDto> => ({
  items,
  page: pageNumber,
  pageSize: 20,
  totalCount: items.length,
  totalPages,
})

function setup(kind: PeopleListKind) {
  const pinia = createPinia()
  setActivePinia(pinia)
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: Stub },
      { path: '/profile', name: 'profile', component: Stub },
      { path: '/pessoas', name: 'people', component: Stub },
      { path: '/u/:handle', name: 'user-profile', component: Stub },
    ],
  })
  void router.push('/')
  return mount(MyFollowsPage, { props: { kind }, global: { plugins: [router, pinia] } })
}

describe('MyFollowsPage (/me/followers e /me/following)', () => {
  beforeEach(() => vi.clearAllMocks())

  it('seguidores: lista, botão Seguir de volta e deixar de seguir com rollback no erro', async () => {
    profilesApi.meFollowers.mockResolvedValue(
      page([person('ana', { followsYou: true }), person('bruno', { isFollowing: true })]),
    )
    profilesApi.follow.mockResolvedValue(undefined)
    let failUnfollow!: (error: unknown) => void
    profilesApi.unfollow.mockReturnValue(new Promise((_, rej) => (failUnfollow = rej)))
    const wrapper = setup('followers')
    await flushPromises()

    expect(wrapper.find('h1').text()).toBe('Seguidores')
    expect(profilesApi.meFollowers).toHaveBeenCalledWith({ page: 1, pageSize: 20 })
    const buttons = wrapper.findAll('[data-testid="follow-button"]')
    expect(buttons.map((b) => b.text())).toEqual(['Seguir de volta', 'Seguindo'])

    await buttons[0]!.trigger('click')
    await flushPromises()
    expect(profilesApi.follow).toHaveBeenCalledWith('ana')
    expect(buttons[0]!.text()).toBe('Seguindo')

    await buttons[1]!.trigger('click')
    expect(buttons[1]!.text()).toBe('Seguir') // otimista
    failUnfollow(new ApiError({ status: 500, title: 'x' }))
    await flushPromises()
    expect(buttons[1]!.text()).toBe('Seguindo') // rollback: a API falhou
  })

  it('seguindo: deixar de seguir mantém a pessoa na lista (dá para seguir de novo)', async () => {
    profilesApi.meFollowing.mockResolvedValue(page([person('clara', { isFollowing: true })]))
    profilesApi.unfollow.mockResolvedValue(undefined)
    const wrapper = setup('following')
    await flushPromises()

    expect(wrapper.find('h1').text()).toBe('Seguindo')
    expect(profilesApi.meFollowing).toHaveBeenCalled()
    await wrapper.find('[data-testid="follow-button"]').trigger('click')
    await flushPromises()

    expect(profilesApi.unfollow).toHaveBeenCalledWith('clara')
    expect(wrapper.findAll('[data-testid="person-card"]')).toHaveLength(1)
    expect(wrapper.find('[data-testid="follow-button"]').text()).toBe('Seguir')
  })

  it('paginação: "Carregar mais" pede a próxima página', async () => {
    profilesApi.meFollowers.mockResolvedValueOnce(page([person('a')], 1, 2))
    profilesApi.meFollowers.mockResolvedValueOnce(page([person('b')], 2, 2))
    const wrapper = setup('followers')
    await flushPromises()
    const more = wrapper.findAll('button').find((b) => b.text() === 'Carregar mais')!
    await more.trigger('click')
    await flushPromises()
    expect(profilesApi.meFollowers).toHaveBeenLastCalledWith({ page: 2, pageSize: 20 })
    expect(wrapper.findAll('[data-testid="person-card"]')).toHaveLength(2)
    expect(wrapper.text()).not.toContain('Carregar mais')
  })

  it('vazio e erro têm mensagens próprias', async () => {
    profilesApi.meFollowers.mockResolvedValue(page([]))
    const empty = setup('followers')
    await flushPromises()
    expect(empty.text()).toContain('Ainda não há seguidores')

    profilesApi.meFollowing.mockRejectedValue(new ApiError({ status: 501, title: 'x' }))
    const failed = setup('following')
    await flushPromises()
    expect(failed.find('[role="alert"]').text()).toContain('ainda não está disponível')
  })
})
