/* eslint-disable vue/one-component-per-file */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { defineComponent, h } from 'vue'
import { ApiError } from '@/api/errors'
import { useUiStore } from '@/stores/ui'
import type { LibraryItemDto } from '@/types/api'
import ManualBookPage from '../ManualBookPage.vue'

const api = vi.hoisted(() => ({
  books: { create: vi.fn(), uploadCover: vi.fn() },
  authors: { search: vi.fn() },
  library: { add: vi.fn() },
  countries: { list: vi.fn().mockResolvedValue([]) },
}))
vi.mock('@/api', () => ({ api }))

const Stub = defineComponent({ render: () => null })
const COVER = new File([new Uint8Array(100)], 'capa.jpg', { type: 'image/jpeg' })

/** O seletor real depende de canvas/createImageBitmap (indisponíveis no jsdom). */
const CoverPickerStub = defineComponent({
  props: { file: { type: Object, default: null } },
  emits: ['update:file'],
  setup(props, { emit }) {
    return () =>
      h('div', { 'data-testid': 'cover-picker-stub', 'data-has-file': props.file ? '1' : '' }, [
        h('button', { id: 'pick', type: 'button', onClick: () => emit('update:file', COVER) }),
        h('button', { id: 'unpick', type: 'button', onClick: () => emit('update:file', null) }),
      ])
  },
})

const item = { id: 'item-1', book: { id: 'book-1', title: 'Livro Novo' } } as LibraryItemDto

async function setup() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/books/new', name: 'book-new', component: ManualBookPage },
      { path: '/search', name: 'search', component: Stub },
      { path: '/library/:id', name: 'library-item', component: Stub },
    ],
  })
  await router.push('/books/new')
  const wrapper = mount(ManualBookPage, {
    global: { plugins: [router], stubs: { CoverPicker: CoverPickerStub } },
    attachTo: document.body,
  })
  await flushPromises()
  return { wrapper, router }
}

async function fill(wrapper: Awaited<ReturnType<typeof setup>>['wrapper']) {
  const byLabel = (text: string) => {
    const label = wrapper.findAll('label').find((l) => l.text().startsWith(text))!
    return wrapper.find<HTMLInputElement>(`#${label.attributes('for')}`)
  }
  await byLabel('Título').setValue('Livro Novo')
  await byLabel('Autor 1').setValue('Autora Nova')
  return byLabel
}

describe('ManualBookPage: capa', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    api.books.create.mockReset().mockResolvedValue({ id: 'book-1' })
    api.books.uploadCover.mockReset().mockResolvedValue({})
    api.library.add.mockReset().mockResolvedValue(item)
  })
  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('avisa que, depois de salvo, só moderadores poderão alterar os países de origem', async () => {
    const { wrapper } = await setup()
    expect(wrapper.find('.book-form__moderation').text()).toBe(
      'Depois de salvo, só moderadores poderão alterar estes países.',
    )
  })

  it('arquivo e URL da capa são mutuamente exclusivos', async () => {
    const { wrapper } = await setup()
    const byLabel = await fill(wrapper)
    await byLabel('Ou informe o endereço da imagem').setValue('https://exemplo.com/c.jpg')
    await wrapper.find('#pick').trigger('click') // escolher arquivo limpa a URL
    await flushPromises()
    expect(byLabel('Ou informe o endereço da imagem').element.value).toBe('')
    expect(wrapper.find('[data-testid="cover-picker-stub"]').attributes('data-has-file')).toBe('1')

    await byLabel('Ou informe o endereço da imagem').setValue('https://exemplo.com/outra.jpg') // digitar URL limpa o arquivo
    await flushPromises()
    expect(wrapper.find('[data-testid="cover-picker-stub"]').attributes('data-has-file')).toBe('')
  })

  it('sequência: POST /books → POST /library → POST /books/{id}/cover; abre o detalhe', async () => {
    const order: string[] = []
    api.books.create.mockImplementation(async () => (order.push('create'), { id: 'book-1' }))
    api.library.add.mockImplementation(async () => (order.push('library'), item))
    api.books.uploadCover.mockImplementation(async () => (order.push('cover'), {}))
    const { wrapper, router } = await setup()
    await fill(wrapper)
    await wrapper.find('#pick').trigger('click')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(order).toEqual(['create', 'library', 'cover'])
    expect(api.books.uploadCover).toHaveBeenCalledWith('book-1', COVER)
    expect(api.books.create.mock.calls[0]![0].coverUrl).toBeNull() // arquivo: sem URL externa
    expect(router.currentRoute.value.params.id).toBe('item-1')
  })

  it('falha no envio da capa NÃO perde o livro: avisa e segue para o detalhe', async () => {
    api.books.uploadCover.mockRejectedValue(new ApiError({ status: 500, title: 'x' }))
    const { wrapper, router } = await setup()
    await fill(wrapper)
    await wrapper.find('#pick').trigger('click')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(api.books.create).toHaveBeenCalledTimes(1)
    expect(api.library.add).toHaveBeenCalledTimes(1)
    expect(router.currentRoute.value.name).toBe('library-item')
    const toasts = useUiStore().toasts
    expect(toasts.at(-1)).toMatchObject({ kind: 'warning' })
    expect(toasts.at(-1)!.message).toBe(
      'Livro cadastrado, mas a capa não foi enviada. Você pode tentar de novo na página do livro.',
    )
  })

  it('sem arquivo: não chama o endpoint de capa e envia a URL informada', async () => {
    const { wrapper } = await setup()
    const byLabel = await fill(wrapper)
    await byLabel('Ou informe o endereço da imagem').setValue('https://exemplo.com/c.jpg')
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(api.books.uploadCover).not.toHaveBeenCalled()
    expect(api.books.create.mock.calls[0]![0].coverUrl).toBe('https://exemplo.com/c.jpg')
  })

  it('se POST /library falhar, mantém o bookId criado e não duplica o livro ao repetir', async () => {
    api.library.add.mockRejectedValueOnce(new ApiError({ status: 500, title: 'x' }))
    const { wrapper } = await setup()
    await fill(wrapper)
    await wrapper.find('#pick').trigger('click')
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(api.books.uploadCover).not.toHaveBeenCalled()

    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(api.books.create).toHaveBeenCalledTimes(1)
    expect(api.library.add).toHaveBeenCalledTimes(2)
    expect(api.books.uploadCover).toHaveBeenCalledTimes(1)
  })

  it('mostra "Enviando capa…" durante o envio e ignora duplo envio', async () => {
    let finish!: () => void
    api.books.uploadCover.mockReturnValue(new Promise<void>((resolve) => (finish = resolve)))
    const { wrapper } = await setup()
    await fill(wrapper)
    await wrapper.find('#pick').trigger('click')
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(wrapper.find('.manual__progress').text()).toBe('Enviando capa…')
    expect(wrapper.find('.manual__progress').attributes('aria-live')).toBe('polite')
    expect(api.books.create).toHaveBeenCalledTimes(1)
    expect(api.books.uploadCover).toHaveBeenCalledTimes(1)
    finish()
    await flushPromises()
  })
})

