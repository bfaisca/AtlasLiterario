import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, ref } from 'vue'
import { mount } from '@vue/test-utils'
import { ApiError } from '@/api/errors'
import type { AuthorSuggestionDto } from '@/types/api'
import AuthorAutocomplete from '../AuthorAutocomplete.vue'

const search = vi.hoisted(() => vi.fn())
vi.mock('@/api', () => ({ api: { authors: { search } } }))

const suggestions: AuthorSuggestionDto[] = [
  {
    id: 'a-1',
    name: 'Franz Kafka',
    nationalityIsos: ['AUT', 'CZE'],
    nationalityNames: ['Áustria', 'Tchéquia'],
  },
  { id: 'a-2', name: 'Franz Werfel', nationalityIsos: [], nationalityNames: [] },
]

/** Pai mínimo que mantém o estado como o BookFieldsForm faz. */
const Host = defineComponent({
  components: { AuthorAutocomplete },
  setup() {
    const name = ref('')
    const authorId = ref<string | null>(null)
    return { name, authorId }
  },
  template: `<AuthorAutocomplete label="Autor 1" :model-value="name" :author-id="authorId"
    @change="(v) => { name = v.name; authorId = v.authorId }" />`,
})

function setup() {
  const wrapper = mount(Host, { attachTo: document.body })
  const input = wrapper.find<HTMLInputElement>('input[role="combobox"]')
  return { wrapper, input, vm: wrapper.vm as unknown as { name: string; authorId: string | null } }
}

async function type(input: ReturnType<typeof setup>['input'], text: string) {
  await input.setValue(text)
  await vi.advanceTimersByTimeAsync(300)
}

