import { afterEach, describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { defineComponent } from 'vue'
import { ApiError } from '@/api/errors'
import type { CountryDetailDto, CountryMapDatum, MapType } from '@/types/api'
import CountryPanel from '../CountryPanel.vue'

const Stub = defineComponent({ render: () => null })
const summary: CountryMapDatum = {
  iso: 'FRA',
  name: 'França',
  continent: 'Europe',
  bookCount: 2,
  authorCount: 1,
}
const books: CountryDetailDto['books'] = [
  {
    libraryItemId: 'li-1',
    bookId: 'b-1',
    title: 'O Estrangeiro',
    coverUrl: null,
    authors: ['Albert Camus'],
    finishedDate: '2024-03-10',
    rating: 5,
  },
  {
    libraryItemId: 'li-2',
    bookId: 'b-2',
    title: 'A Peste',
    coverUrl: null,
    authors: ['Albert Camus'],
    finishedDate: null,
    rating: null,
  },
]
/** Mapa de origens: `authors` é sempre [] (contrato v1.10). */
const originsDetail: CountryDetailDto = {
  mapType: 'Origins',
  status: 'Read',
  iso: 'FRA',
  name: 'França',
  continent: 'Europe',
  bookCount: 2,
  authorCount: 1,
  books,
  authors: [],
}
/** Mapa de autores: `books` do painel é ignorado; cada autor traz os próprios livros (v1.11). */
const authorsDetail: CountryDetailDto = {
  ...originsDetail,
  mapType: 'Authors',
  authorCount: 2,
  authors: [
    {
      authorId: 'a-1',
      name: 'Albert Camus',
      countryIso: 'FRA',
      countryName: 'França',
      books: books,
    },
    {
      authorId: 'a-2',
      name: 'Marguerite Duras',
      countryIso: 'FRA',
      countryName: 'França',
      books: [
        {
          libraryItemId: 'li-3',
          bookId: 'b-3',
          title: 'O Amante',
          coverUrl: null,
          authors: ['Marguerite Duras'],
          finishedDate: '2023-01-05',
          rating: 4,
        },
      ],
    },
    {
      authorId: 'a-3',
      name: 'Voltaire',
      countryIso: 'FRA',
      countryName: 'França',
      books: [
        {
          libraryItemId: 'li-4',
          bookId: 'b-4',
          title: 'Cândido',
          coverUrl: null,
          authors: ['Voltaire'],
          finishedDate: null,
          rating: null,
        },
      ],
    },
  ],
}

interface PanelProps {
  mapType: MapType
  summary: CountryMapDatum | null
  detail: CountryDetailDto | null
  loading: boolean
  error: ApiError | null
}

function mountPanel(overrides: Partial<PanelProps> = {}) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: Stub },
      { path: '/library/:id', name: 'library-item', component: Stub },
    ],
  })
  return mount(CountryPanel, {
    props: {
      mapType: 'Origins',
      summary,
      detail: originsDetail,
      loading: false,
      error: null,
      ...overrides,
    },
    global: { plugins: [router] },
    attachTo: document.body,
  })
}

describe('CountryPanel — mapa de origens', () => {
  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('mostra nome, continente, "N livros" e os livros com link para o item da biblioteca', () => {
    const wrapper = mountPanel()
    expect(wrapper.find('h2').text()).toBe('França')
    expect(wrapper.text()).toContain('Europa')
    expect(wrapper.find('[data-testid="panel-count"]').text()).toBe('2 livros')
    expect(wrapper.text()).not.toContain('autor')
    const links = wrapper.findAll('a.book')
    expect(links.map((l) => l.attributes('href'))).toEqual(['/library/li-1', '/library/li-2'])
    expect(links[0]!.text()).toContain('O Estrangeiro')
    expect(links[0]!.text()).toContain('Albert Camus')
    expect(links[0]!.find('[aria-label^="Nota"]').attributes('aria-label')).toContain('5 de 5')
    expect(wrapper.find('[data-testid="author-card"]').exists()).toBe(false)
  })

  it('estado vazio de livros', () => {
    const wrapper = mountPanel({ detail: { ...originsDetail, bookCount: 0, books: [] } })
    expect(wrapper.text()).toContain('Nenhum livro neste país com o filtro atual.')
    expect(wrapper.find('ul').exists()).toBe(false)
  })

  it('mostra o resumo do mapa e o carregamento enquanto o detalhe não chega', () => {
    const wrapper = mountPanel({ detail: null, loading: true })
    expect(wrapper.find('h2').text()).toBe('França')
    expect(wrapper.find('[data-testid="panel-count"]').text()).toBe('2 livros')
    expect(wrapper.find('[role="status"]').exists()).toBe(true)
    expect(wrapper.find('a.book').exists()).toBe(false)
  })
})

