/* eslint-disable vue/one-component-per-file */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { defineComponent, h } from 'vue'
import type { CountryDetailDto, MapDataDto, MapType } from '@/types/api'
import MapPage from '../MapPage.vue'

const mapsApi = vi.hoisted(() => ({ get: vi.fn(), getCountryDetail: vi.fn() }))
vi.mock('@/api', () => ({ api: { maps: mapsApi } }))

const Stub = defineComponent({ render: () => null })
/** O mapa real (WebGL) não roda no jsdom: o stub só expõe os eventos do contrato. */
const WorldMapStub = defineComponent({
  props: {
    data: { type: Array, default: () => [] },
    selectedIso: { type: String, default: null },
    maxBookCount: { type: Number, default: 0 },
  },
  emits: ['country-select', 'country-hover'],
  setup(props, { emit }) {
    return () =>
      h('div', { 'data-testid': 'world-map', 'data-selected': props.selectedIso ?? '' }, [
        h('button', { id: 'tap-fra', onClick: () => emit('country-select', 'FRA') }),
        h('button', { id: 'tap-ocean', onClick: () => emit('country-select', null) }),
        h('button', { id: 'tap-unexplored', onClick: () => emit('country-select', 'ATA') }),
      ])
  },
})

function country(iso: string, name: string, bookCount: number) {
  return { iso, name, continent: 'Europe' as const, bookCount, authorCount: 1 }
}
const authors: MapDataDto = {
  mapType: 'Authors',
  totalReadBooks: 5,
  exploredCountries: 2,
  maxBookCount: 3,
  unknownBookCount: 2,
  countries: [country('FRA', 'França', 3), country('BRA', 'Brasil', 2)],
}
const origins: MapDataDto = {
  mapType: 'Origins',
  totalReadBooks: 5,
  exploredCountries: 1,
  maxBookCount: 2,
  unknownBookCount: 0,
  countries: [country('BRA', 'Brasil', 2)],
}
const detailFor = (mapType: MapType, iso: string): CountryDetailDto => ({
  mapType,
  iso,
  name: iso,
  continent: 'Europe',
  bookCount: 1,
  authorCount: 1,
  books: [],
})

async function setup(url: string) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/map/:type(authors|origins)?', name: 'map', component: MapPage },
      { path: '/library', name: 'library', component: Stub },
      { path: '/search', name: 'search', component: Stub },
      { path: '/library/:id', name: 'library-item', component: Stub },
    ],
  })
  await router.push(url)
  const wrapper = mount(MapPage, {
    global: { plugins: [router], stubs: { WorldMap: WorldMapStub } },
    attachTo: document.body,
  })
  await flushPromises()
  return { wrapper, router }
}