describe('ManualBookPage: autocomplete de autor (authorIds)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    api.books.create.mockReset().mockResolvedValue({ id: 'book-1' })
    api.library.add.mockReset().mockResolvedValue(item)
    api.authors.search.mockReset().mockResolvedValue([
      {
        id: 'a-9',
        name: 'Clarice Lispector',
        nationalityIsos: ['BRA'],
        nationalityNames: ['Brasil'],
      },
    ])
  })
  afterEach(() => {
    vi.useRealTimers()
    document.body.innerHTML = ''
  })

  async function chooseSuggestion(wrapper: Awaited<ReturnType<typeof setup>>['wrapper']) {
    const input = wrapper.find<HTMLInputElement>('input[role="combobox"]')
    await input.setValue('clari')
    await new Promise((resolve) => setTimeout(resolve, 300))
    await flushPromises()
    await input.trigger('keydown', { key: 'ArrowDown' })
    await input.trigger('keydown', { key: 'Enter' })
  }

  it('escolher sugestão envia authorIds alinhado a authorNames (id + null para texto livre)', async () => {
    const { wrapper } = await setup()
    await wrapper.find<HTMLInputElement>('input[role="combobox"]').setValue('') // garante o 1º campo
    const byLabel = (text: string) => {
      const label = wrapper.findAll('label').find((l) => l.text().startsWith(text))!
      return wrapper.find<HTMLInputElement>(`#${label.attributes('for')}`)
    }
    await byLabel('Título').setValue('Livro Novo')
    await chooseSuggestion(wrapper)
    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('Adicionar autor'))!
      .trigger('click')
    await byLabel('Autor 2').setValue('Autor Livre')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(api.authors.search).toHaveBeenCalledWith('clari', 8, expect.any(AbortSignal))
    const request = api.books.create.mock.calls[0]![0]
    expect(request.authorNames).toEqual(['Clarice Lispector', 'Autor Livre'])
    expect(request.authorIds).toEqual(['a-9', null])
  })

  it('texto livre, sem escolher sugestão, NÃO envia authorIds (comportamento anterior)', async () => {
    const { wrapper } = await setup()
    await fill(wrapper)
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    const request = api.books.create.mock.calls[0]![0]
    expect(request.authorNames).toEqual(['Autora Nova'])
    expect('authorIds' in request).toBe(false)
  })

  it('editar o texto depois de escolher desfaz o id; remover um autor mantém o alinhamento', async () => {
    const { wrapper } = await setup()
    const byLabel = (text: string) => {
      const label = wrapper.findAll('label').find((l) => l.text().startsWith(text))!
      return wrapper.find<HTMLInputElement>(`#${label.attributes('for')}`)
    }
    await byLabel('Título').setValue('Livro Novo')
    await chooseSuggestion(wrapper)
    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('Adicionar autor'))!
      .trigger('click')
    await byLabel('Autor 2').setValue('Segundo')
    await wrapper.find('button[aria-label="Remover autor 1"]').trigger('click')
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    const request = api.books.create.mock.calls[0]![0]
    expect(request.authorNames).toEqual(['Segundo'])
    expect('authorIds' in request).toBe(false)
  })
})
