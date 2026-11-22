import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, h } from 'vue'
import BaseInput from '../BaseInput.vue'

describe('BaseInput — senha', () => {
  it('alterna entre mostrar e ocultar a senha com botão acessível', async () => {
    const wrapper = mount(BaseInput, {
      props: { label: 'Senha', type: 'password', modelValue: 'segredo1' },
    })
    const input = wrapper.find('input')
    const toggle = wrapper.find('button')

    expect(input.attributes('type')).toBe('password')
    expect(toggle.attributes('type')).toBe('button') // não submete o formulário
    expect(toggle.attributes('aria-label')).toBe('Mostrar senha')
    expect(toggle.attributes('aria-pressed')).toBe('false')
    expect(toggle.attributes('aria-controls')).toBe(input.attributes('id'))

    await toggle.trigger('click')
    expect(input.attributes('type')).toBe('text')
    expect(toggle.attributes('aria-label')).toBe('Ocultar senha')
    expect(toggle.attributes('aria-pressed')).toBe('true')
    expect((input.element as HTMLInputElement).value).toBe('segredo1')

    await toggle.trigger('click')
    expect(input.attributes('type')).toBe('password')
  })

  it('clicar no botão dentro de um form não dispara submit', async () => {
    let submitted = false
    const Form = defineComponent({
      render: () =>
        h(
          'form',
          {
            onSubmit: (event: Event) => {
              event.preventDefault()
              submitted = true
            },
          },
          [h(BaseInput, { label: 'Senha', type: 'password' })],
        ),
    })
    const wrapper = mount(Form, { attachTo: document.body })
    ;(wrapper.find('button').element as HTMLButtonElement).click()
    await wrapper.vm.$nextTick()
    expect(submitted).toBe(false)
    wrapper.unmount()
  })

  it('o rótulo continua associado ao input', () => {
    const wrapper = mount(BaseInput, { props: { label: 'Senha', type: 'password' } })
    expect(wrapper.find('label').attributes('for')).toBe(wrapper.find('input').attributes('id'))
  })

  it('campos que não são de senha não têm o botão', () => {
    const wrapper = mount(BaseInput, { props: { label: 'E-mail', type: 'email' } })
    expect(wrapper.find('button').exists()).toBe(false)
  })
})
