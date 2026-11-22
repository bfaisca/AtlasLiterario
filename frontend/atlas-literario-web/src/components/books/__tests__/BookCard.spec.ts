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

  it('sinaliza obra sem país (desconhecido) em vez de omitir', () => {
    const wrapper = render('O Processo')
    const gaps = wrapper.find('.book-card__gaps').text()
    expect(gaps).toContain('Obra sem país')
    expect(gaps).not.toContain('Autor sem país') // Kafka tem nacionalidade conhecida
  })

  it('sinaliza autor sem país quando nenhum autor tem nacionalidade', () => {
    const wrapper = render('Cartas de um Farol (demo)')
    const gaps = wrapper.find('.book-card__gaps').text()
    expect(gaps).toContain('Autor sem país')
    expect(gaps).not.toContain('Obra sem país')
    expect(wrapper.text()).toContain('Noruega')
  })

  it('sem autores cadastrados e sem origem mostra os dois avisos', () => {
    const wrapper = render('As Mil e Uma Noites')
    const gaps = wrapper.findAll('.book-card__gap').map((g) => g.text())
    expect(gaps).toEqual(['Obra sem país', 'Autor sem país'])
  })

  it('cartão completo não mostra avisos', () => {
    const wrapper = render('Cem Anos de Solidão')
    expect(wrapper.find('.book-card__gaps').exists()).toBe(false)
  })

  it('usa capa tipográfica quando não há imagem', () => {
    const wrapper = render('Dom Casmurro')
    expect(wrapper.find('img').exists()).toBe(false)
    expect(wrapper.find('.cover__fallback').attributes('aria-label')).toContain('Dom Casmurro')
  })
})
