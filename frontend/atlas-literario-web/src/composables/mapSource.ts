import { computed, type Ref } from 'vue'
import type { ApiError } from '@/api/errors'
import { mapKey, useMapsStore } from '@/stores/maps'
import { profileMapKey, useProfileMapsStore } from '@/stores/profileMaps'
import type {
  CountryDetailDto,
  MapDataDto,
  MapStatusFilter,
  MapType,
  ProfileCountryDetailDto,
} from '@/types/api'

/** Detalhe de país exibido no painel: do próprio usuário ou de um perfil público. */
export type AnyCountryDetail = CountryDetailDto | ProfileCountryDetailDto

/**
 * Fonte de dados abstrata do mapa (própria biblioteca ou perfil de terceiros). `MapExplorer` e
 * `useMapView` só conhecem esta interface, então a mesma tela serve aos dois contextos.
 * Todos os leitores são reativos (leem estado de stores Pinia).
 */
export interface MapSource {
  /** Mapa carregado para (tipo, status), ou `null`. Fontes sem filtro ignoram o status. */
  data(mapType: MapType, status: MapStatusFilter): MapDataDto | null
  loading(mapType: MapType, status: MapStatusFilter): boolean
  error(mapType: MapType, status: MapStatusFilter): ApiError | null
  fetchMap(
    mapType: MapType,
    status: MapStatusFilter,
    options?: { force?: boolean; silent?: boolean },
  ): Promise<void>
  detail(): AnyCountryDetail | null
  detailLoading(): boolean
  detailError(): ApiError | null
  fetchDetail(
    mapType: MapType,
    iso: string,
    status: MapStatusFilter,
    force?: boolean,
  ): Promise<void>
  clearDetail(): void
}

/** Mapa da própria biblioteca (`/maps/...`, com filtro de status). */
export function useOwnMapSource(): MapSource {
  const maps = useMapsStore()
  return {
    data: (type, status) => maps.data[mapKey(type, status)] ?? null,
    loading: (type, status) => !!maps.loadingByKey[mapKey(type, status)],
    error: (type, status) => maps.errorByKey[mapKey(type, status)] ?? null,
    fetchMap: (type, status, options) => maps.fetchMap(type, status, options),
    detail: () => maps.countryDetail,
    detailLoading: () => maps.detailLoading,
    detailError: () => maps.detailError,
    fetchDetail: (type, iso, status, force) => maps.fetchCountryDetail(type, iso, status, force),
    clearDetail: () => maps.clearCountryDetail(),
  }
}

/** Mapa de um perfil público (`/profiles/{handle}/maps/...`): sempre `Read`, sem filtro. */
export function useProfileMapSource(handle: Ref<string>): MapSource {
  const maps = useProfileMapsStore()
  return {
    data: (type) => maps.data[profileMapKey(handle.value, type)] ?? null,
    loading: (type) => !!maps.loadingByKey[profileMapKey(handle.value, type)],
    error: (type) => maps.errorByKey[profileMapKey(handle.value, type)] ?? null,
    fetchMap: (type, _status, options) => maps.fetchMap(handle.value, type, options),
    detail: () => maps.countryDetail,
    detailLoading: () => maps.detailLoading,
    detailError: () => maps.detailError,
    fetchDetail: (type, iso, _status, force) =>
      maps.fetchCountryDetail(handle.value, type, iso, force),
    clearDetail: () => maps.clearCountryDetail(),
  }
}

/** Atalho para derivar leituras reativas de uma fonte. */
export function useSourceReads(
  source: MapSource,
  mapType: Ref<MapType>,
  status: Ref<MapStatusFilter>,
) {
  return {
    current: computed(() => source.data(mapType.value, status.value)),
    loading: computed(() => source.loading(mapType.value, status.value)),
    error: computed(() => source.error(mapType.value, status.value)),
  }
}