describe('CountryPanel — mapa de autores', () => {
  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('cabeçalho "N autores" e uma lista semântica de cards a partir de detail.authors', () => {
    const wrapper = mountPanel({ mapType: 'Authors', detail: authorsDetail })
    expect(wrapper.find('[data-testid="panel-count"]').text()).toBe('3 autores')
    const list = wrapper.find('ul[aria-label="Autores"]')
    expect(list.exists()).toBe(true)
    const cards = wrapper.findAll('[data-testid="author-card"]')
    expect(cards).toHaveLength(3)
    expect(list.findAll(':scope > li')).toHaveLength(3)
    expect(cards[0]!.text()).toContain('Albert Camus')
    expect(cards[0]!.text()).toContain('França')
    // iniciais decorativas ficam fora da árvore de acessibilidade
    expect(cards[1]!.find('[aria-hidden="true"]').text()).toBe('MD')
  })

  it('a contagem vem de authors, não de books nem de authorCount do detalhe', () => {
    const wrapper = mountPanel({
      mapType: 'Authors',
      detail: { ...authorsDetail, authorCount: 99, bookCount: 42 },
    })
    expect(wrapper.find('[data-testid="panel-count"]').text()).toBe('3 autores')
  })

  it('livros dos autores ficam recolhidos; o `books` do painel não é exibido', () => {
    const wrapper = mountPanel({ mapType: 'Authors', detail: authorsDetail })
    for (const region of wrapper.findAll('[role="region"]')) {
      expect((region.element as HTMLElement).style.display).toBe('none')
    }
    expect(wrapper.find('ul.books').exists()).toBe(false)
  })

  it('cada card tem um botão com aria-expanded/aria-controls e "País · N livros"', () => {
    const wrapper = mountPanel({ mapType: 'Authors', detail: authorsDetail })
    const cards = wrapper.findAll('[data-testid="author-card"]')
    const toggle = cards[0]!.find('button')
    expect(toggle.attributes('aria-expanded')).toBe('false')
    const regionId = toggle.attributes('aria-controls')!
    expect(cards[0]!.find(`#${regionId}`).exists()).toBe(true)
    expect(cards[0]!.text()).toContain('França · 2 livros')
    expect(cards[1]!.text()).toContain('França · 1 livro')
  })

  it('clicar expande/recolhe e mostra só os livros do autor, com link', async () => {
    const wrapper = mountPanel({ mapType: 'Authors', detail: authorsDetail })
    const cards = wrapper.findAll('[data-testid="author-card"]')
    const toggle = cards[1]!.find('button')
    await toggle.trigger('click')
    expect(toggle.attributes('aria-expanded')).toBe('true')
    const links = cards[1]!.findAll('a.book')
    expect(links.map((l) => l.attributes('href'))).toEqual(['/library/li-3'])
    expect(links[0]!.text()).toContain('O Amante')
    expect(links[0]!.find('[aria-label^="Nota"]').attributes('aria-label')).toContain('4 de 5')
    expect(cards[1]!.text()).not.toContain('O Estrangeiro')
    // os outros continuam fechados
    expect(cards[0]!.find('button').attributes('aria-expanded')).toBe('false')
    await toggle.trigger('click')
    expect(toggle.attributes('aria-expanded')).toBe('false')
  })

  it('o toggle é um <button> nativo (Enter/Espaço = click no navegador); vários cards podem ficar abertos', async () => {
    const wrapper = mountPanel({ mapType: 'Authors', detail: authorsDetail })
    const toggles = wrapper.findAll('[data-testid="author-card-toggle"]')
    expect(toggles[0]!.element.tagName).toBe('BUTTON')
    expect(toggles[0]!.attributes('type')).toBe('button')
    // jsdom não converte Enter/Espaço em click; o botão nativo faz isso no navegador.
    await toggles[0]!.trigger('click')
    await toggles[2]!.trigger('click')
    expect(toggles.map((t) => t.attributes('aria-expanded'))).toEqual(['true', 'false', 'true'])
  })

  it('trocar de país recolhe todos os cards', async () => {
    const wrapper = mountPanel({ mapType: 'Authors', detail: authorsDetail })
    await wrapper.find('[data-testid="author-card-toggle"]').trigger('click')
    expect(wrapper.find('[data-testid="author-card-toggle"]').attributes('aria-expanded')).toBe(
      'true',
    )
    await wrapper.setProps({
      detail: { ...authorsDetail, iso: 'BRA', name: 'Brasil' },
      summary: { ...summary, iso: 'BRA', name: 'Brasil' },
    })
    expect(wrapper.find('[data-testid="author-card-toggle"]').attributes('aria-expanded')).toBe(
      'false',
    )
  })

  it('estado vazio: "Nenhum autor neste país com o filtro atual."', () => {
    const wrapper = mountPanel({
      mapType: 'Authors',
      detail: { ...authorsDetail, authorCount: 0, authors: [] },
    })
    expect(wrapper.text()).toContain('Nenhum autor neste país com o filtro atual.')
    expect(wrapper.find('[data-testid="panel-count"]').text()).toBe('0 autores')
    expect(wrapper.find('[data-testid="author-card"]').exists()).toBe(false)
  })

  it('enquanto carrega, usa authorCount do resumo no cabeçalho', () => {
    const wrapper = mountPanel({ mapType: 'Authors', detail: null, loading: true })
    expect(wrapper.find('[data-testid="panel-count"]').text()).toBe('1 autor')
    expect(wrapper.find('[role="status"]').exists()).toBe(true)
  })
})

