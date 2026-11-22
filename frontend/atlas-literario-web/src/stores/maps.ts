import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api } from '@/api'
import { toApiError, type ApiError } from '@/api/errors'
import type { CountryDetailDto, MapDataDto, MapType } from '@/types/api'

/**
 * Dados dos mapas (um por tipo) e detalhe do país selecionado.
 * - Os dois mapas são independentes: cache, carregamento e erro por tipo.
 * - Respostas de detalhe fora de ordem são descartadas (só vale a última solicitação).
 * - Mutações na biblioteca chamam `invalidate()`.
 */
export const useMapsStore = defineStore('maps', () => {
  const data = ref<Partial<Record<MapType, MapDataDto>>>({})
  const loadingByType = ref<Partial<Record<MapType, boolean>>>({})
  const errorByType = ref<Partial<Record<MapType, ApiError | null>>>({})
  const inFlight = new Map<MapType, Promise<void>>()
  const inFlightId = new Map<MapType, number>()
  let fetchSeq = 0
  let generation = 0

  const countryDetail = ref<CountryDetailDto | null>(null)
  const detailLoading = ref(false)
  const detailError = ref<ApiError | null>(null)
  const detailCache = new Map<string, CountryDetailDto>()
  let detailRequest = 0

  /** `silent`: pré-carregamento; não registra erro (a tela não depende dele). */
  function fetchMap(mapType: MapType, options: { force?: boolean; silent?: boolean } = {}) {
    if (data.value[mapType] && !options.force) return Promise.resolve()
    const existing = inFlight.get(mapType)
    if (existing) return existing
    const gen = generation
    loadingByType.value = { ...loadingByType.value, [mapType]: true }
    if (!options.silent) errorByType.value = { ...errorByType.value, [mapType]: null }
    const id = ++fetchSeq
    const promise = (async () => {
      try {
        const result = await api.maps.get(mapType)
        if (gen === generation) data.value = { ...data.value, [mapType]: result }
      } catch (e) {
        if (!options.silent && gen === generation)
          errorByType.value = { ...errorByType.value, [mapType]: toApiError(e) }
      } finally {
        loadingByType.value = { ...loadingByType.value, [mapType]: false }
        if (inFlightId.get(mapType) === id) {
          inFlight.delete(mapType)
          inFlightId.delete(mapType)
        }
      }
    })()
    inFlight.set(mapType, promise)
    inFlightId.set(mapType, id)
    return promise
  }

  async function fetchCountryDetail(mapType: MapType, iso: string, force = false) {
    const key = `${mapType}:${iso}`
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
      const result = await api.maps.getCountryDetail(mapType, iso)
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

  return {
    data,
    loadingByType,
    errorByType,
    countryDetail,
    detailLoading,
    detailError,
    fetchMap,
    fetchCountryDetail,
    clearCountryDetail,
    invalidate,
  }
})
