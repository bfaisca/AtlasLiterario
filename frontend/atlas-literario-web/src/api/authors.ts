import type { AxiosInstance } from 'axios'
import type { AuthorDetailsDto, BookDetailsDto, UpdateAuthorCountriesRequest } from '@/types/api'

export interface AuthorsApi {
  get(id: string): Promise<AuthorDetailsDto>
  getBooks(id: string): Promise<BookDetailsDto[]>
  updateCountries(id: string, request: UpdateAuthorCountriesRequest): Promise<AuthorDetailsDto>
}

export function createHttpAuthorsApi(http: AxiosInstance): AuthorsApi {
  return {
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
