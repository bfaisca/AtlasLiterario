import type { BookDetailsDto, CreateBookRequest } from '@/types/api'
import { isValidIsbn10, isValidIsbn13, normalizeIsbn } from './isbn'

/** Rascunho do formulário bibliográfico (cadastro manual e correção). */
export interface BookDraft {
  title: string
  subtitle: string
  authorNames: string[]
  /** Alinhado a `authorNames`: id do autor escolhido no autocomplete; null = nome digitado livremente. */
  authorIds: (string | null)[]
  publisher: string
  publicationYear: string
  isbn13: string
  isbn10: string
  originalLanguage: string
  genres: string
  description: string
  coverUrl: string
  originCountryIsos: string[]
}

export type BookField =
  'title' | 'authorNames' | 'publicationYear' | 'isbn13' | 'isbn10' | 'coverUrl'
export type BookErrors = Partial<Record<BookField, string>>

export function emptyBookDraft(): BookDraft {
  return {
    title: '',
    subtitle: '',
    authorNames: [''],
    authorIds: [null],
    publisher: '',
    publicationYear: '',
    isbn13: '',
    isbn10: '',
    originalLanguage: '',
    genres: '',
    description: '',
    coverUrl: '',
    originCountryIsos: [],
  }
}

export function draftFromBook(book: BookDetailsDto): BookDraft {
  return {
    title: book.title,
    subtitle: book.subtitle ?? '',
    authorNames: book.authors.length ? book.authors.map((a) => a.name) : [''],
    // Sem escolha no autocomplete não há id: o backend resolve por nome, como antes.
    authorIds: book.authors.length ? book.authors.map(() => null) : [null],
    publisher: book.publisher ?? '',
    publicationYear: book.publicationYear?.toString() ?? '',
    isbn13: book.isbn13 ?? '',
    isbn10: book.isbn10 ?? '',
    originalLanguage: book.originalLanguage ?? '',
    genres: book.genres.join(', '),
    description: book.description ?? '',
    coverUrl: book.coverUrl ?? '',
    originCountryIsos: book.originCountries.map((o) => o.countryIso),
  }
}

export function validateBook(draft: BookDraft, currentYear = new Date().getFullYear()): BookErrors {
  const errors: BookErrors = {}
  if (!draft.title.trim()) errors.title = 'Informe o título.'
  if (!draft.authorNames.some((n) => n.trim())) errors.authorNames = 'Informe ao menos um autor.'
  if (draft.publicationYear.trim()) {
    const year = Number(draft.publicationYear)
    if (!Number.isInteger(year) || year < -3000 || year > currentYear + 1) {
      errors.publicationYear = 'Ano inválido.'
    }
  }
  if (draft.isbn13.trim() && !isValidIsbn13(draft.isbn13)) errors.isbn13 = 'ISBN-13 inválido.'
  if (draft.isbn10.trim() && !isValidIsbn10(draft.isbn10)) errors.isbn10 = 'ISBN-10 inválido.'
  // O coverUrl interno (`/api/books/{id}/cover?v=…`) volta como veio; só URLs externas exigem https.
  if (
    draft.coverUrl.trim() &&
    !isUploadedCoverUrl(draft.coverUrl.trim()) &&
    !/^https:\/\//i.test(draft.coverUrl.trim())
  ) {
    errors.coverUrl = 'Use um endereço https://.'
  }
  return errors
}

/**
 * Converte para `CreateBookRequest`. `includeOrigins=false` omite `originCountryIsos`
 * (edição bibliográfica não deve tocar nas origens; essas têm endpoint próprio).
 */
export function toCreateBookRequest(draft: BookDraft, includeOrigins: boolean): CreateBookRequest {
  const text = (value: string) => value.trim() || null
  const request: CreateBookRequest = {
    title: draft.title.trim(),
    subtitle: text(draft.subtitle),
    isbn13: draft.isbn13.trim() ? normalizeIsbn(draft.isbn13) : null,
    isbn10: draft.isbn10.trim() ? normalizeIsbn(draft.isbn10) : null,
    publisher: text(draft.publisher),
    publicationYear: draft.publicationYear.trim() ? Number(draft.publicationYear) : null,
    description: text(draft.description),
    coverUrl: text(draft.coverUrl),
    originalLanguage: text(draft.originalLanguage),
    genres: draft.genres
      .split(',')
      .map((g) => g.trim())
      .filter(Boolean),
    authorNames: [],
  }
  // Nomes e ids andam juntos: descarta vazios e duplicados (mesmo id ou mesmo nome) mantendo o alinhamento.
  const authorIds: (string | null)[] = []
  const seenNames = new Set<string>()
  const seenIds = new Set<string>()
  draft.authorNames.forEach((raw, index) => {
    const name = raw.trim()
    const id = draft.authorIds[index] ?? null
    if (!name || seenNames.has(name) || (id && seenIds.has(id))) return
    seenNames.add(name)
    if (id) seenIds.add(id)
    request.authorNames.push(name)
    authorIds.push(id)
  })
  // Só envia `authorIds` quando houve escolha (contrato v1.10: opcional).
  if (authorIds.some(Boolean)) request.authorIds = authorIds
  if (includeOrigins) request.originCountryIsos = [...draft.originCountryIsos]
  return request
}

/** Capa enviada pelo usuário (caminho interno da API) ou, nos mocks, data URL. */
export function isUploadedCoverUrl(url: string | null | undefined): boolean {
  return !!url && (url.startsWith('/api/books/') || url.startsWith('data:image/'))
}
