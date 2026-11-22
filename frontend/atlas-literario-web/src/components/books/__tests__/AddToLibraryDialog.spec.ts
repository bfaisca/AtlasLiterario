import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { ApiError } from '@/api/errors'
import type { BookSearchResultDto, LibraryItemDto } from '@/types/api'
import AddToLibraryDialog from '../AddToLibraryDialog.vue'

const api = vi.hoisted(() => ({
  books: { search: vi.fn() },
  library: { add: vi.fn(), get: vi.fn() },
}))
vi.mock('@/api', () => ({ api }))

const result: BookSearchResultDto = {
  resultKey: 'GoogleBooks:nw',
  bookId: null,
  inLibrary: false,
  libraryItemId: null,
  title: 'Norwegian Wood',
  subtitle: null,
  authors: ['Haruki Murakami'],
  publisher: null,
  publicationYear: 1987,
  isbn13: null,
  isbn10: null,
  coverUrl: null,
  description: null,
  source: { provider: 'GoogleBooks', externalId: 'nw' },
}
/** O mesmo livro, depois de importado: vira resultado local e já está na biblioteca. */
const created: BookSearchResultDto = {
  ...result,
  resultKey: 'local:b-9',
  bookId: 'b-9',
  inLibrary: true,
  libraryItemId: 'item-9',
  source: { provider: 'Local', externalId: 'b-9' },
}
const item = { id: 'item-9', book: { title: 'Norwegian Wood' } } as LibraryItemDto
const timeout = () => new ApiError({ status: 0, title: 't', code: 'network.timeout' })

function mountDialog(props: Partial<{ result: BookSearchResultDto }> = {}) {
  return mount(AddToLibraryDialog, {
    props: { result, open: true, 'onUpdate:open': vi.fn(), ...props },
    attachTo: document.body,
  })
}
const form = () => document.querySelector<HTMLFormElement>('#add-to-library')!
const submit = async () => {
  form().dispatchEvent(new Event('submit', { cancelable: true }))
  await flushPromises()
}
const progress = () => document.querySelector('.add__progress')?.textContent?.trim() ?? ''
const alertText = () => document.querySelector('[role="alert"]')?.textContent?.trim() ?? ''

