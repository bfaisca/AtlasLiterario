import { afterEach, describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { defineComponent } from 'vue'
import { ApiError } from '@/api/errors'
import type { CountryDetailDto, CountryMapDatum } from '@/types/api'
import CountryPanel from '../CountryPanel.vue'

const Stub = defineComponent({ render: () => null })
const summary: CountryMapDatum = {
  iso: 'FRA',
  name: 'França',
  continent: 'Europe',
  bookCount: 2,
  authorCount: 1,
}
const detail: CountryDetailDto = {
  mapType: 'Authors',
  iso: 'FRA',
  name: 'França',
  continent: 'Europe',
  bookCount: 2,
  authorCount: 1,
  books: [
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
  ],
}

interface PanelProps {
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
    props: { summary, detail, loading: false, error: null, ...overrides },
    global: { plugins: [router] },
    attachTo: document.body,
  })
}

describe('CountryPanel', () => {
  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('mostra nome, continente, contagens e os livros com link para o item da biblioteca', () => {
    const wrapper = mountPanel()
    expect(wrapper.find('h2').text()).toBe('França')
    expect(wrapper.text()).toContain('Europa')
    expect(wrapper.text()).toContain('2 livros')
    expect(wrapper.text()).toContain('1 autor')
    const links = wrapper.findAll('a.book')
    expect(links.map((l) => l.attributes('href'))).toEqual(['/library/li-1', '/library/li-2'])
    expect(links[0]!.text()).toContain('O Estrangeiro')
    expect(links[0]!.text()).toContain('Albert Camus')
    expect(links[0]!.find('[aria-label^="Nota"]').attributes('aria-label')).toContain('5 de 5')
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

  it('mostra o resumo do mapa e o carregamento enquanto o detalhe não chega', () => {
    const wrapper = mountPanel({ detail: null, loading: true })
    expect(wrapper.find('h2').text()).toBe('França')
    expect(wrapper.text()).toContain('2 livros')
    expect(wrapper.find('[role="status"]').exists()).toBe(true)
    expect(wrapper.find('a.book').exists()).toBe(false)
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
