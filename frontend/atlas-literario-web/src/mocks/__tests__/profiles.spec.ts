import { describe, expect, it } from 'vitest'
import { tokenManager } from '@/api/tokenManager'
import { createMockApi } from '../mockApi'

async function as(email: string) {
  const api = createMockApi()
  tokenManager.applyAuthResponse(await api.auth.login({ email, password: 'atlas1234' }))
  return api
}
const demo = () => as('leitora@atlas.dev')

describe('mock: perfis públicos e seguidores (contrato v1.12)', () => {
  it('a conta demo começa Private, sem apelido, com contagens de seguidores/seguindo', async () => {
    const api = await demo()
    const me = await api.auth.me()
    expect(me).toMatchObject({ mapVisibility: 'Private', handle: null })
    // 4 seguem (a conta privada conta no número, mas não aparece na lista); segue 2
    expect(me.followersCount).toBe(4)
    expect(me.followingCount).toBe(2)
  })

  describe('apelido (PUT /auth/me)', () => {
    it('normaliza para minúsculas e permite Public com apelido', async () => {
      const api = await demo()
      const me = await api.auth.updateMe({
        name: 'Leitora Demo',
        mapVisibility: 'Public',
        handle: ' Leitora_Demo ',
      })
      expect(me).toMatchObject({ handle: 'leitora_demo', mapVisibility: 'Public' })
    })

    it('Public sem apelido → 400 profile.handle_required', async () => {
      const api = await demo()
      await expect(
        api.auth.updateMe({ name: 'Demo', mapVisibility: 'Public' }),
      ).rejects.toMatchObject({ status: 400, code: 'profile.handle_required' })
      await expect(
        api.auth.updateMe({ name: 'Demo', mapVisibility: 'Public', handle: '' }),
      ).rejects.toMatchObject({ code: 'profile.handle_required' })
    })

    it('formato inválido e nomes reservados → 400 profile.handle_invalid com errors.handle', async () => {
      const api = await demo()
      for (const handle of ['ab', 'tem espaço', 'x'.repeat(31), 'admin', 'atlas']) {
        await expect(
          api.auth.updateMe({ name: 'Demo', mapVisibility: 'Private', handle }),
        ).rejects.toMatchObject({ status: 400, code: 'profile.handle_invalid' })
      }
    })

    it('apelido de outra conta (inclusive privada, sem diferenciar caixa) → 409 profile.handle_taken', async () => {
      const api = await demo()
      for (const handle of ['marina_lima', 'MARINA_LIMA', 'bia_prado']) {
        await expect(
          api.auth.updateMe({ name: 'Demo', mapVisibility: 'Private', handle }),
        ).rejects.toMatchObject({ status: 409, code: 'profile.handle_taken' })
      }
    })

    it('omitido mantém; null remove (só fora do modo público)', async () => {
      const api = await demo()
      await api.auth.updateMe({ name: 'D', mapVisibility: 'LinkOnly', handle: 'demo_user' })
      expect((await api.auth.updateMe({ name: 'D2', mapVisibility: 'LinkOnly' })).handle).toBe(
        'demo_user',
      )
      expect(
        (await api.auth.updateMe({ name: 'D', mapVisibility: 'Private', handle: null })).handle,
      ).toBeNull()
    })
  })

  describe('busca (GET /profiles)', () => {
    it('só perfis Public; prefixo do apelido ou de qualquer palavra do nome; sem o próprio usuário', async () => {
      const api = await demo()
      const byHandle = await api.profiles.search('marina')
      expect(byHandle.map((p) => p.handle)).toEqual(['marina_lima'])
      // "lima" é palavra do nome de duas pessoas
      expect((await api.profiles.search('lima')).map((p) => p.handle)).toEqual([
        'marcos_lima',
        'marina_lima',
      ])
      // sem acento/caixa
      expect((await api.profiles.search('JOAO')).map((p) => p.handle)).toEqual(['joaoribeiro'])
      // contas privadas nunca aparecem
      expect(await api.profiles.search('bia')).toEqual([])
      expect(await api.profiles.search('ze_c')).toEqual([])
    })

    it('apelido exato/prefixo vem antes do nome', async () => {
      const api = await demo()
      const result = await api.profiles.search('ma')
      expect(result.map((p) => p.handle).slice(0, 2)).toEqual(['marcos_lima', 'marina_lima'])
    })

    it('q curto ou limit fora de 1–20 → 400 validation.failed', async () => {
      const api = await demo()
      await expect(api.profiles.search('a')).rejects.toMatchObject({
        status: 400,
        code: 'validation.failed',
        errors: { q: expect.any(Array) },
      })
      await expect(api.profiles.search('ma', 21)).rejects.toMatchObject({
        errors: { limit: expect.any(Array) },
      })
    })

    it('um perfil que o usuário tornou Public passa a ser descoberto por outros e não por ele', async () => {
      const api = await demo()
      await api.auth.updateMe({ name: 'Leitora Demo', mapVisibility: 'Public', handle: 'leitora' })
      expect(await api.profiles.search('leitora')).toEqual([]) // exclui o próprio
    })
  })

  describe('perfil e mapas de terceiros', () => {
    it('Public: cabeçalho com contagens e flags', async () => {
      const api = await demo()
      const marina = await api.profiles.get('Marina_Lima')
      expect(marina).toMatchObject({
        handle: 'marina_lima',
        name: 'Marina Lima',
        isSelf: false,
        isFollowing: true, // a demo já a segue
        followsYou: true,
      })
      expect(marina.readBooksCount).toBe(7)
      expect(marina.exploredCountriesAuthors).toBeGreaterThan(3)
      expect(marina.followersCount).toBeGreaterThan(0)
    })

    it('Private ou inexistente → 404 profile.not_found (não revela existência)', async () => {
      const api = await demo()
      for (const handle of ['bia_prado', 'ze_carlos', 'nao_existe']) {
        await expect(api.profiles.get(handle)).rejects.toMatchObject({
          status: 404,
          code: 'profile.not_found',
        })
        await expect(api.profiles.map(handle, 'Authors')).rejects.toMatchObject({
          code: 'profile.not_found',
        })
        await expect(api.profiles.followers(handle)).rejects.toMatchObject({ status: 404 })
      }
    })

    it('mapa completo do perfil: sempre só Read', async () => {
      const api = await demo()
      const map = await api.profiles.map('marina_lima', 'Authors')
      expect(map.status).toBe('Read')
      expect(map.totalReadBooks).toBe(7) // o livro abandonado não conta
      expect(map.countries.length).toBeGreaterThan(3)
    })

    it('painel do país: livros SEM libraryItemId e COM resenha; autores com livros', async () => {
      const api = await demo()
      const colombia = await api.profiles.countryDetail('marina_lima', 'Authors', 'COL')
      expect(colombia.status).toBe('Read')
      expect(colombia.books[0]).not.toHaveProperty('libraryItemId')
      const book = colombia.books.find((b) => b.title.startsWith('Cem Anos'))!
      expect(book.review).toContain('Macondo')
      expect(colombia.authors.length).toBeGreaterThan(0)
      expect(colombia.authors[0]!.books[0]).not.toHaveProperty('libraryItemId')
      const origins = await api.profiles.countryDetail('marina_lima', 'Origins', 'COL')
      expect(origins.authors).toEqual([])
    })

    it('ISO desconhecido → 400 country.unknown_iso', async () => {
      const api = await demo()
      await expect(
        api.profiles.countryDetail('marina_lima', 'Authors', 'ZZZ'),
      ).rejects.toMatchObject({ status: 400, code: 'country.unknown_iso' })
    })

    it('perfil sem livros lidos devolve mapa vazio (não erro)', async () => {
      const api = await demo()
      const map = await api.profiles.map('ana_vazia', 'Origins')
      expect(map).toMatchObject({ totalReadBooks: 0, exploredCountries: 0, countries: [] })
    })

    it('o próprio perfil Public é visível ao dono (isSelf, flags falsas)', async () => {
      const api = await demo()
      await api.auth.updateMe({ name: 'Leitora Demo', mapVisibility: 'Public', handle: 'leitora' })
      const self = await api.profiles.get('leitora')
      expect(self).toMatchObject({ isSelf: true, isFollowing: false, followsYou: false })
    })
  })

  describe('seguir', () => {
    it('follow é idempotente e atualiza isFollowing e contagens', async () => {
      const api = await demo()
      const before = await api.profiles.get('joaoribeiro')
      expect(before.isFollowing).toBe(false)
      expect(before.followsYou).toBe(true) // "seguir de volta"

      await api.profiles.follow('joaoribeiro')
      await api.profiles.follow('joaoribeiro') // idempotente

      const after = await api.profiles.get('joaoribeiro')
      expect(after.isFollowing).toBe(true)
      expect(after.followersCount).toBe(before.followersCount + 1)
      expect((await api.auth.me()).followingCount).toBe(3)
    })

    it('unfollow é idempotente, inclusive de perfil privado/inexistente', async () => {
      const api = await demo()
      await api.profiles.unfollow('marina_lima')
      await api.profiles.unfollow('marina_lima')
      expect((await api.profiles.get('marina_lima')).isFollowing).toBe(false)
      await expect(api.profiles.unfollow('bia_prado')).resolves.toBeUndefined()
      await expect(api.profiles.unfollow('nao_existe')).resolves.toBeUndefined()
    })

    it('seguir a si mesmo → 400 follow.self; perfil privado → 404', async () => {
      const api = await demo()
      await api.auth.updateMe({ name: 'Leitora Demo', mapVisibility: 'Public', handle: 'leitora' })
      await expect(api.profiles.follow('leitora')).rejects.toMatchObject({
        status: 400,
        code: 'follow.self',
      })
      await expect(api.profiles.follow('bia_prado')).rejects.toMatchObject({
        status: 404,
        code: 'profile.not_found',
      })
    })
  })

  describe('listas', () => {
    it('minhas listas omitem contas Private e paginam', async () => {
      const api = await demo()
      const followers = await api.profiles.meFollowers()
      // seguem a demo: marina, joão, tomás (Public) e zé (Private, omitido)
      expect(followers.items.map((p) => p.handle).sort()).toEqual([
        'joaoribeiro',
        'marina_lima',
        'tomas_arruda',
      ])
      const page = await api.profiles.meFollowers({ page: 2, pageSize: 2 })
      expect(page).toMatchObject({ page: 2, pageSize: 2, totalCount: 3, totalPages: 2 })
      expect(page.items).toHaveLength(1)
      const following = await api.profiles.meFollowing()
      expect(following.items.map((p) => p.handle).sort()).toEqual(['clara_nunes', 'marina_lima'])
    })

    it('seguidores/seguindo de um perfil Public mostram só perfis Public', async () => {
      const api = await demo()
      const marina = await api.profiles.followers('marina_lima')
      expect(marina.items.every((p) => !['ze_carlos', 'bia_prado'].includes(p.handle))).toBe(true)
      expect(marina.items.map((p) => p.handle)).toContain('joaoribeiro')
      const clara = await api.profiles.following('clara_nunes')
      expect(clara.items.map((p) => p.handle)).toEqual(['marina_lima'])
    })
  })

  it('exige sessão (401) em todas as rotas', async () => {
    tokenManager.clear()
    const api = createMockApi()
    await expect(api.profiles.get('marina_lima')).rejects.toMatchObject({ status: 401 })
    await expect(api.profiles.search('ma')).rejects.toMatchObject({ status: 401 })
    await expect(api.profiles.meFollowers()).rejects.toMatchObject({ status: 401 })
  })
})
