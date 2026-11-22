import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import StarRating from '../StarRating.vue'

describe('StarRating', () => {
  it('renderiza 5 estrelas e emite a nota clicada', async () => {
    const wrapper = mount(StarRating, { props: { modelValue: null } })
    const stars = wrapper.findAll('button')
    expect(stars).toHaveLength(5)

    await stars[3]!.trigger('click')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([4])
  })

  it('clicar na nota atual limpa a avaliação', async () => {
    const wrapper = mount(StarRating, { props: { modelValue: 3 } })
    await wrapper.findAll('button')[2]!.trigger('click')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([null])
  })

  it('marca as estrelas acesas conforme a nota', () => {
    const wrapper = mount(StarRating, { props: { modelValue: 2 } })
    expect(wrapper.findAll('.is-on')).toHaveLength(2)
    expect(wrapper.findAll('button')[1]!.attributes('aria-checked')).toBe('true')
  })

  it('setas do teclado ajustam a nota', async () => {
    const wrapper = mount(StarRating, { props: { modelValue: 2 } })
    await wrapper.find('[role="radiogroup"]').trigger('keydown', { key: 'ArrowRight' })
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([3])
  })

  it('modo somente leitura não tem botões nem emite eventos', () => {
    const wrapper = mount(StarRating, { props: { modelValue: 4, readonly: true } })
    expect(wrapper.findAll('button')).toHaveLength(0)
    expect(wrapper.attributes('aria-label')).toContain('4 de 5')
    expect(wrapper.findAll('.is-on')).toHaveLength(4)
  })
})
