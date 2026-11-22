import type { AxiosInstance } from 'axios'
import type {
  AuthorDetailsDto,
  AuthorSuggestionDto,
  BookDetailsDto,
  UpdateAuthorCountriesRequest,
} from '@/types/api'

export interface AuthorsApi {
  /** v1.10 — autocomplete: `q` ≥ 2 caracteres, `limit` 1–20. `signal` cancela a requisição. */
  search(q: string, limit?: number, signal?: AbortSignal): Promise<AuthorSuggestionDto[]>
  get(id: string): Promise<AuthorDetailsDto>
  getBooks(id: string): Promise<BookDetailsDto[]>
  updateCountries(id: string, request: UpdateAuthorCountriesRequest): Promise<AuthorDetailsDto>
}

export function createHttpAuthorsApi(http: AxiosInstance): AuthorsApi {
  return {
    async search(q, limit = 8, signal) {
      return (await http.get<AuthorSuggestionDto[]>('/authors', { params: { q, limit }, signal }))
        .data
    },
    async get(id) {
      return (await http.get<AuthorDetailsDto>(`/authors/${encodeURIComponent(id)}`)).data
    },
    async getBooks(id) {
      return (await http.get<BookDetailsDto[]>(`/authors/${encodeURIComponent(id)}/books`)).data
    },
    async updateCountries(id, request) {
      return (
        await http.put<AuthorDetailsDto>(`/authors/${encodeURIComponent(id)}/countries`, request)
      ).data
    },
  }
}
