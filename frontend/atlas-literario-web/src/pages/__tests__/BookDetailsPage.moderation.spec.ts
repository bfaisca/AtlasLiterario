import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { defineComponent } from 'vue'
import { ApiError } from '@/api/errors'
import { useAuthStore } from '@/stores/auth'
import { useUiStore } from '@/stores/ui'
import type { GeoRelationDto, LibraryItemDto, UserRole } from '@/types/api'
import BookDetailsPage from '../BookDetailsPage.vue'

const api = vi.hoisted(() => ({
  library: { get: vi.fn() },
  books: { updateOriginCountries: vi.fn() },
  authors: { updateCountries: vi.fn() },
  countries: { list: vi.fn().mockResolvedValue([]) },
}))
vi.mock('@/api', () => ({ api }))

const Stub = defineComponent({ render: () => null })
const NOTE = 'Somente moderadores podem alterar países já informados.'

const relation = (iso: string): GeoRelationDto => ({
  countryIso: iso,
  countryName: iso,
  source: 'Wikidata',
  confidence: 0.9,
  isVerified: false,
})

function itemWith(filled: boolean): LibraryItemDto {
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
      title: 'Livro Países',
      subtitle: null,
      isbn13: null,
      isbn10: null,
      publisher: null,
      publicationYear: null,
      description: null,
      coverUrl: null,
      originalLanguage: null,
      genres: [],
      authors: [{ id: 'a1', name: 'Autora', nationalities: filled ? [relation('BRA')] : [] }],
      originCountries: filled ? [relation('BRA')] : [],
      geoEnrichmentStatus: 'Completed',
      externalIds: [],
    },
  } as LibraryItemDto
}

async function mountAs(role: UserRole, filled: boolean) {
  setActivePinia(createPinia())
  useAuthStore().user = {
    id: 'u',
    name: 'U',
    email: 'u@x.com',
    mapVisibility: 'Private',
    role,
    createdAt: '2025-01-01T00:00:00Z',
  }
  api.library.get.mockResolvedValue(itemWith(filled))
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/library', name: 'library', component: Stub }],
  })
  const wrapper = mount(BookDetailsPage, {
    props: { id: 'i1' },
    global: { plugins: [router] },
    attachTo: document.body,
  })
  await flushPromises()
  return wrapper
}
const labels = (wrapper: Awaited<ReturnType<typeof mountAs>>) =>
  wrapper
    .findAll('button')
    .map((b) => b.attributes('aria-label') ?? '')
    .filter((l) => /país|países/i.test(l))

describe('BookDetailsPage: quem pode alterar países', () => {
  beforeEach(() => {
    api.books.updateOriginCountries.mockReset()
    api.authors.updateCountries.mockReset()
  })
  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('usuário comum + conjuntos vazios ("Desconhecida"): vê "Informar país" (autor e obra), sem nota', async () => {
    const wrapper = await mountAs('User', false)
    expect(labels(wrapper)).toEqual(['Informar país de Autora', 'Informar país de origem da obra'])
    expect(wrapper.findAll('button').filter((b) => b.text() === 'Informar país')).toHaveLength(2)
    expect(wrapper.text()).not.toContain(NOTE)
    expect(wrapper.text()).toContain('Desconhecida')
  })

  it('usuário comum + preenchido: sem botão de edição e com a nota visível (texto, sem tooltip)', async () => {
    const wrapper = await mountAs('User', true)
    expect(labels(wrapper)).toEqual([])
    expect(
      wrapper.findAll('button').some((b) => /Editar países|Informar país/.test(b.text())),
    ).toBe(false)
    const note = wrapper.find('[data-testid="moderation-note"]')
    expect(note.text()).toBe(NOTE)
    expect(note.attributes('title')).toBeUndefined()
    expect(note.attributes('aria-hidden')).toBeUndefined()
  })

  it('moderador + preenchido: vê "Editar países" e não vê a nota', async () => {
    const wrapper = await mountAs('Moderator', true)
    expect(labels(wrapper)).toEqual(['Editar países de Autora', 'Editar países de origem da obra'])
    expect(wrapper.text()).not.toContain(NOTE)
  })

  it('moderador + vazio: também vê "Informar país"', async () => {
    const wrapper = await mountAs('Moderator', false)
    expect(labels(wrapper)).toEqual(['Informar país de Autora', 'Informar país de origem da obra'])
  })

  it('o seletor mantém o aviso de que vale para todos os leitores', async () => {
    const wrapper = await mountAs('User', false)
    await wrapper.find('button[aria-label="Informar país de origem da obra"]').trigger('click')
    await flushPromises()
    expect(document.querySelector('.geo-warning')!.textContent).toContain('todos os leitores')
  })

  it('403 geo.moderator_required: mensagem em português, fecha o seletor e refaz a leitura do item', async () => {
    api.books.updateOriginCountries.mockRejectedValue(
      new ApiError({ status: 403, title: 'x', code: 'geo.moderator_required' }),
    )
    const wrapper = await mountAs('User', false)
    const reads = api.library.get.mock.calls.length
    api.library.get.mockResolvedValue(itemWith(true)) // outra pessoa preencheu enquanto isso

    await wrapper.find('button[aria-label="Informar país de origem da obra"]').trigger('click')
    await flushPromises()
    const save = [...document.querySelectorAll<HTMLButtonElement>('.panel__footer button')].find(
      (b) => b.textContent?.includes('Salvar correção'),
    )!
    save.click()
    await flushPromises()

    expect(useUiStore().toasts.at(-1)).toMatchObject({ kind: 'error', message: NOTE })
    expect(document.querySelector('[role="dialog"]')).toBeNull()
    expect(api.library.get.mock.calls.length).toBeGreaterThan(reads)
    // a tela já reflete o dado preenchido: sem botão, com a nota
    expect(wrapper.find('[data-testid="moderation-note"]').exists()).toBe(true)
  })
})
