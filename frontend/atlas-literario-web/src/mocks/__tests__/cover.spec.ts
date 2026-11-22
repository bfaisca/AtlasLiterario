import { describe, expect, it } from 'vitest'
import { tokenManager } from '@/api/tokenManager'
import { createMockApi } from '../mockApi'

const image = (name = 'capa.jpg', type = 'image/jpeg', size = 200) =>
  new File([new Uint8Array(size)], name, { type })

async function setup() {
  const api = createMockApi()
  tokenManager.applyAuthResponse(
    await api.auth.login({ email: 'leitora@atlas.dev', password: 'atlas1234' }),
  )
  return api
}

describe('mock: capa de livro cadastrado manualmente', () => {
  it('aparece na pesquisa com a capa enviada e abre o item (libraryItemId)', async () => {
    const api = await setup()
    const book = await api.books.create({
      title: 'Zeta Manual Exclusivo',
      authorNames: ['Autora Zeta'],
    })
    const item = await api.library.add({ bookId: book.id, status: 'Read' })
    const updated = await api.books.uploadCover(book.id, image())
    expect(updated.coverUrl).toMatch(/^data:image\/jpeg;base64,/)

    const results = await api.books.search({ q: 'Zeta Manual Exclusivo', type: 'title' })
    const found = results.find((r) => r.bookId === book.id)!
    expect(found.inLibrary).toBe(true)
    expect(found.libraryItemId).toBe(item.id)
    expect(found.coverUrl).toBe(updated.coverUrl)
    expect((await api.library.get(found.libraryItemId!)).book.coverUrl).toBe(updated.coverUrl)
  })

  it('exige o livro na biblioteca (403 book.not_in_library), como no contrato v1.6', async () => {
    const api = await setup()
    const book = await api.books.create({ title: 'Fora da Biblioteca', authorNames: ['X'] })
    await expect(api.books.uploadCover(book.id, image())).rejects.toMatchObject({
      status: 403,
      code: 'book.not_in_library',
    })
  })

  it('erros simulados: tipo inválido (400) e arquivo grande (413)', async () => {
    const api = await setup()
    const book = await api.books.create({ title: 'Erros de Capa', authorNames: ['X'] })
    await api.library.add({ bookId: book.id, status: 'Read' })
    await expect(api.books.uploadCover(book.id, image('a.gif', 'image/gif'))).rejects.toMatchObject(
      {
        status: 400,
        code: 'cover.invalid_type',
      },
    )
    await expect(
      api.books.uploadCover(book.id, image('a.jpg', 'image/jpeg', 2 * 1024 * 1024 + 1)),
    ).rejects.toMatchObject({ status: 413, code: 'cover.too_large' })
  })

  it('remover: volta a null; sem capa enviada, 404 cover.not_found', async () => {
    const api = await setup()
    const book = await api.books.create({ title: 'Remover Capa', authorNames: ['X'] })
    await api.library.add({ bookId: book.id, status: 'Read' })
    await api.books.uploadCover(book.id, image())
    expect((await api.books.removeCover(book.id)).coverUrl).toBeNull()
    await expect(api.books.removeCover(book.id)).rejects.toMatchObject({
      status: 404,
      code: 'cover.not_found',
    })
  })

  it('PUT com o coverUrl atual mantém a capa; com null a remove', async () => {
    const api = await setup()
    const book = await api.books.create({ title: 'Editar Livro Capa', authorNames: ['X'] })
    await api.library.add({ bookId: book.id, status: 'Read' })
    const { coverUrl } = await api.books.uploadCover(book.id, image())
    const kept = await api.books.update(book.id, {
      title: 'Editar Livro Capa',
      authorNames: ['X'],
      coverUrl,
    })
    expect(kept.coverUrl).toBe(coverUrl)
    const cleared = await api.books.update(book.id, {
      title: 'Editar Livro Capa',
      authorNames: ['X'],
      coverUrl: null,
    })
    expect(cleared.coverUrl).toBeNull()
  })
})
