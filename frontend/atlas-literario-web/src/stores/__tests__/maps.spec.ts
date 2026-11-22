import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { ApiError } from '@/api/errors'
import type { CountryDetailDto, MapDataDto, MapStatusFilter, MapType } from '@/types/api'
import { mapKey, useMapsStore } from '../maps'

const mapsApi = vi.hoisted(() => ({ get: vi.fn(), getCountryDetail: vi.fn() }))
vi.mock('@/api', () => ({ api: { maps: mapsApi } }))

const mapData = (mapType: MapType, max = 3, status: MapStatusFilter = 'Read'): MapDataDto => ({
  mapType,
  status,
  totalReadBooks: 4,
  exploredCountries: 1,
  maxBookCount: max,
  unknownBookCount: 0,
  countries: [
    { iso: 'BRA', name: 'Brasil', continent: 'SouthAmerica', bookCount: max, authorCount: 1 },
  ],
})
const detail = (
  iso: string,
  mapType: MapType = 'Authors',
  status: MapStatusFilter = 'Read',
): CountryDetailDto => ({
  mapType,
  status,
  iso,
  name: iso,
  continent: 'Europe',
  bookCount: 1,
  authorCount: 1,
  books: [],
  authors: [],
})
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}

describe('maps store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    mapsApi.get.mockReset()
    mapsApi.getCountryDetail.mockReset()
  })

  it('mantém os dois mapas independentes (cache e erro por tipo)', async () => {
    mapsApi.get.mockImplementation(async (type: MapType) => {
      if (type === 'Origins') throw new ApiError({ status: 501, title: 'x' })
      return mapData(type)
    })
    const store = useMapsStore()
    await store.fetchMap('Authors')
    await store.fetchMap('Origins')
    expect(store.data[mapKey('Authors')]?.mapType).toBe('Authors')
    expect(store.data[mapKey('Origins')]).toBeUndefined()
    expect(store.errorByKey[mapKey('Origins')]?.status).toBe(501)
    expect(store.errorByKey[mapKey('Authors')]).toBeNull()
  })

  it('não repete requisição já em cache nem em andamento', async () => {
    const pending = deferred<MapDataDto>()
    mapsApi.get.mockReturnValue(pending.promise)
    const store = useMapsStore()
    const a = store.fetchMap('Authors')
    const b = store.fetchMap('Authors')
    pending.resolve(mapData('Authors'))
    await Promise.all([a, b])
    await store.fetchMap('Authors')
    expect(mapsApi.get).toHaveBeenCalledTimes(1)
  })

  it('pré-carregamento silencioso não registra erro', async () => {
    mapsApi.get.mockRejectedValue(new ApiError({ status: 500, title: 'x' }))
    const store = useMapsStore()
    await store.fetchMap('Origins', 'Read', { silent: true })
    expect(store.errorByKey[mapKey('Origins')]).toBeUndefined()
  })

  it('descarta resposta de detalhe antiga quando outro país foi selecionado', async () => {
    const first = deferred<CountryDetailDto>()
    mapsApi.getCountryDetail.mockReturnValueOnce(first.promise).mockResolvedValueOnce(detail('NOR'))
    const store = useMapsStore()
    const slow = store.fetchCountryDetail('Authors', 'FRA')
    await store.fetchCountryDetail('Authors', 'NOR')
    first.resolve(detail('FRA'))
    await slow
    expect(store.countryDetail?.iso).toBe('NOR')
    expect(store.detailLoading).toBe(false)
  })

  it('guarda erro do detalhe e permite nova tentativa', async () => {
    mapsApi.getCountryDetail
      .mockRejectedValueOnce(new ApiError({ status: 500, title: 'x' }))
      .mockResolvedValueOnce(detail('FRA'))
    const store = useMapsStore()
    await store.fetchCountryDetail('Authors', 'FRA')
    expect(store.detailError?.status).toBe(500)
    expect(store.countryDetail).toBeNull()
    await store.fetchCountryDetail('Authors', 'FRA', 'Read', true)
    expect(store.detailError).toBeNull()
    expect(store.countryDetail?.iso).toBe('FRA')
  })

  it('usa cache do detalhe por tipo+país e o limpa em invalidate()', async () => {
    mapsApi.getCountryDetail.mockImplementation(async (t: MapType, iso: string) => detail(iso, t))
    const store = useMapsStore()
    await store.fetchCountryDetail('Authors', 'FRA')
    await store.fetchCountryDetail('Authors', 'FRA')
    await store.fetchCountryDetail('Origins', 'FRA')
    expect(mapsApi.getCountryDetail).toHaveBeenCalledTimes(2)
    store.invalidate()
    await store.fetchCountryDetail('Authors', 'FRA')
    expect(mapsApi.getCountryDetail).toHaveBeenCalledTimes(3)
  })

  it('invalidate() esvazia os mapas e ignora resposta que chega depois', async () => {
    const pending = deferred<MapDataDto>()
    mapsApi.get.mockReturnValueOnce(pending.promise)
    const store = useMapsStore()
    const request = store.fetchMap('Authors')
    store.invalidate()
    pending.resolve(mapData('Authors'))
    await request
    expect(store.data[mapKey('Authors')]).toBeUndefined()
  })

  it('chama a API sem `status` no padrão (Read) e com `status` nos demais', async () => {
    mapsApi.get.mockImplementation(async (t: MapType, status?: MapStatusFilter) =>
      mapData(t, 3, status ?? 'Read'),
    )
    const store = useMapsStore()
    await store.fetchMap('Authors')
    expect(mapsApi.get).toHaveBeenLastCalledWith('Authors')
    await store.fetchMap('Authors', 'WantToRead')
    expect(mapsApi.get).toHaveBeenLastCalledWith('Authors', 'WantToRead')
    await store.fetchMap('Authors', 'All')
    expect(mapsApi.get).toHaveBeenLastCalledWith('Authors', 'All')
  })

  it('guarda mapas por (tipo, status) sem misturar caches', async () => {
    mapsApi.get.mockImplementation(async (t: MapType, status?: MapStatusFilter) =>
      mapData(t, status === 'Reading' ? 7 : 3, status ?? 'Read'),
    )
    const store = useMapsStore()
    await store.fetchMap('Authors')
    await store.fetchMap('Authors', 'Reading')
    await store.fetchMap('Authors', 'Reading') // cache: sem nova chamada
    expect(mapsApi.get).toHaveBeenCalledTimes(2)
    expect(store.data[mapKey('Authors', 'Read')]?.maxBookCount).toBe(3)
    expect(store.data[mapKey('Authors', 'Reading')]?.maxBookCount).toBe(7)
    expect(store.data[mapKey('Origins', 'Reading')]).toBeUndefined()
  })

  it('erro de um status não afeta o mapa já carregado de outro', async () => {
    mapsApi.get.mockImplementation(async (t: MapType, status?: MapStatusFilter) => {
      if (status === 'Abandoned') throw new ApiError({ status: 500, title: 'x' })
      return mapData(t, 3, status ?? 'Read')
    })
    const store = useMapsStore()
    await store.fetchMap('Authors')
    await store.fetchMap('Authors', 'Abandoned')
    expect(store.errorByKey[mapKey('Authors', 'Abandoned')]?.status).toBe(500)
    expect(store.errorByKey[mapKey('Authors')]).toBeNull()
    expect(store.data[mapKey('Authors')]).toBeDefined()
  })

  it('detalhe do país: cache por status e chamada com status só quando difere de Read', async () => {
    mapsApi.getCountryDetail.mockImplementation(
      async (t: MapType, iso: string, status?: MapStatusFilter) => detail(iso, t, status ?? 'Read'),
    )
    const store = useMapsStore()
    await store.fetchCountryDetail('Authors', 'FRA')
    expect(mapsApi.getCountryDetail).toHaveBeenLastCalledWith('Authors', 'FRA')
    await store.fetchCountryDetail('Authors', 'FRA', 'All')
    expect(mapsApi.getCountryDetail).toHaveBeenLastCalledWith('Authors', 'FRA', 'All')
    expect(store.countryDetail?.status).toBe('All')
    await store.fetchCountryDetail('Authors', 'FRA') // volta ao cache de Read
    expect(mapsApi.getCountryDetail).toHaveBeenCalledTimes(2)
    expect(store.countryDetail?.status).toBe('Read')
  })
})
