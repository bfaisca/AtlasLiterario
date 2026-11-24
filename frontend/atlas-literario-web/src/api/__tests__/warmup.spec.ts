import { describe, expect, it, vi } from 'vitest'
import { createApiWarmup } from '../warmup'

function setup(results: boolean[], overrides: { enabled?: boolean } = {}) {
  let clock = 0
  const probe = vi.fn(async () => results.shift() ?? true)
  const sleep = vi.fn(async (ms: number) => {
    clock += ms
  })
  const warmup = createApiWarmup({
    enabled: overrides.enabled ?? true,
    healthUrl: '/health',
    probe,
    sleep,
    now: () => clock,
    retryDelayMs: 3_000,
    maxWaitMs: 12_000,
  })
  return { warmup, probe, sleep }
}

describe('createApiWarmup', () => {
  it('espera a API responder: tenta de novo até ficar de pé', async () => {
    const { warmup, probe, sleep } = setup([false, false, true])

    await warmup.ensureAwake()

    expect(probe).toHaveBeenCalledTimes(3)
    expect(sleep).toHaveBeenCalledTimes(2)
    expect(warmup.waking.value).toBe(false)
  })

  it('depois de acordada não verifica de novo dentro do prazo', async () => {
    const { warmup, probe } = setup([true])

    await warmup.ensureAwake()
    await warmup.ensureAwake()

    expect(probe).toHaveBeenCalledTimes(1)
  })

  it('chamadas simultâneas compartilham a mesma verificação', async () => {
    const { warmup, probe } = setup([false, true])

    await Promise.all([warmup.ensureAwake(), warmup.ensureAwake(), warmup.ensureAwake()])

    expect(probe).toHaveBeenCalledTimes(2)
  })

  it('desiste ao fim do prazo e deixa a requisição real mostrar o erro', async () => {
    const { warmup, probe } = setup(Array(20).fill(false))

    await expect(warmup.ensureAwake()).resolves.toBeUndefined()

    expect(probe.mock.calls.length).toBeLessThanOrEqual(6)
    expect(warmup.waking.value).toBe(false)
  })

  it('desativada (dev, testes, mocks) não consulta nada', async () => {
    const { warmup, probe } = setup([false], { enabled: false })

    await warmup.ensureAwake()

    expect(probe).not.toHaveBeenCalled()
  })
})
