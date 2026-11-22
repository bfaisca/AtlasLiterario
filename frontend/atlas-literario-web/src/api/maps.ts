import type { AxiosInstance } from 'axios'
import type {
  CountryDetailDto,
  CreateMapShareRequest,
  MapDataDto,
  MapShareDto,
  MapType,
  PublicMapDto,
} from '@/types/api'
import { toRouteMapType } from '@/utils/mapType'

export interface MapsApi {
  get(mapType: MapType): Promise<MapDataDto>
  getCountryDetail(mapType: MapType, iso: string): Promise<CountryDetailDto>
  createShare(request: CreateMapShareRequest): Promise<MapShareDto>
  listShares(): Promise<MapShareDto[]>
  revokeShare(token: string): Promise<void>
  getPublic(token: string): Promise<PublicMapDto>
}

export function createHttpMapsApi(http: AxiosInstance): MapsApi {
  return {
    async get(mapType) {
      return (await http.get<MapDataDto>(`/maps/${toRouteMapType(mapType)}`)).data
    },
    async getCountryDetail(mapType, iso) {
      return (
        await http.get<CountryDetailDto>(
          `/maps/${toRouteMapType(mapType)}/countries/${encodeURIComponent(iso)}`,
        )
      ).data
    },
    async createShare(request) {
      return (await http.post<MapShareDto>('/maps/share', request)).data
    },
    async listShares() {
      return (await http.get<MapShareDto[]>('/maps/share')).data
    },
    async revokeShare(token) {
      await http.delete(`/maps/share/${encodeURIComponent(token)}`)
    },
    async getPublic(token) {
      return (await http.get<PublicMapDto>(`/public/maps/${encodeURIComponent(token)}`)).data
    },
  }
}
