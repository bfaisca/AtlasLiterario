import { describe, expect, it } from 'vitest'
import { tokenManager } from '@/api/tokenManager'
import { createMockApi } from '../mockApi'

async function setup() {
  const api = createMockApi()
  tokenManager.applyAuthResponse(
    await api.auth.login({ email: 'leitora@atlas.dev', password: 'atlas1234' }),
  )
  return api
}

describe('mock: GET /authors?q=&limit= (contrato v1.10)', () => {
  it('prefixo do nome primeiro, depois prefixo de qualquer palavra; sem acento nem caixa', async () => {
    const api = await setup()
    const byName = (await api.authors.search('FRANZ')).map((a) => a.name)
    expect(byName).toEqual(['Franz Kafka'])
    // "ma" casa prefixo de nome (Machado, Mahfouz, Mario…) antes de prefixo de palavra (García Márquez)
    const names = (await api.authors.search('ma')).map((a) => a.name)
    expect(names.indexOf('Machado de Assis')).toBeLessThan(names.indexOf('Gabriel García Márquez'))
    expect((await api.authors.search('dostoie')).map((a) => a.name)).toEqual(['Fiódor Dostoiévski'])
    expect((await api.authors.search('kafka')).map((a) => a.name)).toEqual(['Franz Kafka'])
  })

  it('devolve nacionalidades (Citizenship) em português; vazio = desconhecido', async () => {
    const api = await setup()
    const [kafka] = await api.authors.search('kafka')
    expect(kafka!.nationalityIsos).toEqual(['AUT', 'CZE'])
    expect(kafka!.nationalityNames).toHaveLength(2)
    expect(kafka!.nationalityNames.every(Boolean)).toBe(true)
    const [duarte] = await api.authors.search('helena')
    expect(duarte).toMatchObject({ nationalityIsos: [], nationalityNames: [] })
    // sem campos extras (somente o necessário)
    expect(Object.keys(kafka!).sort()).toEqual([
      'id',
      'name',
      'nationalityIsos',
      'nationalityNames',
    ])
  })

  it('respeita o limite e valida q (≥ 2) e limit (1–20) com 400 validation.failed', async () => {
    const api = await setup()
    expect(await api.authors.search('a', 5).catch((e) => e)).toMatchObject({
      status: 400,
      code: 'validation.failed',
    })
    expect(await api.authors.search(' a ').catch((e) => e)).toMatchObject({
      errors: { q: expect.any(Array) },
    })
    expect(await api.authors.search('ma', 0).catch((e) => e)).toMatchObject({
      errors: { limit: expect.any(Array) },
    })
    expect(await api.authors.search('ma', 21).catch((e) => e)).toMatchObject({
      errors: { limit: expect.any(Array) },
    })
    expect(await api.authors.search('a', 5, undefined).catch(() => 'x')).toBe('x')
    expect((await api.authors.search('ma', 2)).length).toBeLessThanOrEqual(2)
  })

  it('requisição abortada rejeita sem aplicar resultado', async () => {
    const api = await setup()
    const controller = new AbortController()
    const promise = api.authors.search('kafka', 8, controller.signal)
    controller.abort()
    await expect(promise).rejects.toMatchObject({ name: 'AbortError' })
  })
})

describe('mock: POST /books com authorIds (contrato v1.10)', () => {
  it('id não nulo reutiliza o autor cadastrado (sem duplicar); null cria/resolve por nome', async () => {
    const api = await setup()
    const [camus] = await api.authors.search('camus')
    const before = (await api.authors.search('camus')).length
    const book = await api.books.create({
      title: 'Novo',
      // o nome digitado difere do cadastrado: o id é que manda
      authorNames: ['A. Camus', 'Autora Nova'],
      authorIds: [camus!.id, null],
    })
    expect(book.authors.map((a) => a.id)[0]).toBe(camus!.id)
    expect(book.authors[0]!.name).toBe('Albert Camus')
    expect(book.authors[1]!.name).toBe('Autora Nova')
    expect(await api.authors.search('camus')).toHaveLength(before)
  })

  it('authorIds de tamanho diferente → 400; id desconhecido → 400 em errors.authorIds', async () => {
    const api = await setup()
    await expect(
      api.books.create({ title: 'X', authorNames: ['A', 'B'], authorIds: [null] }),
    ).rejects.toMatchObject({ status: 400, errors: { authorIds: expect.any(Array) } })
    await expect(
      api.books.create({ title: 'X', authorNames: ['A'], authorIds: ['nao-existe'] }),
    ).rejects.toMatchObject({ status: 400, errors: { authorIds: expect.any(Array) } })
  })

  it('sem authorIds funciona como antes (resolução por nome)', async () => {
    const api = await setup()
    const [camus] = await api.authors.search('camus')
    const book = await api.books.create({ title: 'Y', authorNames: ['albert camus'] })
    expect(book.authors[0]!.id).toBe(camus!.id)
  })
})

describe('mock: GET /maps/{type}/countries/{iso} traz authors (contrato v1.10)', () => {
  it('Authors: autor com 2 nacionalidades aparece nos dois países; Origins: authors = []', async () => {
    const api = await setup()
    const aut = await api.maps.getCountryDetail('Authors', 'AUT', 'All')
    const cze = await api.maps.getCountryDetail('Authors', 'CZE', 'All')
    expect(aut.authors.map((a) => a.name)).toContain('Franz Kafka')
    expect(cze.authors.map((a) => a.name)).toContain('Franz Kafka')
    expect(aut.authors.every((a) => a.countryIso === 'AUT')).toBe(true)
    const origins = await api.maps.getCountryDetail('Origins', 'COL')
    expect(origins.authors).toEqual([])
    expect(origins.books.length).toBeGreaterThan(0)
  })
})
