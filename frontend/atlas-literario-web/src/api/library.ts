import type { AxiosInstance } from 'axios'
import { SLOW_REQUEST_TIMEOUT_MS } from './timeouts'
import type {
  AddToLibraryRequest,
  LibraryItemDto,
  LibraryQueryParams,
  PagedResult,
  UpdateLibraryItemRequest,
} from '@/types/api'

export interface LibraryApi {
  list(query?: LibraryQueryParams): Promise<PagedResult<LibraryItemDto>>
  get(id: string): Promise<LibraryItemDto>
  add(request: AddToLibraryRequest): Promise<LibraryItemDto>
  update(id: string, request: UpdateLibraryItemRequest): Promise<LibraryItemDto>
  remove(id: string): Promise<void>
}

export function createHttpLibraryApi(http: AxiosInstance): LibraryApi {
  return {
    async list(query = {}) {
      return (await http.get<PagedResult<LibraryItemDto>>('/library', { params: query })).data
    },
    async get(id) {
      return (await http.get<LibraryItemDto>(`/library/${encodeURIComponent(id)}`)).data
    },
    async add(request) {
      return (
        await http.post<LibraryItemDto>('/library', request, { timeout: SLOW_REQUEST_TIMEOUT_MS })
      ).data
    },
    async update(id, request) {
      return (await http.put<LibraryItemDto>(`/library/${encodeURIComponent(id)}`, request)).data
    },
    async remove(id) {
      await http.delete(`/library/${encodeURIComponent(id)}`)
    },
  }
}
