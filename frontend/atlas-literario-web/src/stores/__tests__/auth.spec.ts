import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type { AuthResponse } from '@/types/api'
import { ApiError } from '@/api/errors'
import { REFRESH_TOKEN_KEY, tokenManager } from '@/api/tokenManager'
import { useAuthStore } from '../auth'

const authApi = vi.hoisted(() => ({
  login: vi.fn(),
  register: vi.fn(),
  refresh: vi.fn(),
  logout: vi.fn(),
  forgotPassword: vi.fn(),
  resetPassword: vi.fn(),
  me: vi.fn(),
  updateMe: vi.fn(),
  changePassword: vi.fn(),
}))

vi.mock('@/api', () => ({ api: { auth: authApi } }))

function authResponse(overrides: Partial<AuthResponse> = {}): AuthResponse {
  return {
    accessToken: 'access-1',
    accessTokenExpiresAt: '2030-01-01T00:00:00Z',
    refreshToken: 'refresh-1',
    user: {
      id: 'u1',
      name: 'Ana',
      email: 'ana@example.com',
      mapVisibility: 'Private',
      role: 'User',
      createdAt: '2025-01-01T00:00:00Z',
    },
    ...overrides,
  }
}

describe('auth store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    tokenManager.clear()
    localStorage.clear()
    Object.values(authApi).forEach((fn) => fn.mockReset())
  })

  it('login guarda o access token em memória e o refresh token no localStorage', async () => {
    authApi.login.mockResolvedValue(authResponse())
    const store = useAuthStore()

    await store.login({ email: 'ana@example.com', password: 'segredo123' })

    expect(authApi.login).toHaveBeenCalledWith({ email: 'ana@example.com', password: 'segredo123' })
    expect(store.isAuthenticated).toBe(true)
    expect(store.user?.name).toBe('Ana')
    expect(tokenManager.getAccessToken()).toBe('access-1')
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBe('refresh-1')
    expect(JSON.stringify(localStorage)).not.toContain('access-1')
  })

  it('login com credenciais inválidas propaga o ApiError e não autentica', async () => {
    authApi.login.mockRejectedValue(
      new ApiError({ status: 401, title: 'x', code: 'auth.invalid_credentials' }),
    )
    const store = useAuthStore()

    await expect(store.login({ email: 'a@b.co', password: 'errada123' })).rejects.toMatchObject({
      code: 'auth.invalid_credentials',
    })
    expect(store.isAuthenticated).toBe(false)
    expect(tokenManager.getAccessToken()).toBeNull()
  })

  it('logout revoga o refresh token e limpa a sessão', async () => {
    authApi.login.mockResolvedValue(authResponse())
    authApi.logout.mockResolvedValue(undefined)
    const store = useAuthStore()
    await store.login({ email: 'ana@example.com', password: 'segredo123' })

    await store.logout()

    expect(authApi.logout).toHaveBeenCalledWith({ refreshToken: 'refresh-1' })
    expect(store.isAuthenticated).toBe(false)
    expect(tokenManager.getAccessToken()).toBeNull()
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBeNull()
  })

  it('logout limpa a sessão local mesmo se a API falhar', async () => {
    authApi.login.mockResolvedValue(authResponse())
    authApi.logout.mockRejectedValue(new ApiError({ status: 0, title: 'offline' }))
    const store = useAuthStore()
    await store.login({ email: 'ana@example.com', password: 'segredo123' })

    await store.logout()

    expect(store.user).toBeNull()
    expect(tokenManager.getRefreshToken()).toBeNull()
  })

  it('restoreSession usa o refresh token persistido (uma única vez)', async () => {
    localStorage.setItem(REFRESH_TOKEN_KEY, 'stored-refresh')
    authApi.refresh.mockResolvedValue(authResponse({ accessToken: 'access-2' }))
    const store = useAuthStore()

    await Promise.all([store.restoreSession(), store.ensureInitialized()])

    expect(authApi.refresh).toHaveBeenCalledTimes(1)
    expect(authApi.refresh).toHaveBeenCalledWith({ refreshToken: 'stored-refresh' })
    expect(store.isAuthenticated).toBe(true)
    expect(store.initialized).toBe(true)
  })

  it('restoreSession com refresh inválido limpa o token persistido', async () => {
    localStorage.setItem(REFRESH_TOKEN_KEY, 'expired')
    authApi.refresh.mockRejectedValue(new ApiError({ status: 401, title: 'x' }))
    const store = useAuthStore()

    await store.restoreSession()

    expect(store.isAuthenticated).toBe(false)
    expect(store.initialized).toBe(true)
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBeNull()
  })

  it('sem refresh token não chama a API', async () => {
    const store = useAuthStore()
    await store.ensureInitialized()
    expect(authApi.refresh).not.toHaveBeenCalled()
    expect(store.initialized).toBe(true)
  })

  describe('isModerator (papel vindo da sessão)', () => {
    const asModerator = () => authResponse({ user: { ...authResponse().user, role: 'Moderator' } })

    it('false sem sessão e para usuário comum', async () => {
      const store = useAuthStore()
      expect(store.isModerator).toBe(false)
      authApi.login.mockResolvedValue(authResponse())
      await store.login({ email: 'ana@example.com', password: 'segredo123' })
      expect(store.isModerator).toBe(false)
    })

    it('true no login de moderador e false de novo após o logout', async () => {
      authApi.login.mockResolvedValue(asModerator())
      authApi.logout.mockResolvedValue(undefined)
      const store = useAuthStore()
      await store.login({ email: 'm@example.com', password: 'segredo123' })
      expect(store.isModerator).toBe(true)
      await store.logout()
      expect(store.isModerator).toBe(false)
    })

    it('a restauração da sessão (refresh) atualiza o papel', async () => {
      localStorage.setItem(REFRESH_TOKEN_KEY, 'refresh-1')
      authApi.refresh.mockResolvedValue(asModerator())
      const store = useAuthStore()
      await store.restoreSession()
      expect(store.isModerator).toBe(true)
    })

    it('uma renovação de sessão feita pelo cliente HTTP também atualiza o papel (rebaixamento incluso)', async () => {
      authApi.login.mockResolvedValue(asModerator())
      const store = useAuthStore()
      await store.login({ email: 'm@example.com', password: 'segredo123' })
      expect(store.isModerator).toBe(true)
      tokenManager.applyAuthResponse(authResponse()) // refresh automático devolveu papel User
      expect(store.isModerator).toBe(false)
    })
  })
})
