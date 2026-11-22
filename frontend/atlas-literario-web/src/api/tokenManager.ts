import type { AuthResponse } from '@/types/api'

/**
 * Guarda os tokens da sessão.
 * - Access token: somente em memória (não persiste; reduz exposição a XSS persistente).
 * - Refresh token: localStorage (para restaurar a sessão), sempre com try/catch
 *   (modo privado / storage bloqueado não quebra a aplicação).
 *
 * Módulo sem dependência de Pinia/Vue para ser usado pelo cliente HTTP sem ciclos.
 */
export const REFRESH_TOKEN_KEY = 'atlas.refreshToken'

type Listener<T> = (payload: T) => void

let accessToken: string | null = null
let accessTokenExpiresAt: string | null = null
let memoryRefreshToken: string | null = null

const updatedListeners = new Set<Listener<AuthResponse>>()
const expiredListeners = new Set<Listener<void>>()

function readStorage(): string | null {
  try {
    return globalThis.localStorage?.getItem(REFRESH_TOKEN_KEY) ?? null
  } catch {
    return null
  }
}

function writeStorage(value: string | null): void {
  try {
    if (value) globalThis.localStorage?.setItem(REFRESH_TOKEN_KEY, value)
    else globalThis.localStorage?.removeItem(REFRESH_TOKEN_KEY)
  } catch {
    // storage indisponível: mantém apenas em memória
  }
}

export const tokenManager = {
  getAccessToken(): string | null {
    return accessToken
  },

  getAccessTokenExpiresAt(): string | null {
    return accessTokenExpiresAt
  },

  getRefreshToken(): string | null {
    return memoryRefreshToken ?? readStorage()
  },

  /** Aplica uma nova sessão (login, cadastro ou refresh) e notifica os ouvintes. */
  applyAuthResponse(response: AuthResponse): void {
    accessToken = response.accessToken
    accessTokenExpiresAt = response.accessTokenExpiresAt
    memoryRefreshToken = response.refreshToken
    writeStorage(response.refreshToken)
    updatedListeners.forEach((listener) => listener(response))
  },

  /** Limpa a sessão local sem notificar (logout explícito). */
  clear(): void {
    accessToken = null
    accessTokenExpiresAt = null
    memoryRefreshToken = null
    writeStorage(null)
  },

  /** Sessão perdida (refresh falhou): limpa e notifica para redirecionar ao login. */
  expireSession(): void {
    this.clear()
    expiredListeners.forEach((listener) => listener())
  },

  onSessionUpdated(listener: Listener<AuthResponse>): () => void {
    updatedListeners.add(listener)
    return () => updatedListeners.delete(listener)
  },

  onSessionExpired(listener: Listener<void>): () => void {
    expiredListeners.add(listener)
    return () => expiredListeners.delete(listener)
  },
}
