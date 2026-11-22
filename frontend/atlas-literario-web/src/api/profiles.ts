import type { AxiosInstance } from 'axios'
import type {
  MapDataDto,
  MapType,
  PagedResult,
  ProfileCountryDetailDto,
  ProfileDto,
  ProfileSummaryDto,
} from '@/types/api'
import { toRouteMapType } from '@/utils/mapType'

export interface PageParams {
  page?: number
  pageSize?: number
}

/** Perfis públicos e seguidores (contrato v1.12, §8.1). Todas as rotas exigem autenticação. */
export interface ProfilesApi {
  /** `q` ≥ 2 caracteres, `limit` 1–20 (padrão 10). `signal` cancela a requisição. */
  search(q: string, limit?: number, signal?: AbortSignal): Promise<ProfileSummaryDto[]>
  /** `404 profile.not_found` também quando o perfil não é `Public`. */
  get(handle: string): Promise<ProfileDto>
  /** Idempotente. `400 follow.self`, `404 profile.not_found`. */
  follow(handle: string): Promise<void>
  /** Idempotente. */
  unfollow(handle: string): Promise<void>
  followers(handle: string, params?: PageParams): Promise<PagedResult<ProfileSummaryDto>>
  following(handle: string, params?: PageParams): Promise<PagedResult<ProfileSummaryDto>>
  /** Sempre só `Read` (sem `status`). */
  map(handle: string, mapType: MapType): Promise<MapDataDto>
  countryDetail(handle: string, mapType: MapType, iso: string): Promise<ProfileCountryDetailDto>
  meFollowers(params?: PageParams): Promise<PagedResult<ProfileSummaryDto>>
  meFollowing(params?: PageParams): Promise<PagedResult<ProfileSummaryDto>>
}

const base = (handle: string) => `/profiles/${encodeURIComponent(handle)}`
const mapPath = (handle: string, mapType: MapType) =>
  `${base(handle)}/maps/${toRouteMapType(mapType)}`

export function createHttpProfilesApi(http: AxiosInstance): ProfilesApi {
  return {
    async search(q, limit = 10, signal) {
      return (await http.get<ProfileSummaryDto[]>('/profiles', { params: { q, limit }, signal }))
        .data
    },
    async get(handle) {
      return (await http.get<ProfileDto>(base(handle))).data
    },
    async follow(handle) {
      await http.post(`${base(handle)}/follow`)
    },
    async unfollow(handle) {
      await http.delete(`${base(handle)}/follow`)
    },
    async followers(handle, params) {
      return (
        await http.get<PagedResult<ProfileSummaryDto>>(`${base(handle)}/followers`, { params })
      ).data
    },
    async following(handle, params) {
      return (
        await http.get<PagedResult<ProfileSummaryDto>>(`${base(handle)}/following`, { params })
      ).data
    },
    async map(handle, mapType) {
      return (await http.get<MapDataDto>(mapPath(handle, mapType))).data
    },
    async countryDetail(handle, mapType, iso) {
      return (
        await http.get<ProfileCountryDetailDto>(
          `${mapPath(handle, mapType)}/countries/${encodeURIComponent(iso)}`,
        )
      ).data
    },
    async meFollowers(params) {
      return (await http.get<PagedResult<ProfileSummaryDto>>('/me/followers', { params })).data
    },
    async meFollowing(params) {
      return (await http.get<PagedResult<ProfileSummaryDto>>('/me/following', { params })).data
    },
  }
}
