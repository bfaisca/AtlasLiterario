import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { api } from '@/api'
import { ApiError } from '@/api/errors'
import { tokenManager } from '@/api/tokenManager'
import { useLibraryStore } from './library'
import { useMapsStore } from './maps'
import { useProfileMapsStore } from './profileMaps'
import { useProfilesStore } from './profiles'
import { useStatisticsStore } from './statistics'
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
  /** Revisão do perfil em memória: respostas de `/auth/me` iniciadas antes de uma alteração são descartadas. */
  let userRevision = 0

  // `user` só é preenchido junto com os tokens (applySession) e limpo junto com eles (clearSession).
  const isAuthenticated = computed(() => user.value !== null)
  /** Papel vindo do AuthResponse (login, registro, refresh/restauração) e de `/auth/me`. */
  const isModerator = computed(() => user.value?.role === 'Moderator')

  /**
   * Zera todo estado em memória por usuário (biblioteca, mapas, estatísticas, perfis/seguidores). Importar as stores
   * aqui não cria ciclo: nenhuma delas depende de `auth` (o cliente HTTP só usa `tokenManager`).
   */
  function resetUserData() {
    useLibraryStore().reset()
    useMapsStore().reset()
    useStatisticsStore().reset()
    useProfilesStore().reset()
    useProfileMapsStore().reset()
  }

  /** Define o usuário; zera os dados se não havia usuário ou se o id mudou (não no refresh). */
  function setUser(next: UserProfileDto) {
    if (user.value?.id !== next.id) resetUserData()
    userRevision++
    user.value = next
  }

  function applySession(response: AuthResponse) {
    // `applyAuthResponse` notifica `onSessionUpdated` (-> setUser); o reset ocorre antes de o
    // novo usuário ser publicado, comparando com o usuário anterior.
    tokenManager.applyAuthResponse(response)
    setUser(response.user)
  }

  // Refresh feito pelo cliente HTTP também atualiza o usuário (mesmo id: sem reset).
  tokenManager.onSessionUpdated((response) => setUser(response.user))

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
    resetUserData()
  }

  /** Restaura a sessão a partir do refresh token persistido. Executa uma única vez. */
  function restoreSession(): Promise<void> {
    restorePromise ??= (async () => {
      const refreshToken = tokenManager.getRefreshToken()
      if (!refreshToken) return
      try {
        applySession(await api.auth.refresh({ refreshToken }))
      } catch (error) {
        // Só encerra a sessão se o servidor rejeitou o token. Falha de rede, timeout ou 5xx (API acordando) mantêm o
        // refresh token: apagá-lo faria o usuário perder o login por um problema passageiro.
        const rejected = error instanceof ApiError && [400, 401, 403].includes(error.status)
        if (rejected) clearSession()
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
    userRevision++ // uma releitura de `/auth/me` em voo ficou obsoleta
    const updated = await api.auth.updateMe(request)
    // Sessão encerrada/trocada durante a requisição: a resposta não pertence mais a quem está logado.
    if (user.value?.id === updated.id) {
      userRevision++
      user.value = updated
    }
  }

  /** Recarrega `/auth/me` (contagens de seguidores/seguindo mudam fora desta tela). */
  async function refreshUser() {
    const current = user.value
    if (!current) return
    const revision = userRevision
    const fresh = await api.auth.me()
    // Descarta se a sessão trocou ou se o perfil foi alterado (salvo) enquanto a requisição corria.
    if (revision === userRevision && user.value?.id === current.id && fresh.id === current.id) {
      user.value = fresh
    }
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
    refreshUser,
    requestPasswordReset,
    resetPassword,
  }
})
