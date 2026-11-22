import { describe, expect, it } from 'vitest'
import { emptyBookDraft, isUploadedCoverUrl, toCreateBookRequest, validateBook } from '../bookForm'

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
