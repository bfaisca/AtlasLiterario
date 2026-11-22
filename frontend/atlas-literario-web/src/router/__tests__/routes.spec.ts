import { describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import { createAuthGuard } from '../guards'
import { routes } from '../routes'

function setup(isAuthenticated: boolean) {
  const router = createRouter({ history: createMemoryHistory(), routes })
  router.beforeEach(createAuthGuard(() => ({ isAuthenticated, ensureInitialized: async () => {} })))
  return router
}

describe('rotas de perfis e seguidores (v1.12)', () => {
  it.each([
    ['/pessoas', 'people'],
    ['/u/marina_lima', 'user-profile'],
    ['/me/followers', 'me-followers'],
    ['/me/following', 'me-following'],
  ])('%s existe, exige autenticação e se chama %s', (path, name) => {
    const resolved = createRouter({ history: createMemoryHistory(), routes }).resolve(path)
    expect(resolved.name).toBe(name)
    expect(resolved.matched.some((record) => record.meta.requiresAuth)).toBe(true)
  })

  it('/u/:handle entrega o apelido como prop e /me/* entregam o tipo da lista', () => {
    const router = createRouter({ history: createMemoryHistory(), routes })
    const profile = router.resolve('/u/marina_lima').matched.at(-1)!
    expect(profile.props.default).toBe(true)
    expect(router.resolve('/u/marina_lima').params.handle).toBe('marina_lima')
    expect(router.resolve('/me/followers').matched.at(-1)!.props.default).toEqual({
      kind: 'followers',
    })
    expect(router.resolve('/me/following').matched.at(-1)!.props.default).toEqual({
      kind: 'following',
    })
  })

  it.each(['/pessoas', '/u/marina_lima', '/me/followers', '/me/following'])(
    'visitante em %s é enviado ao login e volta depois (redirect)',
    async (path) => {
      const router = setup(false)
      await router.push(path)
      expect(router.currentRoute.value.name).toBe('login')
      expect(router.currentRoute.value.query.redirect).toBe(path)
    },
  )

  it('usuário autenticado acessa /pessoas', async () => {
    const router = setup(true)
    await router.push('/pessoas')
    expect(router.currentRoute.value.name).toBe('people')
  })

  it('/me/followers não colide com a rota de perfil (/profile) nem com /u/:handle', () => {
    const router = createRouter({ history: createMemoryHistory(), routes })
    expect(router.resolve('/profile').name).toBe('profile')
    expect(router.resolve('/u/me').name).toBe('user-profile')
  })
})
