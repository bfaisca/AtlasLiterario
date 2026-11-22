import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import type { CountryMapDatum } from '@/types/api'
import CountryList from '../CountryList.vue'

const countries: CountryMapDatum[] = [
  { iso: 'ARG', name: 'Argentina', continent: 'SouthAmerica', bookCount: 1, authorCount: 1 },
  { iso: 'BRA', name: 'Brasil', continent: 'SouthAmerica', bookCount: 7, authorCount: 4 },
  { iso: 'FRA', name: 'França', continent: 'Europe', bookCount: 3, authorCount: 2 },
]

describe('CountryList', () => {
  it('lista os países explorados por quantidade de livros', () => {
    const wrapper = mount(CountryList, { props: { countries, selectedIso: null } })
    expect(wrapper.findAll('.item__name').map((n) => n.text())).toEqual([
      'Brasil',
      'França',
      'Argentina',
    ])
    expect(wrapper.text()).toContain('7 livros · 4 autores')
  })

  it('seleciona por clique (botões nativos, operáveis por teclado) e marca o selecionado', async () => {
    const wrapper = mount(CountryList, { props: { countries, selectedIso: 'FRA' } })
    const buttons = wrapper.findAll('button')
    expect(buttons.find((b) => b.text().includes('França'))!.attributes('aria-pressed')).toBe(
      'true',
    )
    await buttons[0]!.trigger('click')
    expect(wrapper.emitted('select')![0]).toEqual(['BRA'])
  })

  it('mostra mensagem quando não há países explorados', () => {
    const wrapper = mount(CountryList, { props: { countries: [], selectedIso: null } })
    expect(wrapper.text()).toContain('Nenhum país explorado')
  })
})
