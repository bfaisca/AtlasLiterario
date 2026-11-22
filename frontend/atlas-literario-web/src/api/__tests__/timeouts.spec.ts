import { describe, expect, it, vi } from 'vitest'
import type { AxiosInstance } from 'axios'
import { createHttpBooksApi } from '../books'
import { createHttpLibraryApi } from '../library'
import { createHttpClient } from '../http'
import { DEFAULT_TIMEOUT_MS, SLOW_REQUEST_TIMEOUT_MS } from '../timeouts'

function fakeHttp() {
  const ok = vi.fn().mockResolvedValue({ data: {} })
  return { get: ok, post: ok, put: ok, delete: ok } as unknown as AxiosInstance & {
    get: typeof ok
    post: typeof ok
    put: typeof ok
    delete: typeof ok
  }
}

describe('timeout por requisição', () => {
  it('o cliente global continua em 20 s', () => {
    expect(createHttpClient('/api').defaults.timeout).toBe(DEFAULT_TIMEOUT_MS)
    expect(DEFAULT_TIMEOUT_MS).toBe(20_000)
  })

  it('POST /library usa 45 s', async () => {
    const http = fakeHttp()
    await createHttpLibraryApi(http).add({ bookId: 'b1', status: 'Read' })
    expect(http.post).toHaveBeenCalledWith('/library', expect.anything(), {
      timeout: SLOW_REQUEST_TIMEOUT_MS,
    })
    expect(SLOW_REQUEST_TIMEOUT_MS).toBe(45_000)
  })

  it('POST /books/{id}/enrich e POST /books usam 45 s', async () => {
    const http = fakeHttp()
    const books = createHttpBooksApi(http)
    await books.enrich('b1')
    expect(http.post).toHaveBeenLastCalledWith('/books/b1/enrich', undefined, { timeout: 45_000 })
    await books.create({ title: 'X', authorNames: ['A'] })
    expect(http.post).toHaveBeenLastCalledWith('/books', expect.anything(), { timeout: 45_000 })
  })

  it('as demais chamadas não sobrescrevem o timeout (herdam 20 s)', async () => {
    const http = fakeHttp()
    const library = createHttpLibraryApi(http)
    await library.list()
    await library.get('i1')
    await library.update('i1', {
      status: 'Read',
      rating: null,
      review: null,
      startDate: null,
      finishedDate: null,
    })
    await library.remove('i1')
    const books = createHttpBooksApi(http)
    await books.search({ q: 'x' })
    await books.get('b1')
    await books.update('b1', { title: 'X', authorNames: ['A'] })
    await books.updateOriginCountries('b1', { countryIsos: ['BRA'] })
    const calls = [...http.get.mock.calls, ...http.put.mock.calls, ...http.delete.mock.calls]
    for (const call of calls) {
      expect(call[call.length - 1]).not.toHaveProperty('timeout')
    }
  })
})
