import axios from 'axios'
import { ref } from 'vue'

/**
 * Hospedagens gratuitas (Render) dormem após alguns minutos sem tráfego e a primeira requisição pode levar ~1 minuto.
 * Antes de falar com a API, o cliente consulta `/health` (com novas tentativas) e só então segue. Assim a sessão não é
 * encerrada por um timeout de quem ainda estava acordando, e a tela avisa o que está acontecendo.
 */
export interface WarmupOptions {
  enabled: boolean
  healthUrl: string
  /** Uma tentativa; devolve true se o servidor respondeu "de pé". */
  probe?: (url: string) => Promise<boolean>
  sleep?: (ms: number) => Promise<void>
  now?: () => number
  /** Por quanto tempo uma resposta boa dispensa nova verificação (o Render dorme após ~15 min). */
  awakeTtlMs?: number
  maxWaitMs?: number
  retryDelayMs?: number
  /** Só mostra o aviso "acordando" se a primeira verificação demorar mais que isto (evita piscar). */
  showAfterMs?: number
}

export function createApiWarmup(options: WarmupOptions) {
  const awakeTtlMs = options.awakeTtlMs ?? 10 * 60_000
  const maxWaitMs = options.maxWaitMs ?? 120_000
  const retryDelayMs = options.retryDelayMs ?? 3_000
  const showAfterMs = options.showAfterMs ?? 1_500
  const now = options.now ?? Date.now
  const sleep =
    options.sleep ?? ((ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms)))
  const probe = options.probe ?? defaultProbe

  /** true enquanto espera o servidor acordar (para exibir o aviso). */
  const waking = ref(false)
  let awakeUntil = 0
  let inFlight: Promise<void> | null = null

  function markAwake() {
    awakeUntil = now() + awakeTtlMs
  }

  async function run(): Promise<void> {
    const startedAt = now()
    const showTimer = setTimeout(() => (waking.value = true), showAfterMs)
    try {
      while (true) {
        if (await probe(options.healthUrl)) {
          markAwake()
          return
        }
        if (now() - startedAt >= maxWaitMs) return // desiste: a requisição real mostrará o erro de rede
        await sleep(retryDelayMs)
      }
    } finally {
      clearTimeout(showTimer)
      waking.value = false
    }
  }

  function ensureAwake(): Promise<void> {
    if (!options.enabled || now() < awakeUntil) return Promise.resolve()
    inFlight ??= run().finally(() => {
      inFlight = null
    })
    return inFlight
  }

  return { waking, ensureAwake, markAwake }
}

/** 200 = de pé (a API responde "Healthy"; um servidor estático devolve o index.html, também 200). Resto = dormindo. */
async function defaultProbe(url: string): Promise<boolean> {
  try {
    const response = await axios.get(url, { timeout: 10_000, validateStatus: () => true })
    return response.status === 200
  } catch {
    return false
  }
}

function resolveHealthUrl(): string {
  const base = import.meta.env.VITE_API_BASE_URL || '/api'
  try {
    if (/^https?:\/\//i.test(base)) return new URL('/health', base).href
  } catch {
    // cai no caminho relativo
  }
  return '/health'
}

/** Ativo só no build de produção com a API real (em dev/testes/mocks não há o que acordar). */
const warmup = createApiWarmup({
  enabled: import.meta.env.PROD && import.meta.env.VITE_USE_MOCKS !== 'true',
  healthUrl: resolveHealthUrl(),
})

export const apiWaking = warmup.waking
export const ensureApiAwake = warmup.ensureAwake
export const markApiAwake = warmup.markAwake
