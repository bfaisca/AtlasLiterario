import type { NavigationGuard, RouteLocationNormalized, RouteLocationRaw } from 'vue-router'

declare module 'vue-router' {
  interface RouteMeta {
    /** Exige usuário autenticado (herdado pelas rotas filhas). */
    requiresAuth?: boolean
    /** Somente para visitantes (login, cadastro...). Autenticado → dashboard. */
    guestOnly?: boolean
    /** Título da aba. */
    title?: string
  }
}

export interface SessionLike {
  readonly isAuthenticated: boolean
  ensureInitialized(): Promise<void>
}

/** Decisão pura de navegação (testável sem router). */
export function resolveNavigation(
  to: Pick<RouteLocationNormalized, 'meta' | 'fullPath'>,
  isAuthenticated: boolean,
): true | RouteLocationRaw {
  if (to.meta.requiresAuth && !isAuthenticated) {
    return { name: 'login', query: to.fullPath !== '/' ? { redirect: to.fullPath } : {} }
  }
  if (to.meta.guestOnly && isAuthenticated) {
    return { name: 'dashboard' }
  }
  return true
}

/** Aceita apenas redirecionamentos internos (evita open redirect via ?redirect=). */
export function safeRedirect(value: unknown): string | null {
  if (typeof value !== 'string') return null
  return value.startsWith('/') && !value.startsWith('//') ? value : null
}

export function createAuthGuard(getSession: () => SessionLike): NavigationGuard {
  return async (to) => {
    const session = getSession()
    await session.ensureInitialized()
    return resolveNavigation(to, session.isAuthenticated)
  }
}
