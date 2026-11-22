import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import { mount } from '@vue/test-utils'
import { ApiError } from '@/api/errors'
import type { AuthorSuggestionDto } from '@/types/api'
import { useAuthorSearch } from '../useAuthorSearch'

const search = vi.hoisted(() => vi.fn())
vi.mock('@/api', () => ({ api: { authors: { search } } }))

const camus: AuthorSuggestionDto = {
  id: 'a-1',
  name: 'Albert Camus',
  nationalityIsos: ['FRA'],
  nationalityNames: ['França'],
}

function setup() {
  let composable!: ReturnType<typeof useAuthorSearch>
  const wrapper = mount(
    defineComponent({
      setup() {
        composable = useAuthorSearch()
        return () => h('div')
      },
    }),
  )
  return { composable, wrapper }
}

describe('useAuthorSearch', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    search.mockReset()
  })
  afterEach(() => vi.useRealTimers())

  it('não busca com menos de 2 caracteres (após trim)', async () => {
    const { composable } = setup()
    composable.search('a')
    composable.search('  b ')
    await vi.advanceTimersByTimeAsync(1000)
    expect(search).not.toHaveBeenCalled()
    expect(composable.loading.value).toBe(false)
  })

  it('debounce de 250 ms: só a última digitação dispara, com limite 8', async () => {
    search.mockResolvedValue([camus])
    const { composable } = setup()
    composable.search('al')
    await vi.advanceTimersByTimeAsync(200)
    composable.search('alb')
    await vi.advanceTimersByTimeAsync(200)
    expect(search).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(60)
    expect(search).toHaveBeenCalledTimes(1)
    expect(search).toHaveBeenCalledWith('alb', 8, expect.any(AbortSignal))
    expect(composable.suggestions.value).toEqual([camus])
    expect(composable.loading.value).toBe(false)
  })

  it('cancela a requisição obsoleta e ignora a resposta dela', async () => {
    const signals: AbortSignal[] = []
    const resolvers: Array<(v: AuthorSuggestionDto[]) => void> = []
    search.mockImplementation(
      (_q: string, _l: number, signal: AbortSignal) =>
        new Promise((resolve) => {
          signals.push(signal)
          resolvers.push(resolve)
        }),
    )
    const { composable } = setup()
    composable.search('alb')
    await vi.advanceTimersByTimeAsync(260)
    composable.search('albe')
    expect(signals[0]!.aborted).toBe(true) // abortada assim que o texto muda
    await vi.advanceTimersByTimeAsync(260)
    expect(signals).toHaveLength(2)
    resolvers[1]!([camus])
    await vi.advanceTimersByTimeAsync(0)
    resolvers[0]!([{ ...camus, id: 'velho', name: 'Obsoleto' }]) // chega atrasada
    await vi.advanceTimersByTimeAsync(0)
    expect(composable.suggestions.value.map((s) => s.name)).toEqual(['Albert Camus'])
  })

  it('erro da API vira mensagem amigável (camada central) e zera as sugestões', async () => {
    search.mockRejectedValue(new ApiError({ status: 0, title: 'Sem conexão' }))
    const { composable } = setup()
    composable.search('alb')
    await vi.advanceTimersByTimeAsync(300)
    expect(composable.error.value).toBeTruthy()
    expect(composable.suggestions.value).toEqual([])
    expect(composable.hasNoResults.value).toBe(false)
  })

  it('lista vazia = hasNoResults; clear() reinicia tudo', async () => {
    search.mockResolvedValue([])
    const { composable } = setup()
    composable.search('zzz')
    await vi.advanceTimersByTimeAsync(300)
    expect(composable.hasNoResults.value).toBe(true)
    composable.clear()
    expect(composable.hasNoResults.value).toBe(false)
    expect(composable.searchedQuery.value).toBeNull()
  })

  it('ao desmontar cancela busca pendente', async () => {
    search.mockResolvedValue([camus])
    const { composable, wrapper } = setup()
    composable.search('alb')
    wrapper.unmount()
    await vi.advanceTimersByTimeAsync(500)
    expect(search).not.toHaveBeenCalled()
  })
})
