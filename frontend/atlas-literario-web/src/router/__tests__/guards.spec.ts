import { describe, expect, it } from 'vitest'
import { defineComponent } from 'vue'
import { createMemoryHistory, createRouter, type RouteRecordRaw } from 'vue-router'
import { createAuthGuard, resolveNavigation, safeRedirect, type SessionLike } from '../guards'

const Stub = defineComponent({ render: () => null })

const routes: RouteRecordRaw[] = [
  {
    path: '/',
    component: Stub,
    meta: { requiresAuth: true },
    children: [
      { path: '', name: 'dashboard', component: Stub },
      { path: 'library', name: 'library', component: Stub },
    ],
  },
  { path: '/login', name: 'login', component: Stub, meta: { guestOnly: true } },
  { path: '/share/:token', name: 'shared-map', component: Stub },
]

function setup(session: Partial<SessionLike> & { isAuthenticated: boolean }) {
  let initCalls = 0
  const fullSession: SessionLike = {
    isAuthenticated: session.isAuthenticated,
    ensureInitialized: async () => {
      initCalls++
    },
  }
  const router = createRouter({ history: createMemoryHistory(), routes })
  router.beforeEach(createAuthGuard(() => fullSession))
  return { router, initCalls: () => initCalls }
}

describe('resolveNavigation', () => {
  it('redireciona visitante de rota protegida para o login com redirect', () => {
    expect(
      resolveNavigation({ meta: { requiresAuth: true }, fullPath: '/library?x=1' }, false),
    ).toEqual({
      name: 'login',
      query: { redirect: '/library?x=1' },
    })
  })

  it('libera rota protegida para usuário autenticado', () => {
    expect(resolveNavigation({ meta: { requiresAuth: true }, fullPath: '/library' }, true)).toBe(
      true,
    )
  })

  it('envia usuário autenticado de rotas guestOnly para o dashboard', () => {
    expect(resolveNavigation({ meta: { guestOnly: true }, fullPath: '/login' }, true)).toEqual({
      name: 'dashboard',
    })
  })
})

describe('auth guard no router', () => {
  it('meta.requiresAuth do pai protege as rotas filhas', async () => {
    const { router, initCalls } = setup({ isAuthenticated: false })
    await router.push('/library')
    expect(router.currentRoute.value.name).toBe('login')
    expect(router.currentRoute.value.query.redirect).toBe('/library')
    expect(initCalls()).toBeGreaterThan(0)
  })

  it('rota pública de compartilhamento não exige login', async () => {
    const { router } = setup({ isAuthenticated: false })
    await router.push('/share/abc')
    expect(router.currentRoute.value.name).toBe('shared-map')
  })

  it('usuário autenticado não vê o login', async () => {
    const { router } = setup({ isAuthenticated: true })
    await router.push('/login')
    expect(router.currentRoute.value.name).toBe('dashboard')
  })
})

describe('safeRedirect', () => {
  it('aceita apenas caminhos internos', () => {
    expect(safeRedirect('/library')).toBe('/library')
    expect(safeRedirect('//evil.com')).toBeNull()
    expect(safeRedirect('https://evil.com')).toBeNull()
    expect(safeRedirect(['/x'])).toBeNull()
  })
})
