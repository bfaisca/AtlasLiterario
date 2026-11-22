import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { defineComponent } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { ApiError } from '@/api/errors'
import type { BookSearchResultDto, LibraryItemDto } from '@/types/api'
import SearchPage from '../SearchPage.vue'

const api = vi.hoisted(() => ({
  books: { search: vi.fn() },
  library: { add: vi.fn(), list: vi.fn() },
}))
vi.mock('@/api', () => ({ api }))

const Stub = defineComponent({ render: () => null })

const RESULTS: BookSearchResultDto[] = [
  {
    resultKey: 'GoogleBooks:gb1',
    bookId: null,
    inLibrary: false,
    libraryItemId: null,
    title: 'O Alienista',
    subtitle: null,
    authors: ['Machado de Assis'],
    publisher: 'Editora',
    publicationYear: 1882,
    isbn13: null,
    isbn10: null,
    coverUrl: null,
    description: null,
    source: { provider: 'GoogleBooks', externalId: 'gb1' },
  },
  {
    resultKey: 'local:b2',
    bookId: 'b2',
    inLibrary: true,
    libraryItemId: 'li-2',
    title: 'Dom Casmurro',
    subtitle: null,
    authors: ['Machado de Assis'],
    publisher: null,
    publicationYear: 1899,
    isbn13: null,
    isbn10: null,
    coverUrl: null,
    description: null,
    source: { provider: 'Local', externalId: 'b2' },
  },
]

async function setup() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/search', name: 'search', component: SearchPage },
      { path: '/books/new', name: 'book-new', component: Stub },
      { path: '/library/:id', name: 'library-item', component: Stub },
    ],
  })
  await router.push('/search')
  const wrapper = mount(SearchPage, {
    global: { plugins: [router] },
    attachTo: document.body,
  })
  return { wrapper, router }
}

describe('SearchPage', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    setActivePinia(createPinia())
    api.books.search.mockReset()
    api.library.add.mockReset()
  })
  afterEach(() => {
    vi.useRealTimers()
    document.body.innerHTML = ''
  })

  it('pesquisa com debounce, mostra resultados e indica o que já está na biblioteca', async () => {
    api.books.search.mockResolvedValue(RESULTS)
    const { wrapper } = await setup()
    const input = wrapper.find('#search-input')

    await input.setValue('ma')
    await input.setValue('machado')
    expect(api.books.search).not.toHaveBeenCalled()

    await vi.advanceTimersByTimeAsync(400)
    await flushPromises()

    expect(api.books.search).toHaveBeenCalledTimes(1)
    expect(api.books.search).toHaveBeenCalledWith({ q: 'machado', type: 'all', limit: 20 })
    expect(wrapper.text()).toContain('O Alienista')
    expect(wrapper.text()).toContain('Já na biblioteca')
    expect(wrapper.find('a.result__in-library').exists()).toBe(true)
    expect(wrapper.find('button[aria-label="Adicionar Dom Casmurro"]').exists()).toBe(false)
  })

  it('adiciona pela fonte externa e navega para o detalhe', async () => {
    api.books.search.mockResolvedValue(RESULTS)
    api.library.add.mockResolvedValue({
      id: 'item-9',
      book: { title: 'O Alienista' },
    } as LibraryItemDto)
    const { wrapper, router } = await setup()

    await wrapper.find('#search-input').setValue('alienista')
    await vi.advanceTimersByTimeAsync(400)
    await flushPromises()

    await wrapper.find('button[aria-label="Adicionar O Alienista"]').trigger('click')
    await flushPromises()
    const form = document.querySelector<HTMLFormElement>('#add-to-library')!
    expect(form).not.toBeNull()
    form.dispatchEvent(new Event('submit', { cancelable: true }))
    await flushPromises()

    expect(api.library.add).toHaveBeenCalledWith({
      source: { provider: 'GoogleBooks', externalId: 'gb1' },
      status: 'WantToRead',
      rating: null,
      review: null,
      startDate: null,
      finishedDate: null,
    })
    await flushPromises()
    expect(router.currentRoute.value.name).toBe('library-item')
    expect(router.currentRoute.value.params.id).toBe('item-9')
  })

  it('mostra estado específico quando as fontes externas estão indisponíveis (502)', async () => {
    api.books.search.mockRejectedValue(
      new ApiError({ status: 502, title: 'Bad Gateway', code: 'external.unavailable' }),
    )
    const { wrapper } = await setup()
    await wrapper.find('#search-input').setValue('qualquer')
    await vi.advanceTimersByTimeAsync(400)
    await flushPromises()

    expect(wrapper.text()).toContain('Fontes externas indisponíveis')
    expect(wrapper.text()).toContain('Cadastrar manualmente')
  })

  it('sem resultados oferece cadastro manual', async () => {
    api.books.search.mockResolvedValue([])
    const { wrapper } = await setup()
    await wrapper.find('#search-input').setValue('zzzz')
    await vi.advanceTimersByTimeAsync(400)
    await flushPromises()
    expect(wrapper.text()).toContain('Nenhum resultado')
  })
})
