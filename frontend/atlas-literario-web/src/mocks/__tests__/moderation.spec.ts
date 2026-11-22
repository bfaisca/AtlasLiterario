import { describe, expect, it } from 'vitest'
import { tokenManager } from '@/api/tokenManager'
import { createMockApi } from '../mockApi'

async function as(email: string) {
  const api = createMockApi()
  tokenManager.applyAuthResponse(await api.auth.login({ email, password: 'atlas1234' }))
  return api
}

describe('mock: regra de países (contrato v1.7)', () => {
  it('o login devolve o papel: comum = User, moderadora = Moderator', async () => {
    const api = createMockApi()
    expect(
      (await api.auth.login({ email: 'leitora@atlas.dev', password: 'atlas1234' })).user.role,
    ).toBe('User')
    expect(
      (await api.auth.login({ email: 'moderadora@atlas.dev', password: 'atlas1234' })).user.role,
    ).toBe('Moderator')
    const registered = await api.auth.register({
      name: 'N',
      email: 'n@x.com',
      password: 'atlas1234',
    })
    expect(registered.user.role).toBe('User')
  })

  it('usuário comum preenche origem vazia, mas não altera nem remove a existente (403)', async () => {
    const api = await as('leitora@atlas.dev')
    const book = await api.books.create({ title: 'Sem Origem', authorNames: ['X'] })
    await api.library.add({ bookId: book.id, status: 'Read' })
    const filled = await api.books.updateOriginCountries(book.id, { countryIsos: ['BRA'] })
    expect(filled.originCountries.map((o) => o.countryIso)).toEqual(['BRA'])
    await expect(
      api.books.updateOriginCountries(book.id, { countryIsos: ['PRT'] }),
    ).rejects.toMatchObject({
      status: 403,
      code: 'geo.moderator_required',
    })
    await expect(
      api.books.updateOriginCountries(book.id, { countryIsos: [] }),
    ).rejects.toMatchObject({
      code: 'geo.moderator_required',
    })
  })

  it('usuário comum precisa ter o livro na biblioteca para a primeira informação', async () => {
    const api = await as('leitora@atlas.dev')
    const book = await api.books.create({ title: 'Fora da Estante', authorNames: ['X'] })
    await expect(
      api.books.updateOriginCountries(book.id, { countryIsos: ['BRA'] }),
    ).rejects.toMatchObject({
      status: 403,
      code: 'book.not_in_library',
    })
  })

  it('cidadania de autor segue a mesma regra', async () => {
    const api = await as('leitora@atlas.dev')
    const book = await api.books.create({ title: 'Autor Novo', authorNames: ['Autor Sem Pais'] })
    const item = await api.library.add({ bookId: book.id, status: 'Read' })
    const author = item.book.authors[0]!
    await api.authors.updateCountries(author.id, { citizenshipIsos: ['ARG'] })
    await expect(
      api.authors.updateCountries(author.id, { citizenshipIsos: ['CHL'] }),
    ).rejects.toMatchObject({
      code: 'geo.moderator_required',
    })
  })

  it('moderadora altera e remove países já informados', async () => {
    const api = await as('moderadora@atlas.dev')
    const book = await api.books.create({ title: 'Livro da Moderação', authorNames: ['X'] })
    await api.library.add({ bookId: book.id, status: 'Read' })
    await api.books.updateOriginCountries(book.id, { countryIsos: ['BRA'] })
    const changed = await api.books.updateOriginCountries(book.id, { countryIsos: ['PRT'] })
    expect(changed.originCountries.map((o) => o.countryIso)).toEqual(['PRT'])
    const removed = await api.books.updateOriginCountries(book.id, { countryIsos: [] })
    expect(removed.originCountries).toEqual([])
  })

  it('PUT /books com originCountryIsos obedece à regra; sem o campo, não toca nas origens', async () => {
    const api = await as('leitora@atlas.dev')
    const book = await api.books.create({
      title: 'Livro PUT',
      authorNames: ['X'],
      originCountryIsos: ['BRA'],
    })
    await api.library.add({ bookId: book.id, status: 'Read' })
    await expect(
      api.books.update(book.id, {
        title: 'Livro PUT',
        authorNames: ['X'],
        originCountryIsos: ['PRT'],
      }),
    ).rejects.toMatchObject({ code: 'geo.moderator_required' })
    const same = await api.books.update(book.id, { title: 'Livro PUT 2', authorNames: ['X'] })
    expect(same.originCountries.map((o) => o.countryIso)).toEqual(['BRA'])
  })
})
