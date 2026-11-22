import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api } from '@/api'
import { toApiError, type ApiError } from '@/api/errors'
import type { CountryDetailDto, MapDataDto, MapStatusFilter, MapType } from '@/types/api'
import { DEFAULT_MAP_STATUS, mapStatusToApiParam } from '@/utils/mapStatus'

/** Chave de cache de um mapa: tipo + filtro de status (caches nunca se misturam). */
export function mapKey(mapType: MapType, status: MapStatusFilter = DEFAULT_MAP_STATUS): string {
  return `${mapType}:${status}`
}

/**
 * Dados dos mapas (um por tipo e filtro de status) e detalhe do país selecionado.
 * - Os dois mapas são independentes: cache, carregamento e erro por (tipo, status).
 * - Respostas de detalhe fora de ordem são descartadas (só vale a última solicitação).
 * - Mutações na biblioteca chamam `invalidate()`.
 */
export const useMapsStore = defineStore('maps', () => {
  const data = ref<Record<string, MapDataDto | undefined>>({})
  const loadingByKey = ref<Record<string, boolean | undefined>>({})
  const errorByKey = ref<Record<string, ApiError | null | undefined>>({})
  const inFlight = new Map<string, Promise<void>>()
  const inFlightId = new Map<string, number>()
  let fetchSeq = 0
  let generation = 0

  const countryDetail = ref<CountryDetailDto | null>(null)
  const detailLoading = ref(false)
  const detailError = ref<ApiError | null>(null)
  const detailCache = new Map<string, CountryDetailDto>()
  let detailRequest = 0

  /** `silent`: pré-carregamento; não registra erro (a tela não depende dele). */
  function fetchMap(
    mapType: MapType,
    status: MapStatusFilter = DEFAULT_MAP_STATUS,
    options: { force?: boolean; silent?: boolean } = {},
  ) {
    const key = mapKey(mapType, status)
    if (data.value[key] && !options.force) return Promise.resolve()
    const existing = inFlight.get(key)
    if (existing) return existing
    const gen = generation
    loadingByKey.value = { ...loadingByKey.value, [key]: true }
    if (!options.silent) errorByKey.value = { ...errorByKey.value, [key]: null }
    const id = ++fetchSeq
    const promise = (async () => {
      try {
        const param = mapStatusToApiParam(status)
        const result = await (param ? api.maps.get(mapType, param) : api.maps.get(mapType))
        if (gen === generation) data.value = { ...data.value, [key]: result }
      } catch (e) {
        if (!options.silent && gen === generation)
          errorByKey.value = { ...errorByKey.value, [key]: toApiError(e) }
      } finally {
        // Após reset()/invalidate() a chave pode já pertencer a outra requisição/usuário.
        if (gen === generation) loadingByKey.value = { ...loadingByKey.value, [key]: false }
        if (inFlightId.get(key) === id) {
          inFlight.delete(key)
          inFlightId.delete(key)
        }
      }
    })()
    inFlight.set(key, promise)
    inFlightId.set(key, id)
    return promise
  }

  async function fetchCountryDetail(
    mapType: MapType,
    iso: string,
    status: MapStatusFilter = DEFAULT_MAP_STATUS,
    force = false,
  ) {
    const key = `${mapKey(mapType, status)}:${iso}`
    const request = ++detailRequest
    detailError.value = null
    const cached = detailCache.get(key)
    if (cached && !force) {
      countryDetail.value = cached
      detailLoading.value = false
      return
    }
    countryDetail.value = null
    detailLoading.value = true
    const gen = generation
    try {
      const param = mapStatusToApiParam(status)
      const result = await (param
        ? api.maps.getCountryDetail(mapType, iso, param)
        : api.maps.getCountryDetail(mapType, iso))
      if (request !== detailRequest) return
      if (gen === generation) detailCache.set(key, result)
      countryDetail.value = result
    } catch (e) {
      if (request !== detailRequest) return
      detailError.value = toApiError(e)
    } finally {
      if (request === detailRequest) detailLoading.value = false
    }
  }

  function clearCountryDetail() {
    detailRequest++
    countryDetail.value = null
    detailError.value = null
    detailLoading.value = false
  }

  /** Mudanças na biblioteca invalidam os mapas (recalculados sob demanda). */
  function invalidate() {
    generation++
    data.value = {}
    detailCache.clear()
    inFlight.clear()
    inFlightId.clear()
  }

  /** Troca/encerramento de sessão: zera dados, detalhe e caches e descarta respostas em voo. */
  function reset() {
    generation++
    detailRequest++
    data.value = {}
    loadingByKey.value = {}
    errorByKey.value = {}
    countryDetail.value = null
    detailLoading.value = false
    detailError.value = null
    detailCache.clear()
    inFlight.clear()
    inFlightId.clear()
  }

  return {
    data,
    loadingByKey,
    errorByKey,
    countryDetail,
    detailLoading,
    detailError,
    fetchMap,
    fetchCountryDetail,
    clearCountryDetail,
    invalidate,
    reset,
  }
})
