import { describe, expect, it } from 'vitest'
import { mount, RouterLinkStub } from '@vue/test-utils'
import BookCard from '../BookCard.vue'
import { createMockLibrary } from '@/mocks/data'

const library = createMockLibrary()
const find = (title: string) => library.find((i) => i.book.title === title)!

function render(title: string) {
  return mount(BookCard, {
    props: { item: find(title) },
    global: { stubs: { RouterLink: RouterLinkStub } },
  })
}

describe('BookCard', () => {
  it('mostra título, autores, status e origem', () => {
    const wrapper = render('Cem Anos de Solidão')
    expect(wrapper.text()).toContain('Cem Anos de Solidão')
    expect(wrapper.text()).toContain('Gabriel García Márquez')
    expect(wrapper.text()).toContain('Lido')
    expect(wrapper.text()).toContain('Colômbia')
    expect(wrapper.findComponent(RouterLinkStub).props('to')).toEqual({
      name: 'library-item',
      params: { id: find('Cem Anos de Solidão').id },
    })
  })

  it('sinaliza origem desconhecida em vez de omitir', () => {
    const wrapper = render('O Processo')
    expect(wrapper.text()).toContain('Origem desconhecida')
  })

  it('usa capa tipográfica quando não há imagem', () => {
    const wrapper = render('Dom Casmurro')
    expect(wrapper.find('img').exists()).toBe(false)
    expect(wrapper.find('.cover__fallback').attributes('aria-label')).toContain('Dom Casmurro')
  })
})
