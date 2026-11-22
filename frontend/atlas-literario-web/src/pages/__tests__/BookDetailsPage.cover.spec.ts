/* eslint-disable vue/one-component-per-file */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { defineComponent, h } from 'vue'
import { ApiError } from '@/api/errors'
import { useLibraryStore } from '@/stores/library'
import { useUiStore } from '@/stores/ui'
import type { LibraryItemDto } from '@/types/api'
import BookDetailsPage from '../BookDetailsPage.vue'

const api = vi.hoisted(() => ({
  library: { get: vi.fn() },
  books: { uploadCover: vi.fn(), removeCover: vi.fn(), update: vi.fn() },
  authors: {},
  countries: { list: vi.fn().mockResolvedValue([]) },
}))
vi.mock('@/api', () => ({ api }))

const Stub = defineComponent({ render: () => null })
const FILE = new File([new Uint8Array(100)], 'nova.jpg', { type: 'image/jpeg' })
const CoverPickerStub = defineComponent({
  props: { file: { type: Object, default: null }, disabled: Boolean },
  emits: ['update:file'],
  setup(props, { emit }) {
    return () =>
      h('div', { 'data-testid': 'picker-stub', 'data-disabled': props.disabled ? '1' : '' }, [
        h('button', { id: 'pick', type: 'button', onClick: () => emit('update:file', FILE) }),
      ])
  },
})

const INTERNAL = '/api/books/b1/cover?v=abc123'

function itemWith(coverUrl: string | null): LibraryItemDto {
  return {
    id: 'i1',
    status: 'Read',
    rating: null,
    review: null,
    startDate: null,
    finishedDate: null,
    createdAt: '2025-01-01T00:00:00Z',
    updatedAt: '2025-01-01T00:00:00Z',
    book: {
      id: 'b1',
      title: 'Livro Capa',
      subtitle: null,
      isbn13: null,
      isbn10: null,
      publisher: null,
      publicationYear: null,
      description: null,
      coverUrl,
      originalLanguage: null,
      genres: [],
      authors: [{ id: 'a1', name: 'Autora', nationalities: [] }],
      originCountries: [],
      geoEnrichmentStatus: 'Completed',
      externalIds: [],
    },
  } as LibraryItemDto
}

async function mountWith(coverUrl: string | null) {
  api.library.get.mockResolvedValue(itemWith(coverUrl))
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/library', name: 'library', component: Stub }],
  })
  const wrapper = mount(BookDetailsPage, {
    props: { id: 'i1' },
    global: { plugins: [router], stubs: { CoverPicker: CoverPickerStub } },
    attachTo: document.body,
  })
  await flushPromises()
  return wrapper
}
const button = (text: string) =>
  [...document.querySelectorAll<HTMLButtonElement>('button')].find((b) =>
    b.textContent?.trim().includes(text),
  )