describe('MapPage (URL e seleção)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    mapsApi.get.mockReset()
    mapsApi.getCountryDetail.mockReset()
    mapsApi.get.mockImplementation(async (type: MapType) =>
      type === 'Authors' ? authors : origins,
    )
    mapsApi.getCountryDetail.mockImplementation(async (type: MapType, iso: string) =>
      detailFor(type, iso),
    )
  })
  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('abre o painel direto quando ?country= é um país explorado do mapa', async () => {
    const { wrapper } = await setup('/map/authors?country=fra')
    expect(wrapper.find('[data-testid="country-panel"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="world-map"]').attributes('data-selected')).toBe('FRA')
    expect(mapsApi.getCountryDetail).toHaveBeenCalledWith('Authors', 'FRA')
  })

  it('descarta ?country= inválido ou sem livros neste mapa', async () => {
    const { wrapper, router } = await setup('/map/authors?country=XXX')
    expect(wrapper.find('[data-testid="country-panel"]').exists()).toBe(false)
    expect(router.currentRoute.value.query.country).toBeUndefined()
    expect(mapsApi.getCountryDetail).not.toHaveBeenCalled()
  })

  it('clicar no mapa grava o país na URL; clicar de novo, no oceano ou em país inexplorado limpa', async () => {
    const { wrapper, router } = await setup('/map/authors')
    await wrapper.find('#tap-fra').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.query.country).toBe('FRA')
    expect(wrapper.find('[data-testid="country-panel"]').exists()).toBe(true)

    await wrapper.find('#tap-fra').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.query.country).toBeUndefined()

    await wrapper.find('#tap-fra').trigger('click')
    await wrapper.find('#tap-ocean').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.query.country).toBeUndefined()

    await wrapper.find('#tap-fra').trigger('click')
    await wrapper.find('#tap-unexplored').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.query.country).toBeUndefined()
  })

  it('selecionar pela lista alternativa abre o painel', async () => {
    const { wrapper, router } = await setup('/map/authors')
    const item = wrapper.findAll('.item').find((b) => b.text().includes('Brasil'))!
    await item.trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.query.country).toBe('BRA')
    expect(wrapper.find('[data-testid="country-panel"]').exists()).toBe(true)
  })

  it('Esc fecha o painel e remove o país da URL', async () => {
    const { wrapper, router } = await setup('/map/authors?country=FRA')
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    await flushPromises()
    expect(router.currentRoute.value.query.country).toBeUndefined()
    expect(wrapper.find('[data-testid="country-panel"]').exists()).toBe(false)
  })

  it('alternar o mapa mantém o país quando existe nos dois e o descarta quando não existe', async () => {
    const { wrapper, router } = await setup('/map/authors?country=BRA')
    await router.push({ name: 'map', params: { type: 'origins' }, query: { country: 'BRA' } })
    await flushPromises()
    expect(router.currentRoute.value.query.country).toBe('BRA')
    expect(wrapper.find('[data-testid="country-panel"]').exists()).toBe(true)
    expect(mapsApi.getCountryDetail).toHaveBeenLastCalledWith('Origins', 'BRA')

    await router.push({ name: 'map', params: { type: 'authors' }, query: { country: 'FRA' } })
    await flushPromises()
    expect(router.currentRoute.value.query.country).toBe('FRA') // existe em Autores

    await router.push({ name: 'map', params: { type: 'origins' }, query: { country: 'FRA' } })
    await flushPromises()
    expect(router.currentRoute.value.query.country).toBeUndefined() // FRA não está em Origens
    expect(wrapper.find('[data-testid="country-panel"]').exists()).toBe(false)
  })

  it('clicar no seletor Autores/Obras não fecha o painel (está em [data-keep-panel])', async () => {
    const { wrapper, router } = await setup('/map/authors?country=BRA')
    const radio = wrapper.findAll('[role="radio"]').find((r) => r.text() === 'Obras')!
    radio.element.dispatchEvent(new Event('pointerdown', { bubbles: true }))
    await radio.trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.params.type).toBe('origins')
    expect(router.currentRoute.value.query.country).toBe('BRA')
    expect(wrapper.find('[data-testid="country-panel"]').exists()).toBe(true)
  })

  it('o painel mostra erro com retry quando o detalhe falha', async () => {
    mapsApi.getCountryDetail.mockRejectedValueOnce({
      isAxiosError: true,
      response: { status: 500 },
    })
    const { wrapper } = await setup('/map/authors?country=FRA')
    expect(wrapper.find('[role="alert"]').exists()).toBe(true)
    const retry = wrapper.findAll('button').find((b) => b.text() === 'Tentar novamente')!
    await retry.trigger('click')
    await flushPromises()
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
    expect(mapsApi.getCountryDetail).toHaveBeenCalledTimes(2)
  })

  it('explica livros sem país conhecido e liga à biblioteca (Read)', async () => {
    const { wrapper } = await setup('/map/authors')
    const note = wrapper.find('[role="note"]')
    expect(note.text()).toContain('2 livros lidos')
    expect(note.text()).toContain('sem nacionalidade conhecida')
    expect(note.text()).toContain('Dado ausente não é país inexplorado')
    expect(note.find('a').attributes('href')).toBe('/library?status=Read')
  })

  it('sem livros lidos: mostra o estado vazio com CTA para a biblioteca', async () => {
    mapsApi.get.mockResolvedValue({
      ...authors,
      totalReadBooks: 0,
      exploredCountries: 0,
      countries: [],
      maxBookCount: 0,
      unknownBookCount: 0,
    })
    const { wrapper } = await setup('/map/authors')
    expect(wrapper.text()).toContain('Nenhum livro lido ainda')
    const cta = wrapper.findAll('a').find((a) => a.text() === 'Ir para a biblioteca')!
    expect(cta.attributes('href')).toBe('/library')
    expect(wrapper.find('[role="note"]').exists()).toBe(false)
  })

  it('mostra a atribuição Natural Earth e as fontes de dados', async () => {
    const { wrapper } = await setup('/map/authors')
    const credits = wrapper.find('.map-page__credits')
    expect(credits.text()).toContain('Natural Earth')
    expect(credits.text()).toContain('Wikidata')
    expect(credits.find('a').attributes('rel')).toContain('noopener')
  })

  it('a legenda exibe o máximo do mapa atual', async () => {
    const { wrapper } = await setup('/map/authors')
    expect(wrapper.find('.legend__max').text()).toBe('máx. 3')
    expect(wrapper.findAll('[data-testid="legend-tick"]').map((t) => t.text())).toEqual([
      '1',
      '2',
      '3',
    ])
  })
})