describe('CountryPanel — comportamento comum', () => {
  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('é um diálogo não modal nomeado pelo título e move o foco para ele ao abrir', async () => {
    const wrapper = mountPanel()
    await wrapper.vm.$nextTick()
    await wrapper.vm.$nextTick()
    const dialog = wrapper.find('[role="dialog"]')
    expect(dialog.attributes('aria-modal')).toBe('false')
    expect(dialog.attributes('aria-labelledby')).toBe('country-panel-title')
    expect(document.activeElement?.id).toBe('country-panel-title')
  })

  it('mostra erro traduzido com "Tentar novamente"', async () => {
    const wrapper = mountPanel({
      detail: null,
      error: new ApiError({ status: 501, title: 'x', code: 'feature.not_implemented' }),
    })
    expect(wrapper.find('[role="alert"]').exists()).toBe(true)
    const retry = wrapper.findAll('button').find((b) => b.text() === 'Tentar novamente')!
    await retry.trigger('click')
    expect(wrapper.emitted('retry')).toHaveLength(1)
  })

  it('fecha com Esc, com o botão e ao clicar fora (exceto em [data-keep-panel])', async () => {
    document.body.insertAdjacentHTML(
      'beforeend',
      '<main id="outside">x</main><div data-keep-panel><canvas id="map"></canvas></div>',
    )
    const wrapper = mountPanel()
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    expect(wrapper.emitted('close')).toHaveLength(1)

    await wrapper.find('button[aria-label="Fechar painel"]').trigger('click')
    expect(wrapper.emitted('close')).toHaveLength(2)

    document.getElementById('map')!.dispatchEvent(new Event('pointerdown', { bubbles: true }))
    expect(wrapper.emitted('close')).toHaveLength(2)
    wrapper.find('h2').element.dispatchEvent(new Event('pointerdown', { bubbles: true }))
    expect(wrapper.emitted('close')).toHaveLength(2)
    document.getElementById('outside')!.dispatchEvent(new Event('pointerdown', { bubbles: true }))
    expect(wrapper.emitted('close')).toHaveLength(3)
  })

  it('devolve o foco ao elemento anterior ao desmontar', async () => {
    const trigger = document.createElement('button')
    document.body.append(trigger)
    trigger.focus()
    const wrapper = mountPanel()
    await wrapper.vm.$nextTick()
    await wrapper.vm.$nextTick()
    expect(document.activeElement).not.toBe(trigger)
    wrapper.unmount()
    expect(document.activeElement).toBe(trigger)
  })
})