describe('BookDetailsPage: capa', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    api.books.uploadCover.mockReset().mockResolvedValue({})
    api.books.removeCover.mockReset().mockResolvedValue({})
  })
  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('"Trocar capa" abre a gaveta; escolher a imagem envia na hora e atualiza a tela e o cache', async () => {
    const wrapper = await mountWith(null)
    const invalidate = vi.spyOn(useLibraryStore(), 'invalidate')
    const getCalls = api.library.get.mock.calls.length
    expect(wrapper.text()).toContain('Este livro ainda não tem capa.')
    expect(button('Remover capa')).toBeUndefined() // sem capa enviada, sem "Remover capa"

    button('Trocar capa')!.click()
    await flushPromises()
    api.library.get.mockResolvedValue(itemWith(INTERNAL))
    document.querySelector<HTMLButtonElement>('#pick')!.click()
    await flushPromises()

    expect(api.books.uploadCover).toHaveBeenCalledWith('b1', FILE)
    expect(invalidate).toHaveBeenCalled()
    expect(api.library.get.mock.calls.length).toBeGreaterThan(getCalls) // recarrega o item
    expect(document.querySelector('[role="dialog"]')).toBeNull() // gaveta fechou
    expect(useUiStore().toasts.at(-1)!.message).toBe('Capa atualizada.')
    expect(wrapper.find('img').attributes('src')).toBe(INTERNAL)
  })

  it('falha no envio: mostra o erro na gaveta e permite tentar de novo', async () => {
    api.books.uploadCover.mockRejectedValueOnce(
      new ApiError({ status: 413, title: 'x', code: 'cover.too_large' }),
    )
    const wrapper = await mountWith(null)
    button('Trocar capa')!.click()
    await flushPromises()
    document.querySelector<HTMLButtonElement>('#pick')!.click()
    await flushPromises()

    const alert = document.querySelector('[role="alert"]')!
    expect(alert.textContent).toContain('máximo de 2 MB')
    expect(document.querySelector('[role="dialog"]')).not.toBeNull()
    button('Tentar enviar de novo')!.click()
    await flushPromises()
    expect(api.books.uploadCover).toHaveBeenCalledTimes(2)
    expect(document.querySelector('[role="dialog"]')).toBeNull()
    expect(wrapper.exists()).toBe(true)
  })

  it('durante o envio mostra "Enviando capa…", desabilita o seletor e bloqueia Esc', async () => {
    let finish!: () => void
    api.books.uploadCover.mockReturnValue(new Promise<void>((resolve) => (finish = resolve)))
    await mountWith(null)
    button('Trocar capa')!.click()
    await flushPromises()
    document.querySelector<HTMLButtonElement>('#pick')!.click()
    await flushPromises()
    expect(document.querySelector('.cover-form__status')!.textContent).toContain('Enviando capa…')
    expect(
      document.querySelector('[data-testid="picker-stub"]')!.getAttribute('data-disabled'),
    ).toBe('1')
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    await flushPromises()
    expect(document.querySelector('[role="dialog"]')).not.toBeNull()
    finish()
    await flushPromises()
  })

  it('"Remover capa" só aparece para capa enviada e exige confirmação', async () => {
    await mountWith(INTERNAL)
    const remove = button('Remover capa')!
    expect(remove).toBeDefined()
    remove.click()
    await flushPromises()
    expect(api.books.removeCover).not.toHaveBeenCalled() // ainda não confirmou
    expect(document.querySelector('[role="dialog"]')!.textContent).toContain('Remover capa?')

    api.library.get.mockResolvedValue(itemWith(null))
    const confirm = [...document.querySelectorAll<HTMLButtonElement>('.panel__footer button')].find(
      (b) => b.textContent?.includes('Remover capa'),
    )!
    confirm.click()
    await flushPromises()
    expect(api.books.removeCover).toHaveBeenCalledWith('b1')
    expect(useUiStore().toasts.at(-1)!.message).toBe('Capa removida.')
  })

  it('capa de fonte externa: "Trocar capa" existe, "Remover capa" não', async () => {
    const wrapper = await mountWith('https://exemplo.com/c.jpg')
    expect(wrapper.text()).toContain('Capa obtida de uma fonte externa.')
    expect(button('Trocar capa')).toBeDefined()
    expect(button('Remover capa')).toBeUndefined()
  })

  it('a edição bibliográfica reenvia o coverUrl interno sem alterá-lo (e sem validar como https)', async () => {
    api.books.update.mockResolvedValue({})
    await mountWith(INTERNAL)
    button('Corrigir dados do livro')!.click()
    await flushPromises()
    const form = document.querySelector<HTMLFormElement>('#book-form')!
    form.dispatchEvent(new Event('submit', { cancelable: true }))
    await flushPromises()
    expect(api.books.update).toHaveBeenCalledTimes(1)
    expect(api.books.update.mock.calls[0]![1].coverUrl).toBe(INTERNAL)
    expect(api.books.update.mock.calls[0]![1]).not.toHaveProperty('originCountryIsos')
  })
})
