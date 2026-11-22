import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import type { CountryDto } from '@/types/api'
import { searchCountries } from '@/stores/countries'
import CountryPicker from '../CountryPicker.vue'

const COUNTRIES: CountryDto[] = [
  {
    iso: 'BRA',
    iso2: 'BR',
    name: 'Brazil',
    namePt: 'Brasil',
    continent: 'SouthAmerica',
    geoJsonId: 'BRA',
  },
  {
    iso: 'AUT',
    iso2: 'AT',
    name: 'Austria',
    namePt: 'Áustria',
    continent: 'Europe',
    geoJsonId: 'AUT',
  },
  {
    iso: 'CZE',
    iso2: 'CZ',
    name: 'Czechia',
    namePt: 'Tchéquia',
    continent: 'Europe',
    geoJsonId: 'CZE',
  },
  {
    iso: 'AUS',
    iso2: 'AU',
    name: 'Australia',
    namePt: 'Austrália',
    continent: 'Oceania',
    geoJsonId: 'AUS',
  },
]

const countriesApi = vi.hoisted(() => ({ list: vi.fn() }))
vi.mock('@/api', () => ({ api: { countries: countriesApi } }))

describe('searchCountries', () => {
  it('busca sem acentos pelo nome em português, priorizando prefixo', () => {
    expect(searchCountries(COUNTRIES, 'aust').map((c) => c.iso)).toEqual(['AUS', 'AUT'])
    expect(searchCountries(COUNTRIES, 'tcheq').map((c) => c.iso)).toEqual(['CZE'])
  })

  it('aceita nome em inglês e código ISO', () => {
    expect(searchCountries(COUNTRIES, 'czech').map((c) => c.iso)).toEqual(['CZE'])
    expect(searchCountries(COUNTRIES, 'bra').map((c) => c.iso)).toEqual(['BRA'])
    expect(searchCountries(COUNTRIES, '')).toEqual([])
  })
})

describe('CountryPicker', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    countriesApi.list.mockReset().mockResolvedValue(COUNTRIES)
  })

  async function render(modelValue: string[] = []) {
    const wrapper = mount(CountryPicker, {
      props: {
        label: 'Países de origem',
        modelValue,
        'onUpdate:modelValue': (value: string[]) => wrapper.setProps({ modelValue: value }),
      },
    })
    await flushPromises()
    return wrapper
  }

  it('carrega os países uma vez e mostra estado "desconhecido" sem seleção', async () => {
    const wrapper = await render()
    expect(countriesApi.list).toHaveBeenCalledTimes(1)
    expect(wrapper.text()).toContain('fica como desconhecido')
  })

  it('filtra pelo nome e seleciona com clique', async () => {
    const wrapper = await render()
    await wrapper.find('input').setValue('áus')
    const options = wrapper.findAll('[role="option"]')
    expect(options.map((o) => o.text())).toEqual([
      expect.stringContaining('Austrália'),
      expect.stringContaining('Áustria'),
    ])

    await options[1]!.trigger('mousedown')
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([['AUT']])
    expect(wrapper.find('.picker__chip').text()).toContain('Áustria')
  })

  it('teclado: setas + Enter selecionam sem submeter; chip remove', async () => {
    const wrapper = await render(['BRA'])
    const input = wrapper.find('input')
    await input.setValue('a')
    await input.trigger('keydown', { key: 'ArrowDown' })
    await input.trigger('keydown', { key: 'Enter' })
    const selected = wrapper.emitted('update:modelValue')?.at(-1)?.[0] as string[]
    expect(selected).toHaveLength(2)
    expect(selected[0]).toBe('BRA')

    await wrapper.find('button[aria-label="Remover Brasil"]').trigger('click')
    expect(wrapper.emitted('update:modelValue')?.at(-1)?.[0]).not.toContain('BRA')
  })
})
