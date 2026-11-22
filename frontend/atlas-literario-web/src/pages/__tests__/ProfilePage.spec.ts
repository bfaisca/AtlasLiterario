import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { defineComponent } from 'vue'
import { ApiError } from '@/api/errors'
import { useAuthStore } from '@/stores/auth'
import type { MapVisibility, UserProfileDto, UserRole } from '@/types/api'
import ProfilePage from '../ProfilePage.vue'

const authApi = vi.hoisted(() => ({ me: vi.fn(), updateMe: vi.fn() }))
vi.mock('@/api', () => ({ api: { auth: authApi } }))

const Stub = defineComponent({ render: () => null })

function user(overrides: Partial<UserProfileDto> = {}): UserProfileDto {
  return {
    id: 'u',
    name: 'Ana',
    email: 'ana@x.com',
    mapVisibility: 'Private',
    role: 'User',
    createdAt: '2025-01-01T00:00:00Z',
    handle: null,
    followersCount: 3,
    followingCount: 2,
    ...overrides,
  }
}

function mountWith(profile: UserProfileDto) {
  setActivePinia(createPinia())
  useAuthStore().user = profile
  authApi.me.mockResolvedValue(profile)
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: Stub },
      { path: '/me/followers', name: 'me-followers', component: Stub },
      { path: '/me/following', name: 'me-following', component: Stub },
      { path: '/pessoas', name: 'people', component: Stub },
      { path: '/u/:handle', name: 'user-profile', component: Stub },
      { path: '/login', name: 'login', component: Stub },
    ],
  })
  void router.push('/')
  return mount(ProfilePage, { global: { plugins: [router] } })
}

const mountAs = (role: UserRole) => mountWith(user({ role }))

async function choose(wrapper: ReturnType<typeof mountWith>, value: MapVisibility) {
  await wrapper.find('[data-testid="visibility-select"]').setValue(value)
}

describe('ProfilePage: papel', () => {
  beforeEach(() => vi.clearAllMocks())

  it('moderador vê o selo "Moderador"', () => {
    expect(mountAs('Moderator').find('h1').text()).toContain('Moderador')
  })
  it('usuário comum não vê selo de papel', () => {
    const wrapper = mountAs('User')
    expect(wrapper.find('h1').text()).toBe('Ana')
    expect(wrapper.text()).not.toContain('Moderador')
  })
})

describe('ProfilePage: apelido e visibilidade pública', () => {
  beforeEach(() => vi.clearAllMocks())

  it('Public mostra aviso claro de que mapas, títulos e resenhas ficam visíveis', async () => {
    const wrapper = mountWith(user())
    expect(wrapper.find('[data-testid="public-warning"]').exists()).toBe(false)

    await choose(wrapper, 'Public')

    const warning = wrapper.find('[data-testid="public-warning"]')
    expect(warning.exists()).toBe(true)
    expect(warning.text()).toContain('Qualquer usuário autenticado')
    expect(warning.text()).toContain('títulos')
    expect(warning.text()).toContain('resenhas')
    expect(warning.text()).toContain('sem precisar seguir')

    await choose(wrapper, 'LinkOnly')
    expect(wrapper.find('[data-testid="public-warning"]').exists()).toBe(false)
  })

  it('Public exige apelido: não chama a API e explica o motivo', async () => {
    const wrapper = mountWith(user())
    await choose(wrapper, 'Public')

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(authApi.updateMe).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('Para deixar o perfil público, escolha um apelido.')
  })

  it('valida o formato do apelido no cliente (3–30, a–z 0–9 _)', async () => {
    const wrapper = mountWith(user())
    for (const bad of [
      'ab',
      'tem espaço',
      'acentuação',
      'x'.repeat(31).slice(0, 30) + '!',
      'admin',
    ]) {
      await wrapper.find('[data-testid="handle-input"]').setValue(bad)
      await wrapper.find('form').trigger('submit')
      await flushPromises()
      expect(authApi.updateMe).not.toHaveBeenCalled()
      expect(wrapper.find('.field__error').exists(), bad).toBe(true)
    }
  })

  it('digitar maiúsculas vira minúsculas e o envio leva o apelido normalizado', async () => {
    authApi.updateMe.mockResolvedValue(user({ handle: 'marina_lima', mapVisibility: 'Public' }))
    const wrapper = mountWith(user())
    const input = wrapper.find('[data-testid="handle-input"]')

    await input.setValue('Marina_Lima')
    expect((input.element as HTMLInputElement).value).toBe('marina_lima')
    await choose(wrapper, 'Public')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(authApi.updateMe).toHaveBeenCalledWith({
      name: 'Ana',
      mapVisibility: 'Public',
      handle: 'marina_lima',
    })
    expect(useAuthStore().user?.handle).toBe('marina_lima')
  })

  it.each([
    ['profile.handle_taken', 409, 'Este apelido já está em uso'],
    ['profile.handle_invalid', 400, 'Apelido inválido'],
    ['profile.handle_required', 400, 'escolha um apelido'],
  ])('erro %s da API aparece no campo do apelido', async (code, status, text) => {
    authApi.updateMe.mockRejectedValue(new ApiError({ status, title: 'x', code }))
    const wrapper = mountWith(user())
    await wrapper.find('[data-testid="handle-input"]').setValue('marina_lima')

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.find('.field__error').text()).toContain(text)
  })

  it('apelido vazio fora do modo público envia handle null (remove)', async () => {
    authApi.updateMe.mockResolvedValue(user({ handle: null }))
    const wrapper = mountWith(user({ handle: 'antigo_apelido' }))
    expect((wrapper.find('[data-testid="handle-input"]').element as HTMLInputElement).value).toBe(
      'antigo_apelido',
    )

    await wrapper.find('[data-testid="handle-input"]').setValue('')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(authApi.updateMe).toHaveBeenCalledWith({
      name: 'Ana',
      mapVisibility: 'Private',
      handle: null,
    })
  })

  it('a releitura de /auth/me (contagens) não apaga o que a pessoa está digitando', async () => {
    const wrapper = mountWith(user())
    const input = wrapper.find('[data-testid="handle-input"]')
    await input.setValue('estou_digitando')
    await choose(wrapper, 'Public')

    // /auth/me responde depois, com as contagens novas e os mesmos campos editáveis de antes
    useAuthStore().user = user({ followersCount: 99 })
    await flushPromises()

    expect((input.element as HTMLInputElement).value).toBe('estou_digitando')
    expect(wrapper.find('[data-testid="public-warning"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="link-followers"]').text()).toContain('99 seguidores')
  })

  it('mostra contagens de seguidores/seguindo com links para as listas', async () => {
    const wrapper = mountWith(user({ followersCount: 7, followingCount: 4 }))
    await flushPromises()

    const followers = wrapper.find('[data-testid="link-followers"]')
    const following = wrapper.find('[data-testid="link-following"]')
    expect(followers.text()).toContain('7 seguidores')
    expect(followers.attributes('href')).toBe('/me/followers')
    expect(following.text()).toContain('4 seguindo')
    expect(following.attributes('href')).toBe('/me/following')
    expect(authApi.me).toHaveBeenCalled()
  })
})
