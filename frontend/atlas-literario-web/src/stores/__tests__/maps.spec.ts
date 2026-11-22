import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { ApiError } from '@/api/errors'
import type { CountryDetailDto, MapDataDto, MapType } from '@/types/api'
import { useMapsStore } from '../maps'

const mapsApi = vi.hoisted(() => ({ get: vi.fn(), getCountryDetail: vi.fn() }))
vi.mock('@/api', () => ({ api: { maps: mapsApi } }))

const mapData = (mapType: MapType, max = 3): MapDataDto => ({
  mapType,
  totalReadBooks: 4,
  exploredCountries: 1,
  maxBookCount: max,
  unknownBookCount: 0,
  countries: [
    { iso: 'BRA', name: 'Brasil', continent: 'SouthAmerica', bookCount: max, authorCount: 1 },
  ],
})
const detail = (iso: string, mapType: MapType = 'Authors'): CountryDetailDto => ({
  mapType,
  iso,
  name: iso,
  continent: 'Europe',
  bookCount: 1,
  authorCount: 1,
  books: [],
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
    expect(store.data.Authors?.mapType).toBe('Authors')
    expect(store.data.Origins).toBeUndefined()
    expect(store.errorByType.Origins?.status).toBe(501)
    expect(store.errorByType.Authors).toBeNull()
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
    await store.fetchMap('Origins', { silent: true })
    expect(store.errorByType.Origins).toBeUndefined()
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
    await store.fetchCountryDetail('Authors', 'FRA', true)
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
    expect(store.data.Authors).toBeUndefined()
  })
})
