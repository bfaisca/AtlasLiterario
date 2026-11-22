import { describe, expect, it, vi } from 'vitest'
import type { BookSearchResultDto } from '@/types/api'
import { findLibraryItemId, isSameBook } from '../addRecovery'

const base: BookSearchResultDto = {
  resultKey: 'GoogleBooks:x',
  bookId: null,
  inLibrary: false,
  libraryItemId: null,
  title: 'Norwegian Wood',
  subtitle: null,
  authors: ['Haruki Murakami'],
  publisher: null,
  publicationYear: null,
  isbn13: null,
  isbn10: null,
  coverUrl: null,
  description: null,
  source: { provider: 'GoogleBooks', externalId: 'x' },
}

describe('isSameBook', () => {
  it('casa por chave, ISBN ou título+primeiro autor (sem acento/caixa)', () => {
    expect(isSameBook({ ...base }, base)).toBe(true)
    expect(
      isSameBook(
        { ...base, resultKey: 'local:1', isbn13: '1' },
        { ...base, isbn13: '1', title: 'Outro' },
      ),
    ).toBe(true)
    expect(isSameBook({ ...base, resultKey: 'local:1', title: 'NORWEGIAN wood' }, base)).toBe(true)
    expect(isSameBook({ ...base, resultKey: 'local:1', authors: ['Outra Pessoa'] }, base)).toBe(
      false,
    )
  })
})

describe('findLibraryItemId', () => {
  it('tenta várias vezes nas esperas indicadas e para ao achar', async () => {
    const hit = { ...base, resultKey: 'local:1', inLibrary: true, libraryItemId: 'i1' }
    const search = vi
      .fn()
      .mockResolvedValueOnce([])
      .mockRejectedValueOnce(new Error('rede'))
      .mockResolvedValueOnce([hit])
    const sleep = vi.fn().mockResolvedValue(undefined)
    expect(await findLibraryItemId(base, search, { delaysMs: [0, 10, 20, 30], sleep })).toBe('i1')
    expect(search).toHaveBeenCalledTimes(3)
    expect(sleep.mock.calls.map((c) => c[0])).toEqual([10, 20])
  })

  it('devolve null quando nunca aparece', async () => {
    const search = vi.fn().mockResolvedValue([])
    expect(
      await findLibraryItemId(base, search, { delaysMs: [0, 1], sleep: async () => {} }),
    ).toBeNull()
  })
})