describe('AuthorAutocomplete', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    search.mockReset()
    search.mockResolvedValue(suggestions)
  })
  afterEach(() => {
    vi.useRealTimers()
    document.body.innerHTML = ''
  })

  it('é um combobox ARIA ligado ao listbox', async () => {
    const { wrapper, input } = setup()
    expect(input.attributes('role')).toBe('combobox')
    expect(input.attributes('aria-autocomplete')).toBe('list')
    expect(input.attributes('aria-expanded')).toBe('false')
    await type(input, 'fra')
    expect(input.attributes('aria-expanded')).toBe('true')
    const listbox = wrapper.find('[role="listbox"]')
    expect(input.attributes('aria-controls')).toBe(listbox.attributes('id'))
    expect(wrapper.findAll('[role="option"]')).toHaveLength(2)
    expect(wrapper.find('label').attributes('for')).toBe(input.attributes('id'))
  })

  it('só busca com ≥ 2 caracteres e com debounce de 250 ms', async () => {
    const { input } = setup()
    await input.setValue('f')
    await vi.advanceTimersByTimeAsync(500)
    expect(search).not.toHaveBeenCalled()
    await input.setValue('fr')
    await vi.advanceTimersByTimeAsync(249)
    expect(search).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(2)
    expect(search).toHaveBeenCalledTimes(1)
    expect(search.mock.calls[0]!.slice(0, 2)).toEqual(['fr', 8])
  })

  it('mostra nome + nacionalidades e "país desconhecido" quando vazio', async () => {
    const { wrapper, input } = setup()
    await type(input, 'franz')
    const [first, second] = wrapper.findAll('[role="option"]')
    expect(first!.text()).toContain('Franz Kafka')
    expect(first!.text()).toContain('Áustria, Tchéquia')
    expect(second!.text()).toContain('país desconhecido')
  })

  it('setas movem aria-activedescendant e Enter escolhe, fixando o id', async () => {
    const { wrapper, input, vm } = setup()
    await type(input, 'franz')
    const options = wrapper.findAll('[role="option"]')
    await input.trigger('keydown', { key: 'ArrowDown' })
    expect(input.attributes('aria-activedescendant')).toBe(options[0]!.attributes('id'))
    await input.trigger('keydown', { key: 'ArrowDown' })
    expect(input.attributes('aria-activedescendant')).toBe(options[1]!.attributes('id'))
    expect(options[1]!.attributes('aria-selected')).toBe('true')
    await input.trigger('keydown', { key: 'ArrowDown' }) // dá a volta
    expect(input.attributes('aria-activedescendant')).toBe(options[0]!.attributes('id'))
    await input.trigger('keydown', { key: 'ArrowUp' }) // volta ao último
    expect(input.attributes('aria-activedescendant')).toBe(options[1]!.attributes('id'))
    await input.trigger('keydown', { key: 'ArrowUp' })
    await input.trigger('keydown', { key: 'Enter' })
    expect(vm.name).toBe('Franz Kafka')
    expect(vm.authorId).toBe('a-1')
    expect(input.element.value).toBe('Franz Kafka')
    expect(wrapper.find('[role="listbox"]').isVisible()).toBe(false)
    expect(wrapper.find('[data-testid="author-pinned"]').text()).toContain('Áustria, Tchéquia')
  })

  it('Enter sem opção ativa não é consumido (o formulário pode enviar)', async () => {
    const { input } = setup()
    await type(input, 'franz')
    const event = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true, bubbles: true })
    input.element.dispatchEvent(event)
    expect(event.defaultPrevented).toBe(false)
  })

  it('clique do mouse escolhe a sugestão', async () => {
    const { wrapper, input, vm } = setup()
    await type(input, 'franz')
    await wrapper.findAll('[role="option"]')[1]!.trigger('click')
    expect(vm.name).toBe('Franz Werfel')
    expect(vm.authorId).toBe('a-2')
    // "país desconhecido" também no chip do autor fixado
    expect(wrapper.find('[data-testid="author-pinned"]').text()).toContain('país desconhecido')
  })

  it('Esc fecha a lista e consome o evento; sem lista aberta, o Esc passa adiante (modal)', async () => {
    const { wrapper, input } = setup()
    const parentSpy = vi.fn()
    document.addEventListener('keydown', parentSpy)
    await type(input, 'franz')
    await input.trigger('keydown', { key: 'Escape' })
    expect(wrapper.find('[role="listbox"]').isVisible()).toBe(false)
    expect(input.attributes('aria-expanded')).toBe('false')
    expect(parentSpy).not.toHaveBeenCalled()
    await input.trigger('keydown', { key: 'Escape' })
    expect(parentSpy).toHaveBeenCalledTimes(1)
    document.removeEventListener('keydown', parentSpy)
  })

  it('digitar depois de escolher desfaz a escolha (authorId volta a null)', async () => {
    const { wrapper, input, vm } = setup()
    await type(input, 'franz')
    await wrapper.findAll('[role="option"]')[0]!.trigger('click')
    expect(vm.authorId).toBe('a-1')
    await input.setValue('Franz Kafkaesco')
    expect(vm.authorId).toBeNull()
    expect(vm.name).toBe('Franz Kafkaesco')
    expect(wrapper.find('[data-testid="author-pinned"]').exists()).toBe(false)
  })

  it('"Usar outro nome" desfaz a escolha mantendo o texto', async () => {
    const { wrapper, input, vm } = setup()
    await type(input, 'franz')
    await wrapper.findAll('[role="option"]')[0]!.trigger('click')
    await wrapper.find('.ac__unpin').trigger('click')
    expect(vm.authorId).toBeNull()
    expect(vm.name).toBe('Franz Kafka')
    expect(input.element.value).toBe('Franz Kafka')
  })

  it('texto livre sem escolher sugestão mantém authorId nulo', async () => {
    const { input, vm } = setup()
    await type(input, 'Autora Desconhecida')
    expect(vm.name).toBe('Autora Desconhecida')
    expect(vm.authorId).toBeNull()
  })

  it('sem resultados: avisa na região viva e não abre listbox', async () => {
    search.mockResolvedValue([])
    const { wrapper, input } = setup()
    await type(input, 'zzzz')
    expect(wrapper.find('[role="status"]').text()).toContain('Nenhum autor cadastrado')
    expect(input.attributes('aria-expanded')).toBe('false')
  })

  it('erro da API: mensagem na região viva, digitação livre segue funcionando', async () => {
    search.mockRejectedValue(new ApiError({ status: 500, title: 'Erro interno do servidor' }))
    const { wrapper, input, vm } = setup()
    await type(input, 'franz')
    expect(wrapper.find('[role="status"]').text()).not.toBe('')
    expect(wrapper.findAll('[role="option"]')).toHaveLength(0)
    await input.setValue('Franz K')
    expect(vm.name).toBe('Franz K')
    expect(vm.authorId).toBeNull()
  })

  it('exibe o erro de validação do campo', () => {
    const wrapper = mount(AuthorAutocomplete, {
      props: { label: 'Autor 1', modelValue: '', error: 'Informe ao menos um autor.' },
    })
    expect(wrapper.find('[role="alert"]').text()).toBe('Informe ao menos um autor.')
    expect(wrapper.find('input').attributes('aria-invalid')).toBe('true')
  })
})
