import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import type { ProfileSummaryDto } from '@/types/api'
import { useProfilesStore } from '@/stores/profiles'
import { usePeopleSearch } from '../usePeopleSearch'

const search = vi.hoisted(() => vi.fn())
vi.mock('@/api', () => ({ api: { profiles: { search } } }))

const marina: ProfileSummaryDto = {
  handle: 'marina_lima',
  name: 'Marina Lima',
  followersCount: 3,
  readBooksCount: 8,
  isFollowing: false,
  followsYou: false,
}

function setup() {
  setActivePinia(createPinia())
  let composable!: ReturnType<typeof usePeopleSearch>
  const wrapper = mount(
    defineComponent({
      setup() {
        composable = usePeopleSearch()
        return () => h('div')
      },
    }),
  )
  return { composable, wrapper }
}

describe('usePeopleSearch', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    search.mockReset()
  })
  afterEach(() => vi.useRealTimers())

  it('não busca com menos de 2 caracteres (após trim)', async () => {
    const { composable } = setup()
    composable.onInput('m')
    composable.onInput('  a ')
    await vi.advanceTimersByTimeAsync(1000)
    expect(search).not.toHaveBeenCalled()
    expect(composable.loading.value).toBe(false)
  })

  it('debounce de 250 ms: só a última digitação dispara, com limite 10 e signal', async () => {
    search.mockResolvedValue([marina])
    const { composable } = setup()
    composable.onInput('ma')
    await vi.advanceTimersByTimeAsync(200)
    composable.onInput('mar')
    await vi.advanceTimersByTimeAsync(200)
    expect(search).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(60)
    expect(search).toHaveBeenCalledTimes(1)
    expect(search).toHaveBeenCalledWith('mar', 10, expect.any(AbortSignal))
    expect(composable.results.value).toEqual([marina])
    expect(composable.searchedQuery.value).toBe('mar')
    expect(composable.loading.value).toBe(false)
  })

  it('digitar de novo cancela a requisição em voo e ignora a resposta obsoleta', async () => {
    const signals: AbortSignal[] = []
    let resolveOld!: (value: ProfileSummaryDto[]) => void
    search.mockImplementationOnce((_q: string, _l: number, signal: AbortSignal) => {
      signals.push(signal)
      return new Promise((res) => (resolveOld = res))
    })
    search.mockResolvedValueOnce([marina])
    const { composable } = setup()

    composable.onInput('ma')
    await vi.advanceTimersByTimeAsync(260)
    expect(search).toHaveBeenCalledTimes(1)
    composable.onInput('mar') // texto mudou com a primeira ainda em voo
    expect(signals[0]!.aborted).toBe(true)
    await vi.advanceTimersByTimeAsync(260)
    resolveOld([{ ...marina, handle: 'velho', name: 'Velho' }])
    await vi.advanceTimersByTimeAsync(0)

    expect(composable.results.value.map((p) => p.handle)).toEqual(['marina_lima'])
  })

  it('apagar o texto limpa resultados e cancela a busca pendente', async () => {
    search.mockResolvedValue([marina])
    const { composable } = setup()
    composable.onInput('mar')
    await vi.advanceTimersByTimeAsync(260)
    expect(composable.results.value).toHaveLength(1)

    composable.onInput('')
    expect(composable.results.value).toEqual([])
    expect(composable.searchedQuery.value).toBeNull()

    composable.onInput('mar')
    composable.onInput('m') // volta a ficar curto antes do debounce
    await vi.advanceTimersByTimeAsync(1000)
    expect(search).toHaveBeenCalledTimes(1)
  })

  it('desmontar a página limpa o estado de busca do store', async () => {
    search.mockResolvedValue([marina])
    const { composable, wrapper } = setup()
    composable.onInput('mar')
    await vi.advanceTimersByTimeAsync(260)
    wrapper.unmount()
    expect(useProfilesStore().searchResults).toEqual([])
  })
})
