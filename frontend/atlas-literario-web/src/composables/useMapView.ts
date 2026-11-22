import { computed, ref, watch, type Ref } from 'vue'
import type { MapDataDto, MapStatusFilter, MapType } from '@/types/api'
import type { CountryHoverEvent } from '@/components/map/types'
import { useSourceReads, type MapSource } from './mapSource'

export interface MapViewOptions {
  source: MapSource
  mapType: Ref<MapType>
  status: Ref<MapStatusFilter>
  /** País pedido (ex.: `?country=` da URL), já normalizado; só vira seleção se estiver no mapa. */
  requestedIso: Ref<string | null>
  /** Aplica a seleção (ex.: atualiza a URL). `null` limpa. */
  onSelect: (iso: string | null) => void
}

/**
 * Lógica de visualização de um mapa a partir de uma `MapSource` (não conhece rotas nem stores):
 * carrega o mapa, pré-carrega o outro tipo, valida o país selecionado e carrega o seu detalhe.
 */
export function useMapView(options: MapViewOptions) {
  const { source, mapType, status, requestedIso, onSelect } = options
  const { current, loading, error } = useSourceReads(source, mapType, status)

  const hover = ref<CountryHoverEvent | null>(null)
  const otherType = computed<MapType>(() => (mapType.value === 'Authors' ? 'Origins' : 'Authors'))

  /** Durante a troca de mapa o desenho anterior permanece até o novo chegar (sem piscar). */
  const shown = ref<MapDataDto | null>(null)
  watch(
    current,
    (value) => {
      if (value) shown.value = value
    },
    { immediate: true },
  )

  watch(
    [mapType, status],
    ([type, filter]) => {
      hover.value = null
      void source.fetchMap(type, filter)
    },
    { immediate: true },
  )
  // Pré-carrega o outro mapa (mesmo status): a alternância fica instantânea e a seleção pode ser validada.
  watch(current, (value) => {
    if (value) void source.fetchMap(otherType.value, status.value, { silent: true })
  })

  /** Ao trocar o status o painel permanece (com o resumo anterior) até o novo mapa chegar. */
  const selectionSource = computed(() => current.value ?? (loading.value ? shown.value : null))
  const selectedDatum = computed(
    () => selectionSource.value?.countries.find((c) => c.iso === requestedIso.value) ?? null,
  )
  /** Só há seleção válida (e painel) para países explorados do mapa atual. */
  const selectedIso = computed(() => selectedDatum.value?.iso ?? null)

  // Descarta um país pedido que não existe no mapa carregado (inválido ou sem livros neste mapa).
  watch([current, requestedIso], ([data, iso]) => {
    if (data && iso && !data.countries.some((c) => c.iso === iso)) onSelect(null)
  })

  watch(
    [mapType, status, selectedIso],
    ([type, filter, iso]) => {
      if (iso) void source.fetchDetail(type, iso, filter)
      else source.clearDetail()
    },
    { immediate: true },
  )

  const detail = computed(() => {
    const value = source.detail()
    return value?.iso === selectedIso.value &&
      value?.mapType === mapType.value &&
      value?.status === status.value
      ? value
      : null
  })

  function onMapSelect(iso: string | null) {
    const explored = iso && current.value?.countries.some((c) => c.iso === iso)
    if (!explored) return onSelect(null)
    onSelect(iso === selectedIso.value ? null : iso)
  }

  const hasNoBooks = computed(() => current.value !== null && current.value.totalReadBooks === 0)

  function retryMap() {
    void source.fetchMap(mapType.value, status.value, { force: true })
  }
  function retryDetail() {
    if (selectedIso.value)
      void source.fetchDetail(mapType.value, selectedIso.value, status.value, true)
  }

  return {
    current,
    shown,
    loading,
    error,
    hover,
    selectedDatum,
    selectedIso,
    detail,
    detailLoading: computed(() => source.detailLoading()),
    detailError: computed(() => source.detailError()),
    hasNoBooks,
    onMapSelect,
    retryMap,
    retryDetail,
  }
}
