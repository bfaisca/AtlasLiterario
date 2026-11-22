import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api } from '@/api'
import { toApiError, type ApiError } from '@/api/errors'
import type { MapDataDto, MapType, ProfileCountryDetailDto } from '@/types/api'
import { normalizeHandle } from '@/utils/handle'

/** Chave de cache dos mapas de um perfil: apelido + tipo (o mapa de terceiros é sempre `Read`). */
export function profileMapKey(handle: string, mapType: MapType): string {
  return `${normalizeHandle(handle)}:${mapType}`
}

/**
 * Mapas de perfis de terceiros (`/profiles/{handle}/maps/...`) e o detalhe do país selecionado.
 * Mesmas garantias do store de mapas próprios: cache por (perfil, tipo), respostas de detalhe fora
 * de ordem descartadas e `reset()` descarta tudo, inclusive requisições em voo.
 */
export const useProfileMapsStore = defineStore('profileMaps', () => {
  const data = ref<Record<string, MapDataDto | undefined>>({})
  const loadingByKey = ref<Record<string, boolean | undefined>>({})
  const errorByKey = ref<Record<string, ApiError | null | undefined>>({})
  const inFlight = new Map<string, Promise<void>>()
  let generation = 0

  const countryDetail = ref<ProfileCountryDetailDto | null>(null)
  const detailLoading = ref(false)
  const detailError = ref<ApiError | null>(null)
  const detailCache = new Map<string, ProfileCountryDetailDto>()
  let detailRequest = 0

  /** `silent`: pré-carregamento; não registra erro (a tela não depende dele). */
  function fetchMap(
    handle: string,
    mapType: MapType,
    options: { force?: boolean; silent?: boolean } = {},
  ) {
    const key = profileMapKey(handle, mapType)
    if (data.value[key] && !options.force) return Promise.resolve()
    const existing = inFlight.get(key)
    if (existing) return existing
    const gen = generation
    loadingByKey.value = { ...loadingByKey.value, [key]: true }
    if (!options.silent) errorByKey.value = { ...errorByKey.value, [key]: null }
    const promise = (async () => {
      try {
        const result = await api.profiles.map(normalizeHandle(handle), mapType)
        if (gen === generation) data.value = { ...data.value, [key]: result }
      } catch (e) {
        if (!options.silent && gen === generation)
          errorByKey.value = { ...errorByKey.value, [key]: toApiError(e) }
      } finally {
        if (gen === generation) {
          loadingByKey.value = { ...loadingByKey.value, [key]: false }
          inFlight.delete(key)
        }
      }
    })()
    inFlight.set(key, promise)
    return promise
  }

  async function fetchCountryDetail(handle: string, mapType: MapType, iso: string, force = false) {
    const key = `${profileMapKey(handle, mapType)}:${iso}`
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
      const result = await api.profiles.countryDetail(normalizeHandle(handle), mapType, iso)
      if (request !== detailRequest || gen !== generation) return
      detailCache.set(key, result)
      countryDetail.value = result
    } catch (e) {
      if (request !== detailRequest || gen !== generation) return
      detailError.value = toApiError(e)
    } finally {
      if (request === detailRequest && gen === generation) detailLoading.value = false
    }
  }

  function clearCountryDetail() {
    detailRequest++
    countryDetail.value = null
    detailError.value = null
    detailLoading.value = false
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
    reset,
  }
})
