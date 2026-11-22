import type { AxiosInstance } from 'axios'
import { SLOW_REQUEST_TIMEOUT_MS } from './timeouts'
import type {
  BookDetailsDto,
  BookSearchParams,
  BookSearchResultDto,
  CreateBookRequest,
  UpdateOriginCountriesRequest,
} from '@/types/api'

export interface BooksApi {
  search(params: BookSearchParams): Promise<BookSearchResultDto[]>
  get(id: string): Promise<BookDetailsDto>
  create(request: CreateBookRequest): Promise<BookDetailsDto>
  update(id: string, request: CreateBookRequest): Promise<BookDetailsDto>
  updateOriginCountries(id: string, request: UpdateOriginCountriesRequest): Promise<BookDetailsDto>
  enrich(id: string): Promise<BookDetailsDto>
  /** multipart/form-data, campo `file` (JPEG/PNG/WebP até 2 MB). Exige o livro na biblioteca. */
  uploadCover(id: string, file: Blob, filename?: string): Promise<BookDetailsDto>
  removeCover(id: string): Promise<BookDetailsDto>
}

export function createHttpBooksApi(http: AxiosInstance): BooksApi {
  return {
    async search(params) {
      return (await http.get<BookSearchResultDto[]>('/books/search', { params })).data
    },
    async get(id) {
      return (await http.get<BookDetailsDto>(`/books/${encodeURIComponent(id)}`)).data
    },
    async create(request) {
      return (
        await http.post<BookDetailsDto>('/books', request, { timeout: SLOW_REQUEST_TIMEOUT_MS })
      ).data
    },
    async update(id, request) {
      return (await http.put<BookDetailsDto>(`/books/${encodeURIComponent(id)}`, request)).data
    },
    async updateOriginCountries(id, request) {
      return (
        await http.put<BookDetailsDto>(`/books/${encodeURIComponent(id)}/origin-countries`, request)
      ).data
    },
    async enrich(id) {
      return (
        await http.post<BookDetailsDto>(`/books/${encodeURIComponent(id)}/enrich`, undefined, {
          timeout: SLOW_REQUEST_TIMEOUT_MS,
        })
      ).data
    },
    async uploadCover(id, file, filename) {
      const form = new FormData()
      form.append('file', file, filename ?? (file instanceof File ? file.name : 'capa'))
      return (
        await http.post<BookDetailsDto>(`/books/${encodeURIComponent(id)}/cover`, form, {
          timeout: SLOW_REQUEST_TIMEOUT_MS,
        })
      ).data
    },
    async removeCover(id) {
      return (await http.delete<BookDetailsDto>(`/books/${encodeURIComponent(id)}/cover`)).data
    },
  }
}
