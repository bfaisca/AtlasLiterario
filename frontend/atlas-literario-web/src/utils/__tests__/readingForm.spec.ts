import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ReadingFields from '@/components/books/ReadingFields.vue'
import {
  emptyReadingDraft,
  toReadingRequest,
  validateReading,
  type ReadingDraft,
} from '../readingForm'

const TODAY = '2026-11-15'
const draft = (overrides: Partial<ReadingDraft>): ReadingDraft => ({
  ...emptyReadingDraft(),
  ...overrides,
})

describe('validateReading', () => {
  it('exige início ≤ conclusão para livros lidos', () => {
    expect(
      validateReading(
        draft({ status: 'Read', startDate: '2025-05-10', finishedDate: '2025-05-01' }),
        TODAY,
      ),
    ).toEqual({ finishedDate: 'A conclusão deve ser igual ou posterior ao início.' })
    expect(
      validateReading(
        draft({ status: 'Read', startDate: '2025-05-01', finishedDate: '2025-05-01' }),
        TODAY,
      ),
    ).toEqual({})
  })

  it('ignora a data de conclusão quando o status não é Lido', () => {
    expect(
      validateReading(
        draft({ status: 'Reading', startDate: '2025-05-10', finishedDate: '2025-05-01' }),
        TODAY,
      ),
    ).toEqual({})
  })

  it('rejeita datas futuras, nota fora de 1–5 e resenha longa', () => {
    const errors = validateReading(
      draft({
        status: 'Read',
        startDate: '2027-01-01',
        finishedDate: '2027-01-02',
        rating: 6,
        review: 'x'.repeat(10_001),
      }),
      TODAY,
    )
    expect(Object.keys(errors).sort()).toEqual(['finishedDate', 'rating', 'review', 'startDate'])
  })

  it('Lido sem data de conclusão é válido (o backend usa a data de hoje)', () => {
    expect(validateReading(draft({ status: 'Read' }), TODAY)).toEqual({})
    expect(toReadingRequest(draft({ status: 'Read' })).finishedDate).toBeNull()
  })

  it('toReadingRequest limpa a conclusão ao sair de Lido e normaliza vazios', () => {
    expect(
      toReadingRequest(
        draft({ status: 'Abandoned', finishedDate: '2025-01-01', review: '  ', startDate: '' }),
      ),
    ).toEqual({
      status: 'Abandoned',
      rating: null,
      review: null,
      startDate: null,
      finishedDate: null,
    })
  })
})

describe('ReadingFields', () => {
  it('mostra a data de conclusão apenas para Lido e exibe erros', async () => {
    const wrapper = mount(ReadingFields, {
      props: {
        modelValue: draft({ status: 'Reading' }),
        'onUpdate:modelValue': (value: ReadingDraft) => wrapper.setProps({ modelValue: value }),
        errors: { finishedDate: 'A conclusão deve ser igual ou posterior ao início.' },
      },
    })
    expect(wrapper.findAll('input[type="date"]')).toHaveLength(1)

    await wrapper.find('input[value="Read"]').setValue(true)
    expect((wrapper.emitted('update:modelValue')?.at(-1)?.[0] as ReadingDraft).status).toBe('Read')
    expect(wrapper.findAll('input[type="date"]')).toHaveLength(2)
    expect(wrapper.text()).toContain('A conclusão deve ser igual ou posterior ao início.')
  })
})