describe('AddToLibraryDialog', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    setActivePinia(createPinia())
    api.books.search.mockReset()
    api.library.add.mockReset()
    api.library.get.mockReset()
  })
  afterEach(() => {
    vi.useRealTimers()
    document.body.innerHTML = ''
  })

  it('adiciona normalmente e emite "created"', async () => {
    api.library.add.mockResolvedValue(item)
    const wrapper = mountDialog()
    await flushPromises()
    await submit()
    expect(wrapper.emitted('added')![0]).toEqual([item, 'created'])
  })

  it('mostra o progresso (aria-live), desabilita campos/botões e bloqueia Esc e duplo envio', async () => {
    let resolve!: (value: LibraryItemDto) => void
    api.library.add.mockReturnValue(new Promise((res) => (resolve = res)))
    const wrapper = mountDialog()
    await flushPromises()
    await submit()
    await submit() // segundo envio durante o primeiro

    expect(api.library.add).toHaveBeenCalledTimes(1)
    expect(document.querySelector('.add__progress')!.getAttribute('aria-live')).toBe('polite')
    expect(progress()).toContain('Salvando e buscando nacionalidade e origem da obra')
    expect(progress()).toContain('isso pode levar alguns segundos')
    expect(document.querySelector<HTMLFieldSetElement>('.add__fields')!.disabled).toBe(true)
    const buttons = [...document.querySelectorAll<HTMLButtonElement>('.panel__footer button')]
    expect(buttons.every((b) => b.disabled)).toBe(true)
    expect(document.querySelector<HTMLButtonElement>('.panel__close')!.disabled).toBe(true)

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    await flushPromises()
    expect(wrapper.emitted('update:open')).toBeUndefined()

    resolve(item)
    await flushPromises()
    expect(wrapper.emitted('added')).toHaveLength(1)
  })

  it('timeout com item criado no servidor: confere, recupera e emite "recovered" (sem erro)', async () => {
    api.library.add.mockRejectedValue(timeout())
    api.books.search.mockResolvedValue([created])
    api.library.get.mockResolvedValue(item)
    const wrapper = mountDialog()
    await flushPromises()
    await submit()
    await vi.advanceTimersByTimeAsync(0)
    await flushPromises()

    expect(api.books.search).toHaveBeenCalledWith({ q: 'Norwegian Wood', type: 'title', limit: 20 })
    expect(api.library.get).toHaveBeenCalledWith('item-9')
    expect(wrapper.emitted('added')![0]).toEqual([item, 'recovered'])
    expect(alertText()).toBe('')
  })

  it('espera o servidor terminar: acha o item numa verificação posterior', async () => {
    api.library.add.mockRejectedValue(timeout())
    api.books.search
      .mockResolvedValueOnce([{ ...created, inLibrary: false, libraryItemId: null }])
      .mockResolvedValueOnce([created])
    api.library.get.mockResolvedValue(item)
    const wrapper = mountDialog()
    await flushPromises()
    await submit()
    expect(progress()).toContain('Conferindo se o livro já foi adicionado')
    await vi.advanceTimersByTimeAsync(2500)
    await flushPromises()
    expect(wrapper.emitted('added')![0]).toEqual([item, 'recovered'])
  })

  it('timeout sem item criado: mostra erro com instrução de nova tentativa e mantém o formulário', async () => {
    api.library.add.mockRejectedValue(timeout())
    api.books.search.mockResolvedValue([])
    const wrapper = mountDialog()
    await flushPromises()
    await submit()
    await vi.advanceTimersByTimeAsync(10_000)
    await flushPromises()

    expect(wrapper.emitted('added')).toBeUndefined()
    expect(alertText()).toContain('demorou demais')
    expect(alertText()).toContain('O livro não foi adicionado')
    expect(document.querySelector('#add-to-library')).not.toBeNull() // formulário preservado
    expect(document.querySelector<HTMLFieldSetElement>('.add__fields')!.disabled).toBe(false)

    // nova tentativa funciona
    api.library.add.mockResolvedValue(item)
    await submit()
    expect(wrapper.emitted('added')![0]).toEqual([item, 'created'])
  })

  it('409 library.duplicate com libraryItemId: sucesso suave levando ao item existente', async () => {
    api.library.add.mockRejectedValue(
      new ApiError({ status: 409, title: 'c', code: 'library.duplicate' }),
    )
    api.library.get.mockResolvedValue(item)
    const wrapper = mountDialog({ result: { ...created, inLibrary: false } })
    await flushPromises()
    await submit()
    expect(api.library.get).toHaveBeenCalledWith('item-9')
    expect(wrapper.emitted('added')![0]).toEqual([item, 'duplicate'])
    expect(api.books.search).not.toHaveBeenCalled()
  })

  it('409 sem libraryItemId: procura o item; sem achar, mostra mensagem clara', async () => {
    api.library.add.mockRejectedValue(
      new ApiError({ status: 409, title: 'c', code: 'library.duplicate' }),
    )
    api.books.search.mockResolvedValue([])
    const wrapper = mountDialog()
    await flushPromises()
    await submit()
    await vi.advanceTimersByTimeAsync(10_000)
    await flushPromises()
    expect(wrapper.emitted('added')).toBeUndefined()
    expect(alertText()).toBe('Este livro já está na sua biblioteca.')
  })

  it('erros de validação continuam indo para os campos (sem recuperação)', async () => {
    api.library.add.mockRejectedValue(
      new ApiError({ status: 400, title: 'v', code: 'library.invalid_dates' }),
    )
    mountDialog()
    await flushPromises()
    await submit()
    expect(api.books.search).not.toHaveBeenCalled()
    expect(alertText()).toContain('data de início')
  })
})
