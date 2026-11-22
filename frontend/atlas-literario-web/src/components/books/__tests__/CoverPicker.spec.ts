import { afterEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { CoverError } from '@/utils/imageResize'
import CoverPicker from '../CoverPicker.vue'

const original = new File([new Uint8Array(3000)], 'foto.png', { type: 'image/png' })
const prepared = new File([new Uint8Array(1500)], 'foto.jpg', { type: 'image/jpeg' })

function mountPicker(prepare = vi.fn().mockResolvedValue(prepared), props = {}) {
  const wrapper = mount(CoverPicker, {
    props: { title: 'Meu livro', prepare, ...props },
    global: { stubs: { RouterLink: true } },
    attachTo: document.body,
  })
  return { wrapper, prepare }
}

async function pick(wrapper: ReturnType<typeof mountPicker>['wrapper'], file: File) {
  const input = wrapper.find<HTMLInputElement>('[data-testid="cover-input"]')
  Object.defineProperty(input.element, 'files', { value: [file], configurable: true })
  await input.trigger('change')
  await flushPromises()
}

describe('CoverPicker', () => {
  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('aceita só JPEG, PNG e WebP no seletor e o botão abre o input (teclado: botão nativo)', async () => {
    const { wrapper } = mountPicker()
    const input = wrapper.find<HTMLInputElement>('[data-testid="cover-input"]')
    expect(input.attributes('accept')).toBe('image/jpeg,image/png,image/webp')
    const click = vi.spyOn(input.element, 'click')
    const button = wrapper.findAll('button').find((b) => b.text().includes('Escolher imagem'))!
    expect(button.element.tagName).toBe('BUTTON')
    await button.trigger('click')
    expect(click).toHaveBeenCalled()
  })

  it('seleção: reduz, emite o arquivo preparado e mostra nome e tamanho', async () => {
    const { wrapper, prepare } = mountPicker()
    await pick(wrapper, original)
    expect(prepare).toHaveBeenCalledWith(original)
    expect(wrapper.emitted('update:file')![0]).toEqual([prepared])
    await wrapper.setProps({ file: prepared })
    expect(wrapper.find('.picker__status').text()).toContain('foto.jpg · 1 kB')
    expect(wrapper.text()).toContain('Escolher outra imagem')
  })

  it('arrastar e soltar também seleciona', async () => {
    const { wrapper, prepare } = mountPicker()
    await wrapper.find('[data-testid="cover-picker"]').trigger('dragenter')
    expect(wrapper.find('.picker').classes()).toContain('is-dragging')
    const drop = new Event('drop', { bubbles: true, cancelable: true })
    Object.defineProperty(drop, 'dataTransfer', { value: { files: [original] } })
    wrapper.find('[data-testid="cover-picker"]').element.dispatchEvent(drop)
    await flushPromises()
    expect(prepare).toHaveBeenCalledWith(original)
    expect(wrapper.emitted('update:file')![0]).toEqual([prepared])
    expect(wrapper.find('.picker').classes()).not.toContain('is-dragging')
  })

  it('"Remover" descarta a seleção', async () => {
    const { wrapper } = mountPicker(undefined, { file: prepared })
    const remove = wrapper.findAll('button').find((b) => b.text().includes('Remover'))!
    await remove.trigger('click')
    expect(wrapper.emitted('update:file')!.at(-1)).toEqual([null])
  })

  it('sem seleção não mostra "Remover"', () => {
    const { wrapper } = mountPicker()
    expect(wrapper.findAll('button').some((b) => b.text().includes('Remover'))).toBe(false)
  })

  it('mostra erro em português (role alert, dentro da região aria-live) e não emite arquivo', async () => {
    const prepare = vi.fn().mockRejectedValue(new CoverError('unsupported_type'))
    const { wrapper } = mountPicker(prepare)
    await pick(wrapper, new File(['x'], 'a.gif', { type: 'image/gif' }))
    const status = wrapper.find('.picker__status')
    expect(status.attributes('aria-live')).toBe('polite')
    expect(status.find('[role="alert"]').text()).toContain('SVG e GIF não são aceitas')
    expect(wrapper.emitted('update:file')).toBeUndefined()
    expect(wrapper.emitted('error')![0]![0]).toContain('SVG e GIF')
  })

  it('erro inesperado vira mensagem genérica em português', async () => {
    const prepare = vi.fn().mockRejectedValue(new Error('boom'))
    const { wrapper } = mountPicker(prepare)
    await pick(wrapper, original)
    expect(wrapper.find('[role="alert"]').text()).toContain('Não foi possível processar')
  })

  it('exibe a capa atual quando nada foi selecionado', () => {
    const { wrapper } = mountPicker(undefined, { currentUrl: 'https://exemplo.com/c.jpg' })
    expect(wrapper.find('img').attributes('src')).toBe('https://exemplo.com/c.jpg')
  })

  it('desabilitado: botões e input ficam inativos e nada é processado', async () => {
    const { wrapper, prepare } = mountPicker(undefined, { disabled: true })
    expect(wrapper.findAll('button').every((b) => b.attributes('disabled') !== undefined)).toBe(
      true,
    )
    await pick(wrapper, original)
    expect(prepare).not.toHaveBeenCalled()
  })

  it('alvos de toque: botões com altura mínima de 44 px', () => {
    const { wrapper } = mountPicker()
    expect(wrapper.find('.picker__button').exists()).toBe(true)
  })
})
