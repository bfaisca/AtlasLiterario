import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, RouterLinkStub } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { defineComponent } from 'vue'
import type { LibraryItemDto, PagedResult } from '@/types/api'
import { createMockLibrary } from '@/mocks/data'
import { useAuthStore } from '@/stores/auth'
import LibraryPage from '../LibraryPage.vue'

const libraryApi = vi.hoisted(() => ({ list: vi.fn() }))
vi.mock('@/api', () => ({ api: { library: libraryApi } }))

const Stub = defineComponent({ render: () => null })
const library = createMockLibrary()
const paged = (items: LibraryItemDto[]): PagedResult<LibraryItemDto> => ({
  items,
  page: 1,
  pageSize: 24,
  totalCount: items.length,
  totalPages: 1,
})

async function setup(url: string) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/library', name: 'library', component: LibraryPage },
      { path: '/library/:id', name: 'library-item', component: Stub },
      { path: '/search', name: 'search', component: Stub },
    ],
  })
  await router.push(url)
  const wrapper = mount(LibraryPage, {
    global: { plugins: [router], stubs: { RouterLink: RouterLinkStub } },
    attachTo: document.body,
  })
  await flushPromises()
  return { wrapper, router }
}

describe('LibraryPage: filtro de dados geográficos', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    libraryApi.list.mockReset()
    libraryApi.list.mockResolvedValue(paged(library))
  })
  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('oferece as cinco opções, com "Todos os livros" como padrão', async () => {
    const { wrapper } = await setup('/library')
    const select = wrapper.find('[data-testid="geo-missing-select"]')
    expect(select.findAll('option').map((o) => o.text())).toEqual([
      'Todos os livros',
      'Falta algum país',
      'Sem país da obra',
      'Sem país do autor',
      'Sem nenhum dos dois',
    ])
    expect((select.element as HTMLSelectElement).value).toBe('')
    expect(libraryApi.list.mock.calls[0]![0]).not.toHaveProperty('geoMissing')
  })

  it('escolher uma opção grava na URL e consulta a API com geoMissing (página 1)', async () => {
    const { wrapper, router } = await setup('/library')
    await wrapper.find('[data-testid="geo-missing-select"]').setValue('origin')
    await flushPromises()
    expect(router.currentRoute.value.query.geoMissing).toBe('origin')
    expect(libraryApi.list).toHaveBeenLastCalledWith(
      expect.objectContaining({ geoMissing: 'origin', page: 1 }),
    )
    await wrapper.find('[data-testid="geo-missing-select"]').setValue('')
    await flushPromises()
    expect(router.currentRoute.value.query.geoMissing).toBeUndefined()
  })

  it('lê ?geoMissing= da URL e preserva os demais filtros', async () => {
    const { wrapper } = await setup('/library?geoMissing=both&status=Read')
    expect(
      (wrapper.find('[data-testid="geo-missing-select"]').element as HTMLSelectElement).value,
    ).toBe('both')
    expect(libraryApi.list).toHaveBeenLastCalledWith(
      expect.objectContaining({ geoMissing: 'both', status: ['Read'] }),
    )
  })

  it('lista vazia só com esse filtro mostra mensagem própria e permite voltar a todos', async () => {
    libraryApi.list.mockResolvedValue(paged([]))
    const { wrapper, router } = await setup('/library?geoMissing=any')
    expect(wrapper.text()).toContain('Nenhum livro com dados geográficos faltando.')
    const back = wrapper.findAll('button').find((b) => b.text() === 'Ver todos os livros')!
    await back.trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.query.geoMissing).toBeUndefined()
  })

  it('com outro filtro junto, a lista vazia usa a mensagem genérica', async () => {
    libraryApi.list.mockResolvedValue(paged([]))
    const { wrapper } = await setup('/library?geoMissing=any&status=Reading')
    expect(wrapper.text()).toContain('Nenhum livro com esses filtros')
  })

  it('os cartões listados mostram os avisos de obra/autor sem país', async () => {
    const missing = library.filter((i) => i.book.title === 'As Mil e Uma Noites')
    libraryApi.list.mockResolvedValue(paged(missing))
    const { wrapper } = await setup('/library?geoMissing=both')
    expect(wrapper.text()).toContain('Obra sem país')
    expect(wrapper.text()).toContain('Autor sem país')
  })

  it('troca de conta: a página não reutiliza os livros do usuário anterior', async () => {
    const { wrapper } = await setup('/library')
    const first = library[0]!
    expect(wrapper.text()).toContain(first.book.title)

    useAuthStore().clearSession()
    libraryApi.list.mockResolvedValue(paged([]))
    await flushPromises()
    expect(wrapper.text()).not.toContain(first.book.title)

    // nova montagem (nova sessão) busca no servidor em vez de reutilizar itens antigos
    const calls = libraryApi.list.mock.calls.length
    await setup('/library')
    expect(libraryApi.list.mock.calls.length).toBeGreaterThan(calls)
  })
})
