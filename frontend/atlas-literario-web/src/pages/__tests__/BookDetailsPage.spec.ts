import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { defineComponent } from 'vue'
import type { GeoEnrichmentStatus, LibraryItemDto } from '@/types/api'
import BookDetailsPage from '../BookDetailsPage.vue'

const api = vi.hoisted(() => ({
  library: { get: vi.fn() },
  books: { enrich: vi.fn() },
  authors: {},
  countries: { list: vi.fn().mockResolvedValue([]) },
}))
vi.mock('@/api', () => ({ api }))

const Stub = defineComponent({ render: () => null })

function itemWith(status: GeoEnrichmentStatus): LibraryItemDto {
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
      title: 'Norwegian Wood',
      subtitle: null,
      isbn13: null,
      isbn10: null,
      publisher: null,
      publicationYear: 1987,
      description: null,
      coverUrl: null,
      originalLanguage: null,
      genres: [],
      authors: [{ id: 'a1', name: 'Haruki Murakami', nationalities: [] }],
      originCountries: [],
      geoEnrichmentStatus: status,
      externalIds: [],
    },
  } as LibraryItemDto
}

async function mountWith(status: GeoEnrichmentStatus) {
  api.library.get.mockResolvedValue(itemWith(status))
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/library', name: 'library', component: Stub }],
  })
  const wrapper = mount(BookDetailsPage, { props: { id: 'i1' }, global: { plugins: [router] } })
  await flushPromises()
  return wrapper
}

describe('BookDetailsPage: dados geográficos', () => {
  beforeEach(() => setActivePinia(createPinia()))
  afterEach(() => {
    document.body.innerHTML = ''
  })

  it.each(['Pending', 'Partial', 'Failed'] as const)(
    'status %s: mostra o aviso e o botão "Buscar dados novamente"',
    async (status) => {
      const wrapper = await mountWith(status)
      const button = wrapper
        .findAll('button')
        .find((b) => b.text().includes('Buscar dados novamente'))
      expect(button).toBeDefined()
      if (status !== 'Failed') {
        expect(wrapper.text()).toContain('Alguns dados geográficos ainda não foram encontrados')
      }
    },
  )

  it('status Completed: sem aviso nem botão', async () => {
    const wrapper = await mountWith('Completed')
    expect(wrapper.text()).not.toContain('Buscar dados novamente')
  })

  it('dado ausente aparece como "Desconhecida" (autor e origem)', async () => {
    const wrapper = await mountWith('Partial')
    expect(wrapper.text().match(/Desconhecida/g)!.length).toBeGreaterThanOrEqual(2)
  })
})
