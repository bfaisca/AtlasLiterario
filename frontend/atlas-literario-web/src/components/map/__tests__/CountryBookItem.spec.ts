import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { defineComponent } from 'vue'
import type { CountryBookDto, ProfileBookDto } from '@/types/api'
import CountryBookItem from '../CountryBookItem.vue'

const Stub = defineComponent({ render: () => null })
const router = createRouter({
  history: createMemoryHistory(),
  routes: [{ path: '/library/:id', name: 'library-item', component: Stub }],
})

const own: CountryBookDto = {
  libraryItemId: 'item-1',
  bookId: 'b1',
  title: 'Cem Anos de Solidão',
  coverUrl: null,
  authors: ['Gabriel García Márquez'],
  finishedDate: '2025-01-18',
  rating: 5,
}
const theirs: ProfileBookDto = {
  bookId: 'b1',
  title: 'Cem Anos de Solidão',
  coverUrl: null,
  authors: ['Gabriel García Márquez'],
  finishedDate: '2025-01-18',
  rating: 5,
  review: 'Macondo ficou morando em mim.',
}
const mountItem = (book: CountryBookDto | ProfileBookDto) =>
  mount(CountryBookItem, { props: { book }, global: { plugins: [router] } })

describe('CountryBookItem', () => {
  it('livro da própria biblioteca: link para o item e sem resenha expansível', () => {
    const wrapper = mountItem(own)
    expect(wrapper.find('a').attributes('href')).toBe('/library/item-1')
    expect(wrapper.find('[data-testid="review-toggle"]').exists()).toBe(false)
  })

  it('livro de perfil público: sem link de biblioteca; título, autoria, data e nota visíveis', () => {
    const wrapper = mountItem(theirs)
    expect(wrapper.find('a').exists()).toBe(false)
    expect(wrapper.html()).not.toContain('/library/')
    expect(wrapper.text()).toContain('Cem Anos de Solidão')
    expect(wrapper.text()).toContain('Gabriel García Márquez')
    expect(wrapper.text()).toContain('Lido em')
    expect(wrapper.find('[role="img"], [aria-label*="Nota"]').exists()).toBe(true)
  })

  it('resenha: botão "Ler resenha" expande/recolhe com aria-expanded', async () => {
    const wrapper = mountItem(theirs)
    const toggle = wrapper.find('[data-testid="review-toggle"]')
    const text = wrapper.find('[data-testid="review-text"]')
    expect(toggle.text()).toBe('Ler resenha')
    expect(toggle.attributes('aria-expanded')).toBe('false')
    expect(text.attributes('style')).toContain('display: none')
    expect(toggle.attributes('aria-controls')).toBe(text.attributes('id'))

    await toggle.trigger('click')

    expect(toggle.attributes('aria-expanded')).toBe('true')
    expect(toggle.text()).toBe('Ocultar resenha')
    expect(text.attributes('style') ?? '').not.toContain('display: none')
    expect(text.text()).toBe('Macondo ficou morando em mim.')
  })

  it('sem resenha (null ou só espaços) não há botão', () => {
    expect(
      mountItem({ ...theirs, review: null })
        .find('[data-testid="review-toggle"]')
        .exists(),
    ).toBe(false)
    expect(
      mountItem({ ...theirs, review: '   ' })
        .find('[data-testid="review-toggle"]')
        .exists(),
    ).toBe(false)
  })

  it('a resenha é renderizada como texto (HTML não é interpretado)', async () => {
    const wrapper = mountItem({ ...theirs, review: '<img src=x onerror=alert(1)> oi' })
    await wrapper.find('[data-testid="review-toggle"]').trigger('click')
    expect(wrapper.find('[data-testid="review-text"] img').exists()).toBe(false)
    expect(wrapper.find('[data-testid="review-text"]').text()).toContain('<img src=x')
  })
})
