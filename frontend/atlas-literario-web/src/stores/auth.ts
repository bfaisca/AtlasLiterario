import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { api } from '@/api'
import { tokenManager } from '@/api/tokenManager'
import type {
  AuthResponse,
  ForgotPasswordRequest,
  LoginRequest,
  RegisterRequest,
  ResetPasswordRequest,
  UpdateProfileRequest,
  UserProfileDto,
} from '@/types/api'

export const useAuthStore = defineStore('auth', () => {
  const user = ref<UserProfileDto | null>(null)
  const initialized = ref(false)
  let restorePromise: Promise<void> | null = null

  // `user` só é preenchido junto com os tokens (applySession) e limpo junto com eles (clearSession).
  const isAuthenticated = computed(() => user.value !== null)
  /** Papel vindo do AuthResponse (login, registro, refresh/restauração) e de `/auth/me`. */
  const isModerator = computed(() => user.value?.role === 'Moderator')

  function applySession(response: AuthResponse) {
    tokenManager.applyAuthResponse(response)
    user.value = response.user
  }

  // Refresh feito pelo cliente HTTP também atualiza o usuário.
  tokenManager.onSessionUpdated((response) => {
    user.value = response.user
  })

  async function login(request: LoginRequest) {
    applySession(await api.auth.login(request))
  }

  async function register(request: RegisterRequest) {
    applySession(await api.auth.register(request))
  }

  /** Revoga o refresh token no servidor (melhor esforço) e limpa a sessão local. */
  async function logout() {
    const refreshToken = tokenManager.getRefreshToken()
    try {
      if (refreshToken) await api.auth.logout({ refreshToken })
    } catch {
      // mesmo se a API falhar, a sessão local é encerrada
    } finally {
      clearSession()
    }
  }

  /** Limpa o estado local (usado também quando a sessão expira). */
  function clearSession() {
    tokenManager.clear()
    user.value = null
  }

  /** Restaura a sessão a partir do refresh token persistido. Executa uma única vez. */
  function restoreSession(): Promise<void> {
    restorePromise ??= (async () => {
      const refreshToken = tokenManager.getRefreshToken()
      if (!refreshToken) return
      try {
        applySession(await api.auth.refresh({ refreshToken }))
      } catch {
        clearSession()
      }
    })().finally(() => {
      initialized.value = true
    })
    return restorePromise
  }

  async function ensureInitialized() {
    if (!initialized.value) await restoreSession()
  }

  async function updateProfile(request: UpdateProfileRequest) {
    user.value = await api.auth.updateMe(request)
  }

  async function requestPasswordReset(request: ForgotPasswordRequest) {
    await api.auth.forgotPassword(request)
  }

  async function resetPassword(request: ResetPasswordRequest) {
    await api.auth.resetPassword(request)
  }

  return {
    user,
    initialized,
    isAuthenticated,
    isModerator,
    login,
    register,
    logout,
    clearSession,
    restoreSession,
    ensureInitialized,
    updateProfile,
    requestPasswordReset,
    resetPassword,
  }
})
