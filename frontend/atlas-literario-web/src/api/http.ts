import axios, { type AxiosInstance, type InternalAxiosRequestConfig } from 'axios'
import type { AuthResponse } from '@/types/api'
import { toApiError } from './errors'
import { tokenManager } from './tokenManager'

declare module 'axios' {
  interface AxiosRequestConfig {
    /** Não tentar refresh automático em 401 para esta requisição. */
    skipAuthRefresh?: boolean
  }
}

interface RetriableRequestConfig extends InternalAxiosRequestConfig {
  _retried?: boolean
}

import { DEFAULT_TIMEOUT_MS } from './timeouts'

export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || '/api'

/** Endpoints em que um 401 é resposta de negócio (credenciais), não token expirado. */
const NO_REFRESH_PATHS = [
  '/auth/login',
  '/auth/register',
  '/auth/refresh',
  '/auth/forgot-password',
  '/auth/reset-password',
]

function isNoRefreshPath(url: string | undefined): boolean {
  return !!url && NO_REFRESH_PATHS.some((path) => url.endsWith(path))
}

/** Chamada de refresh sem interceptors (evita recursão). */
async function requestNewTokens(baseURL: string): Promise<string> {
  const refreshToken = tokenManager.getRefreshToken()
  if (!refreshToken) throw new Error('Sem refresh token')
  const { data } = await axios.post<AuthResponse>(`${baseURL}/auth/refresh`, { refreshToken })
  tokenManager.applyAuthResponse(data)
  return data.accessToken
}

export function createHttpClient(baseURL: string = API_BASE_URL): AxiosInstance {
  const http = axios.create({
    baseURL,
    timeout: DEFAULT_TIMEOUT_MS,
    headers: { Accept: 'application/json' },
    // status=Read&status=Reading (sem colchetes), como o ASP.NET Core espera
    paramsSerializer: { indexes: null },
  })

  http.interceptors.request.use((config) => {
    const token = tokenManager.getAccessToken()
    if (token && !config.headers.has('Authorization')) {
      config.headers.set('Authorization', `Bearer ${token}`)
    }
    return config
  })

  // Um único refresh em andamento; requisições concorrentes com 401 aguardam a mesma promessa (fila).
  let refreshInFlight: Promise<string> | null = null

  http.interceptors.response.use(
    (response) => response,
    async (error: unknown) => {
      if (!axios.isAxiosError(error)) throw toApiError(error)

      const config = error.config as RetriableRequestConfig | undefined
      const canRefresh =
        error.response?.status === 401 &&
        !!config &&
        !config._retried &&
        !config.skipAuthRefresh &&
        !isNoRefreshPath(config.url) &&
        !!tokenManager.getRefreshToken()

      if (!canRefresh || !config) throw toApiError(error)

      // Uma única nova tentativa por requisição.
      config._retried = true

      // O token pode já ter sido renovado por outra requisição enquanto esta estava em voo.
      const current = tokenManager.getAccessToken()
      const used = config.headers.get('Authorization')
      if (current && used !== `Bearer ${current}`) {
        config.headers.set('Authorization', `Bearer ${current}`)
        return http(config)
      }

      try {
        refreshInFlight ??= requestNewTokens(baseURL).finally(() => {
          refreshInFlight = null
        })
        const newToken = await refreshInFlight
        config.headers.set('Authorization', `Bearer ${newToken}`)
        return http(config)
      } catch {
        tokenManager.expireSession()
        throw toApiError(error)
      }
    },
  )

  return http
}

export const http = createHttpClient()
