import { describe, expect, it } from 'vitest'
import {
  draftFromBook,
  emptyBookDraft,
  isUploadedCoverUrl,
  toCreateBookRequest,
  validateBook,
} from '../bookForm'

const valid = () => ({ ...emptyBookDraft(), title: 'X', authorNames: ['A'] })

describe('coverUrl no formulário do livro', () => {
  it('URL externa exige https', () => {
    expect(
      validateBook({ ...valid(), coverUrl: 'http://exemplo.com/c.jpg' }).coverUrl,
    ).toBeDefined()
    expect(
      validateBook({ ...valid(), coverUrl: 'https://exemplo.com/c.jpg' }).coverUrl,
    ).toBeUndefined()
  })

  it('o caminho interno /api/books/ NÃO é validado como URL absoluta e volta inalterado', () => {
    const internal = '/api/books/abc/cover?v=123'
    const draft = { ...valid(), coverUrl: internal }
    expect(validateBook(draft).coverUrl).toBeUndefined()
    expect(toCreateBookRequest(draft, false).coverUrl).toBe(internal)
  })

  it('só /api/books/ e data:image/ (mocks) contam como capa enviada', () => {
    expect(isUploadedCoverUrl('/api/books/x/cover?v=1')).toBe(true)
    expect(isUploadedCoverUrl('data:image/png;base64,AAA')).toBe(true)
    expect(isUploadedCoverUrl('/api/outro')).toBe(false)
    expect(isUploadedCoverUrl('https://exemplo.com/a.jpg')).toBe(false)
    expect(isUploadedCoverUrl(null)).toBe(false)
  })
})

describe('authorIds no formulário do livro (contrato v1.10)', () => {
  it('sem escolha: authorIds não é enviado', () => {
    const request = toCreateBookRequest(
      { ...valid(), authorNames: ['A', 'B'], authorIds: [null, null] },
      false,
    )
    expect(request.authorNames).toEqual(['A', 'B'])
    expect('authorIds' in request).toBe(false)
  })

  it('com escolha: ids alinhados aos nomes (null para texto livre)', () => {
    const request = toCreateBookRequest(
      { ...valid(), authorNames: ['A', 'B'], authorIds: [null, 'id-b'] },
      true,
    )
    expect(request.authorNames).toEqual(['A', 'B'])
    expect(request.authorIds).toEqual([null, 'id-b'])
  })

  it('descarta vazios e duplicados mantendo o alinhamento', () => {
    const request = toCreateBookRequest(
      {
        ...valid(),
        authorNames: ['  ', 'A', 'A', 'B', 'C'],
        authorIds: ['x', 'id-a', null, 'id-a', 'id-c'],
      },
      false,
    )
    expect(request.authorNames).toEqual(['A', 'C'])
    expect(request.authorIds).toEqual(['id-a', 'id-c'])
  })

  it('o rascunho nasce sem ids e a edição de um livro existente também (resolução por nome)', () => {
    expect(emptyBookDraft().authorIds).toEqual([null])
    const draft = draftFromBook({
      id: 'b',
      title: 'T',
      subtitle: null,
      isbn13: null,
      isbn10: null,
      publisher: null,
      publicationYear: null,
      description: null,
      coverUrl: null,
      originalLanguage: null,
      genres: [],
      authors: [
        { id: '1', name: 'A', nationalities: [] },
        { id: '2', name: 'B', nationalities: [] },
      ],
      originCountries: [],
      geoEnrichmentStatus: 'Completed',
      externalIds: [],
    })
    expect(draft.authorNames).toEqual(['A', 'B'])
    expect(draft.authorIds).toEqual([null, null])
  })
})
