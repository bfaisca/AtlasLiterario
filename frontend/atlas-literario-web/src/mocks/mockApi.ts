/**
 * Implementação em memória da fachada `AtlasApi` (VITE_USE_MOCKS=true).
 * Mesma interface e mesmos erros (`ApiError` com `code`) da implementação HTTP, com um
 * "banco" normalizado (autores, livros, itens da biblioteca) para que correções de país,
 * importações e edições se reflitam em todas as telas. Os dados se perdem ao recarregar.
 *
 * Atalhos de demonstração: buscar "offline" simula 502 external.unavailable;
 * ISBN fictício 9780000000002 encontra "Torto Arado"; token de redefinição: "demo-token".
 */
import { ApiError } from '@/api/errors'
import { tokenManager } from '@/api/tokenManager'
import type { AtlasApi } from '@/api/types'
import type {
  AuthorCountryDto,
  AuthorDetailsDto,
  AuthResponse,
  BookDetailsDto,
  BookSearchResultDto,
  CreateBookRequest,
  GeoEnrichmentStatus,
  GeoRelationDto,
  GeoSource,
  LibraryItemDto,
  LibraryQueryParams,
  MapShareDto,
  PagedResult,
  ReadingStatus,
  UpdateLibraryItemRequest,
  UserProfileDto,
} from '@/types/api'
import { todayIsoDate } from '@/utils/format'
import { isValidIsbn10, isValidIsbn13, normalizeIsbn } from '@/utils/isbn'
import { isUploadedCoverUrl } from '@/utils/bookForm'
import { fromRouteMapType } from '@/utils/mapType'
import {
  computeCountryDetail,
  computeDashboard,
  computeMapData,
  computePassports,
  countriesOfBook,
} from './aggregation'
import { countryIndex, MOCK_COUNTRIES, MOCK_TOTAL_COUNTRIES } from './countries'
import {
  createMockLibrary,
  getMockCatalog,
  MOCK_DEMO_PASSWORD,
  MOCK_DEMO_USER,
  MOCK_MODERATOR_USER,
  toAuthorSummary,
} from './data'
import { externalCatalog, type ExternalSeed } from './externalCatalog'

const LATENCY_MS = 250

function delay<T>(value: T, ms = LATENCY_MS): Promise<T> {
  return new Promise((resolve) => setTimeout(() => resolve(value), ms))
}

function clone<T>(value: T): T {
  return structuredClone(value)
}

function readAsDataUrl(file: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(String(reader.result))
    reader.onerror = () => reject(reader.error)
    reader.readAsDataURL(file)
  })
}

function notFound(): ApiError {
  return new ApiError({ status: 404, title: 'Não encontrado' })
}

function validation(errors: Record<string, string[]>, code = 'validation.failed'): ApiError {
  return new ApiError({
    status: 400,
    title: 'Um ou mais erros de validação ocorreram.',
    code,
    errors,
  })
}

export function normalizeText(text: string): string {
  return text
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .toLowerCase()
    .trim()
}

function randomToken(prefix: string, userId: string): string {
  return `${prefix}.${userId}.${Math.random().toString(36).slice(2)}`
}

function genId(): string {
  return crypto.randomUUID()
}

/** Filtro/ordenação/paginação de GET /library (mesma semântica do contrato). */
export function queryLibrary(
  items: readonly LibraryItemDto[],
  query: LibraryQueryParams = {},
): PagedResult<LibraryItemDto> {
  let result = [...items]
  if (query.q) {
    const q = normalizeText(query.q)
    result = result.filter(
      (i) =>
        normalizeText(i.book.title).includes(q) ||
        i.book.authors.some((a) => normalizeText(a.name).includes(q)),
    )
  }
  if (query.status?.length) {
    const statuses = new Set<ReadingStatus>(query.status)
    result = result.filter((i) => statuses.has(i.status))
  }
  if (query.countryIso) {
    const mapType = fromRouteMapType(query.mapType)
    result = result.filter((i) => countriesOfBook(i.book, mapType).has(query.countryIso!))
  }
  if (query.genre) {
    const genre = normalizeText(query.genre)
    result = result.filter((i) => i.book.genres.some((g) => normalizeText(g) === genre))
  }
  if (query.minRating) result = result.filter((i) => (i.rating ?? 0) >= query.minRating!)

  const sort = query.sort ?? 'createdAt'
  const direction = (query.order ?? 'desc') === 'asc' ? 1 : -1
  const key = (i: LibraryItemDto): string | number => {
    switch (sort) {
      case 'title':
        return normalizeText(i.book.title)
      case 'author':
        return normalizeText(i.book.authors[0]?.name ?? '')
      case 'finishedDate':
        return i.finishedDate ?? ''
      case 'rating':
        return i.rating ?? 0
      case 'publicationYear':
        return i.book.publicationYear ?? 0
      default:
        return i.createdAt
    }
  }
  result.sort((a, b) => {
    const ka = key(a)
    const kb = key(b)
    return (ka < kb ? -1 : ka > kb ? 1 : 0) * direction || a.id.localeCompare(b.id)
  })

  const pageSize = Math.min(Math.max(query.pageSize ?? 20, 1), 100)
  const page = Math.max(query.page ?? 1, 1)
  const totalCount = result.length
  return {
    items: result.slice((page - 1) * pageSize, page * pageSize),
    page,
    pageSize,
    totalCount,
    totalPages: Math.ceil(totalCount / pageSize),
  }
}

/** Regras de leitura (business-rules §1), com os códigos de erro do contrato. */
export function applyReadingRules(
  request: UpdateLibraryItemRequest,
  today: string = todayIsoDate(),
): UpdateLibraryItemRequest {
  const errors: Record<string, string[]> = {}
  let code = 'validation.failed'
  if (request.rating !== null && (request.rating < 1 || request.rating > 5)) {
    errors.rating = ['A avaliação deve estar entre 1 e 5.']
    code = 'library.invalid_rating'
  }
  if (request.review && request.review.length > 10_000) {
    errors.review = ['A resenha deve ter no máximo 10.000 caracteres.']
    code = 'library.review_too_long'
  }
  // Ao virar Read sem data, preenche com hoje; ao sair de Read, limpa (sem leituras fantasmas).
  const finishedDate = request.status === 'Read' ? (request.finishedDate ?? today) : null
  if (request.startDate && finishedDate && request.startDate > finishedDate) {
    errors.startDate = ['A data de início deve ser anterior à data de conclusão.']
    code = 'library.invalid_dates'
  }
  if (Object.keys(errors).length) {
    throw validation(errors, Object.keys(errors).length === 1 ? code : 'validation.failed')
  }
  return { ...request, finishedDate }
}

type BookRecord = Omit<BookDetailsDto, 'authors'> & { authorIds: string[] }
type ItemRecord = Omit<LibraryItemDto, 'book'> & { bookId: string }

export function createMockApi(): AtlasApi {
  const countries = countryIndex()
  const users = new Map<string, { profile: UserProfileDto; password: string }>([
    [MOCK_DEMO_USER.id, { profile: { ...MOCK_DEMO_USER }, password: MOCK_DEMO_PASSWORD }],
    [MOCK_MODERATOR_USER.id, { profile: { ...MOCK_MODERATOR_USER }, password: MOCK_DEMO_PASSWORD }],
  ])
  const shares = new Map<string, MapShareDto & { userId: string }>()

  // ------------------------------------------------------------ "banco" normalizado
  const authors = new Map<string, AuthorDetailsDto>()
  const books = new Map<string, BookRecord>()
  const items = new Map<string, ItemRecord>()
  /** Livros dos cenários de lentidão que já expiraram uma vez. */
  const timedOutOnce = new Set<string>()
  const externals = externalCatalog()
  const pendingEnrichment = new Map<string, ExternalSeed>()

  const catalog = getMockCatalog()
  for (const author of Object.values(catalog.authors)) authors.set(author.id, clone(author))
  for (const book of Object.values(catalog.books)) {
    for (const summary of book.authors) {
      if (!authors.has(summary.id)) {
        authors.set(summary.id, {
          id: summary.id,
          name: summary.name,
          birthDate: null,
          countries: summary.nationalities.map((n) => ({ ...n, relationship: 'Citizenship' })),
          externalIds: [],
        })
      }
    }
    const { authors: bookAuthors, ...rest } = clone(book)
    books.set(book.id, { ...rest, authorIds: bookAuthors.map((a) => a.id) })
  }
  for (const item of createMockLibrary()) {
    const { book, ...rest } = item
    items.set(item.id, { ...rest, bookId: book.id })
  }

  function relation(iso: string, source: GeoSource, confidence: number): GeoRelationDto {
    const country = countries.get(iso)
    if (!country) throw validation({ countryIsos: [`País desconhecido: ${iso}`] })
    return {
      countryIso: iso,
      countryName: country.namePt,
      source,
      confidence,
      isVerified: source === 'Manual',
    }
  }

  function materializeBook(record: BookRecord): BookDetailsDto {
    const { authorIds, ...rest } = record
    return {
      ...rest,
      authors: authorIds.map((id) => toAuthorSummary(authors.get(id)!)),
    }
  }

  function materializeItem(record: ItemRecord): LibraryItemDto {
    const { bookId, ...rest } = record
    return { ...rest, book: materializeBook(books.get(bookId)!) }
  }

  function libraryItems(): LibraryItemDto[] {
    return [...items.values()].map(materializeItem)
  }

  function geoStatus(record: BookRecord): GeoEnrichmentStatus {
    if (pendingEnrichment.has(record.id)) return 'Pending'
    const authorsKnown = record.authorIds.every((id) =>
      authors.get(id)!.countries.some((c) => c.relationship === 'Citizenship'),
    )
    return record.originCountries.length && record.authorIds.length && authorsKnown
      ? 'Completed'
      : 'Partial'
  }

  function findOrCreateAuthor(name: string): AuthorDetailsDto {
    const key = normalizeText(name)
    const existing = [...authors.values()].find((a) => normalizeText(a.name) === key)
    if (existing) return existing
    const created: AuthorDetailsDto = {
      id: genId(),
      name: name.trim(),
      birthDate: null,
      countries: [],
      externalIds: [],
    }
    authors.set(created.id, created)
    return created
  }

  function findByExternal(provider: string, externalId: string): BookRecord | undefined {
    return [...books.values()].find((b) =>
      b.externalIds.some((e) => e.provider === provider && e.externalId === externalId),
    )
  }

  /** Importa um resultado externo para o catálogo (geografia pendente, como no backend). */
  function importExternal(seed: ExternalSeed): BookRecord {
    const existing = findByExternal(seed.provider, seed.externalId)
    if (existing) return existing
    const record: BookRecord = {
      id: genId(),
      title: seed.title,
      subtitle: seed.subtitle ?? null,
      isbn13: seed.isbn13 ?? null,
      isbn10: null,
      publisher: seed.publisher,
      publicationYear: seed.year,
      description: seed.description,
      coverUrl: null,
      originalLanguage: seed.language,
      genres: [...seed.genres],
      authorIds: seed.authors.map((a) => findOrCreateAuthor(a.name).id),
      originCountries: [],
      geoEnrichmentStatus: 'Pending',
      externalIds: [{ provider: seed.provider, externalId: seed.externalId }],
    }
    books.set(record.id, record)
    pendingEnrichment.set(record.id, seed)
    return record
  }

  /** Simula o enriquecimento via Wikidata. Relações manuais nunca são sobrescritas. */
  function enrichBook(record: BookRecord): void {
    const seed = pendingEnrichment.get(record.id)
    pendingEnrichment.delete(record.id)
    if (seed) {
      for (const seedAuthor of seed.authors) {
        const author = findOrCreateAuthor(seedAuthor.name)
        const hasCitizenship = author.countries.some((c) => c.relationship === 'Citizenship')
        if (!hasCitizenship && seedAuthor.citizenships.length) {
          author.countries = [
            ...author.countries,
            ...seedAuthor.citizenships.map((iso): AuthorCountryDto => ({
              ...relation(iso, 'Wikidata', 0.9),
              relationship: 'Citizenship',
            })),
          ]
        }
      }
      if (!record.originCountries.some((o) => o.source === 'Manual')) {
        record.originCountries = seed.origins.map((iso) => relation(iso, 'Wikidata', 0.9))
      }
    }
    record.geoEnrichmentStatus = geoStatus(record)
  }

  function refreshStatuses() {
    for (const record of books.values()) record.geoEnrichmentStatus = geoStatus(record)
  }

  function validateBookRequest(request: CreateBookRequest): void {
    const errors: Record<string, string[]> = {}
    let code = 'validation.failed'
    if (!request.title?.trim()) errors.title = ['Informe o título.']
    const names = (request.authorNames ?? []).map((n) => n.trim()).filter(Boolean)
    if (!names.length) {
      errors.authorNames = ['Informe ao menos um autor.']
      code = 'author.invalid_name'
    }
    if (request.isbn13 && !isValidIsbn13(request.isbn13)) {
      errors.isbn13 = ['ISBN-13 inválido.']
      code = 'book.invalid_isbn13'
    }
    if (request.isbn10 && !isValidIsbn10(request.isbn10)) {
      errors.isbn10 = ['ISBN-10 inválido.']
      code = 'book.invalid_isbn10'
    }
    if (Object.keys(errors).length) {
      throw validation(errors, Object.keys(errors).length === 1 ? code : 'validation.failed')
    }
  }

  function applyBookRequest(record: BookRecord, request: CreateBookRequest): void {
    record.title = request.title.trim()
    record.subtitle = request.subtitle?.trim() || null
    record.isbn13 = request.isbn13 ? normalizeIsbn(request.isbn13) : null
    record.isbn10 = request.isbn10 ? normalizeIsbn(request.isbn10) : null
    record.publisher = request.publisher?.trim() || null
    record.publicationYear = request.publicationYear ?? null
    record.description = request.description?.trim() || null
    record.coverUrl = request.coverUrl?.trim() || null
    record.originalLanguage = request.originalLanguage?.trim() || null
    record.genres = (request.genres ?? []).map((g) => g.trim()).filter(Boolean)
    record.authorIds = request.authorNames
      .map((n) => n.trim())
      .filter(Boolean)
      .map((name) => findOrCreateAuthor(name).id)
    if (request.originCountryIsos != null) {
      record.originCountries = request.originCountryIsos.map((iso) => relation(iso, 'Manual', 1))
    }
  }

  function issueSession(userId: string): AuthResponse {
    const user = users.get(userId)
    if (!user) throw new ApiError({ status: 401, title: 'Não autenticado' })
    return {
      accessToken: randomToken('mock-access', userId),
      accessTokenExpiresAt: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
      refreshToken: randomToken('mock-refresh', userId),
      user: { ...user.profile },
    }
  }

  function currentUser(): { profile: UserProfileDto; password: string } {
    const userId = tokenManager.getAccessToken()?.split('.')[1]
    const user = userId ? users.get(userId) : undefined
    if (!user)
      throw new ApiError({ status: 401, title: 'Não autenticado', code: 'auth.unauthorized' })
    return user
  }

  function findItem(id: string): ItemRecord {
    currentUser()
    const item = items.get(id)
    if (!item) throw notFound()
    return item
  }

  /**
   * Regra de países (contrato v1.7 / business-rules §2): usuário comum só PREENCHE conjuntos vazios
   * (e com o livro na sua biblioteca); alterar/remover o que já consta exige `Moderator`.
   * O papel é lido do "banco" (não do token), como no backend.
   */
  function assertCanEditGeo(
    current: readonly unknown[],
    inLibraryForUser: boolean,
    notInLibraryCode: string,
  ) {
    const user = currentUser()
    if (users.get(user.profile.id)?.profile.role === 'Moderator') return
    if (current.length > 0) {
      throw new ApiError({
        status: 403,
        title: 'Acesso negado',
        code: 'geo.moderator_required',
      })
    }
    if (!inLibraryForUser) {
      throw new ApiError({ status: 403, title: 'Acesso negado', code: notInLibraryCode })
    }
  }

  function findBook(id: string): BookRecord {
    currentUser()
    const book = books.get(id)
    if (!book) throw notFound()
    return book
  }

  function matches(
    type: string,
    term: string,
    data: { title: string; authors: string[]; isbns: Array<string | null> },
  ) {
    const byTitle = normalizeText(data.title).includes(term)
    const byAuthor = data.authors.some((a) => normalizeText(a).includes(term))
    const isbnTerm = normalizeIsbn(term)
    const byIsbn =
      isbnTerm.length >= 10 && data.isbns.some((i) => i && normalizeIsbn(i) === isbnTerm)
    if (type === 'title') return byTitle
    if (type === 'author') return byAuthor
    if (type === 'isbn') return byIsbn
    return byTitle || byAuthor || byIsbn
  }

  const libraryItemIdOf = (bookId: string) =>
    [...items.values()].find((i) => i.bookId === bookId)?.id ?? null
  const inLibrary = (bookId: string) => libraryItemIdOf(bookId) !== null

  return {
    auth: {
      async register(request) {
        const email = request.email.trim().toLowerCase()
        if ([...users.values()].some((u) => u.profile.email === email)) {
          await delay(null)
          throw new ApiError({
            status: 409,
            title: 'E-mail já cadastrado',
            code: 'auth.email_taken',
          })
        }
        const profile: UserProfileDto = {
          id: genId(),
          name: request.name.trim(),
          email,
          mapVisibility: 'Private',
          role: 'User',
          createdAt: new Date().toISOString(),
        }
        users.set(profile.id, { profile, password: request.password })
        // Atalho de demonstração: e-mails "vazia…@" começam sem livros (testa os estados vazios).
        if (email.startsWith('vazia')) items.clear()
        return delay(issueSession(profile.id))
      },
      async login(request) {
        const email = request.email.trim().toLowerCase()
        const user = [...users.values()].find((u) => u.profile.email === email)
        await delay(null)
        if (!user || user.password !== request.password) {
          throw new ApiError({
            status: 401,
            title: 'Credenciais inválidas',
            code: 'auth.invalid_credentials',
          })
        }
        return issueSession(user.profile.id)
      },
      async refresh(request) {
        const [prefix, userId] = request.refreshToken.split('.')
        await delay(null, 100)
        if (prefix !== 'mock-refresh' || !userId || !users.has(userId)) {
          throw new ApiError({
            status: 401,
            title: 'Refresh token inválido',
            code: 'auth.invalid_refresh_token',
          })
        }
        return issueSession(userId)
      },
      async logout() {
        await delay(null, 50)
      },
      async forgotPassword() {
        await delay(null)
      },
      async resetPassword(request) {
        await delay(null)
        if (request.token !== 'demo-token') {
          throw new ApiError({
            status: 400,
            title: 'Token inválido',
            code: 'auth.invalid_reset_token',
          })
        }
      },
      async me() {
        return delay({ ...currentUser().profile })
      },
      async updateMe(request) {
        const user = currentUser()
        user.profile = {
          ...user.profile,
          name: request.name.trim(),
          mapVisibility: request.mapVisibility,
        }
        return delay({ ...user.profile })
      },
      async changePassword(request) {
        const user = currentUser()
        await delay(null)
        if (user.password !== request.currentPassword) {
          throw new ApiError({
            status: 400,
            title: 'Senha inválida',
            code: 'auth.invalid_password',
          })
        }
        user.password = request.newPassword
      },
    },

    books: {
      async search({ q, type = 'all', limit = 20 }) {
        currentUser()
        const term = normalizeText(q)
        if (term.includes('offline')) {
          await delay(null, 400)
          throw new ApiError({
            status: 502,
            title: 'Fonte externa indisponível',
            code: 'external.unavailable',
          })
        }
        const local: BookSearchResultDto[] = [...books.values()]
          .filter((b) =>
            matches(type, term, {
              title: b.title,
              authors: b.authorIds.map((id) => authors.get(id)!.name),
              isbns: [b.isbn13, b.isbn10],
            }),
          )
          .map((b) => ({
            resultKey: `local:${b.id}`,
            bookId: b.id,
            inLibrary: inLibrary(b.id),
            libraryItemId: libraryItemIdOf(b.id),
            title: b.title,
            subtitle: b.subtitle,
            authors: b.authorIds.map((id) => authors.get(id)!.name),
            publisher: b.publisher,
            publicationYear: b.publicationYear,
            isbn13: b.isbn13,
            isbn10: b.isbn10,
            coverUrl: b.coverUrl,
            description: b.description?.slice(0, 500) ?? null,
            source: { provider: 'Local', externalId: b.id },
          }))
        const external: BookSearchResultDto[] = externals
          .filter((seed) => !findByExternal(seed.provider, seed.externalId))
          .filter((seed) =>
            matches(type, term, {
              title: seed.title,
              authors: seed.authors.map((a) => a.name),
              isbns: [seed.isbn13 ?? null],
            }),
          )
          .map((seed) => ({
            resultKey: `${seed.provider}:${seed.externalId}`,
            bookId: null,
            inLibrary: false,
            libraryItemId: null,
            title: seed.title,
            subtitle: seed.subtitle ?? null,
            authors: seed.authors.map((a) => a.name),
            publisher: seed.publisher,
            publicationYear: seed.year,
            isbn13: seed.isbn13 ?? null,
            isbn10: null,
            coverUrl: null,
            description: seed.description.slice(0, 500),
            source: { provider: seed.provider, externalId: seed.externalId },
          }))
        return delay(clone([...local, ...external].slice(0, limit)), 450)
      },
      async get(id) {
        return delay(clone(materializeBook(findBook(id))))
      },
      async create(request) {
        currentUser()
        validateBookRequest(request)
        const record: BookRecord = {
          id: genId(),
          title: '',
          subtitle: null,
          isbn13: null,
          isbn10: null,
          publisher: null,
          publicationYear: null,
          description: null,
          coverUrl: null,
          originalLanguage: null,
          genres: [],
          authorIds: [],
          originCountries: [],
          geoEnrichmentStatus: 'Partial',
          externalIds: [],
        }
        applyBookRequest(record, { ...request, originCountryIsos: request.originCountryIsos ?? [] })
        if (record.isbn13 && [...books.values()].some((b) => b.isbn13 === record.isbn13)) {
          throw new ApiError({
            status: 409,
            title: 'ISBN já cadastrado',
            code: 'book.duplicate_isbn',
          })
        }
        books.set(record.id, record)
        record.geoEnrichmentStatus = geoStatus(record)
        return delay(clone(materializeBook(record)), 300)
      },
      async update(id, request) {
        const record = findBook(id)
        validateBookRequest(request)
        if (request.originCountryIsos != null) {
          assertCanEditGeo(record.originCountries, inLibrary(record.id), 'book.not_in_library')
        }
        applyBookRequest(record, request)
        refreshStatuses()
        return delay(clone(materializeBook(record)))
      },
      async updateOriginCountries(id, request) {
        const record = findBook(id)
        assertCanEditGeo(record.originCountries, inLibrary(record.id), 'book.not_in_library')
        record.originCountries = [...new Set(request.countryIsos)].map((iso) =>
          relation(iso, 'Manual', 1),
        )
        record.geoEnrichmentStatus = geoStatus(record)
        return delay(clone(materializeBook(record)))
      },
      async enrich(id) {
        const record = findBook(id)
        enrichBook(record)
        return delay(clone(materializeBook(record)), 700)
      },
      async uploadCover(id, file) {
        const record = findBook(id)
        // Contrato v1.6: exige o livro na biblioteca do usuário.
        if (!inLibrary(record.id)) {
          throw new ApiError({ status: 403, title: 'Acesso negado', code: 'book.not_in_library' })
        }
        const name = file instanceof File ? file.name : ''
        if (
          !['image/jpeg', 'image/png', 'image/webp'].includes(file.type) ||
          /invalido/i.test(name)
        ) {
          throw new ApiError({ status: 400, title: 'Tipo inválido', code: 'cover.invalid_type' })
        }
        if (file.size > 2 * 1024 * 1024 || /grande/i.test(name)) {
          throw new ApiError({
            status: 413,
            title: 'Imagem grande demais',
            code: 'cover.too_large',
          })
        }
        // Cenário de teste: o envio da capa falha (o livro permanece cadastrado).
        if (/falha/i.test(name)) {
          await delay(null, 600)
          throw new ApiError({ status: 500, title: 'Erro interno do servidor' })
        }
        // Os mocks não têm GET /books/{id}/cover: a "URL interna" simulada é um data URL.
        record.coverUrl = await readAsDataUrl(file)
        refreshStatuses()
        return delay(clone(materializeBook(record)), 500)
      },
      async removeCover(id) {
        const record = findBook(id)
        if (!isUploadedCoverUrl(record.coverUrl)) {
          throw new ApiError({ status: 404, title: 'Não encontrado', code: 'cover.not_found' })
        }
        record.coverUrl = null
        return delay(clone(materializeBook(record)))
      },
    },

    library: {
      async list(query) {
        currentUser()
        return delay(clone(queryLibrary(libraryItems(), query)))
      },
      async get(id) {
        return delay(clone(materializeItem(findItem(id))))
      },
      async add(request) {
        currentUser()
        if (!!request.bookId === !!request.source) {
          throw validation({ bookId: ['Informe exatamente um de bookId ou source.'] })
        }
        let record: BookRecord | undefined
        if (request.bookId) {
          record = books.get(request.bookId)
        } else if (request.source) {
          const { provider, externalId } = request.source
          if (provider === 'Local') {
            record = books.get(externalId)
          } else {
            const seed = externals.find(
              (e) => e.provider === provider && e.externalId === externalId,
            )
            record = seed ? importExternal(seed) : findByExternal(provider, externalId)
          }
        }
        if (!record) throw notFound()
        if (inLibrary(record.id)) {
          await delay(null)
          throw new ApiError({
            status: 409,
            title: 'Livro já na biblioteca',
            code: 'library.duplicate',
          })
        }
        // Cenário "perdido": a 1ª tentativa expira sem concluir (nada foi criado).
        if (/perdido/i.test(record.title) && !timedOutOnce.has(record.id)) {
          timedOutOnce.add(record.id)
          await delay(null, 1500)
          throw new ApiError({
            status: 0,
            title: 'Tempo de resposta esgotado',
            code: 'network.timeout',
          })
        }
        const reading = applyReadingRules({
          status: request.status,
          rating: request.rating ?? null,
          review: request.review ?? null,
          startDate: request.startDate ?? null,
          finishedDate: request.finishedDate ?? null,
        })
        const now = new Date().toISOString()
        const item: ItemRecord = {
          id: genId(),
          ...reading,
          createdAt: now,
          updatedAt: now,
          bookId: record.id,
        }
        items.set(item.id, item)
        // Cenário "lento": a ação conclui no servidor, mas a resposta expira no cliente.
        if (/lento/i.test(record.title) && !timedOutOnce.has(record.id)) {
          timedOutOnce.add(record.id)
          await delay(null, 1500)
          throw new ApiError({
            status: 0,
            title: 'Tempo de resposta esgotado',
            code: 'network.timeout',
          })
        }
        return delay(clone(materializeItem(item)), 400)
      },
      async update(id, request) {
        const current = findItem(id)
        const updated: ItemRecord = {
          ...current,
          ...applyReadingRules(request),
          updatedAt: new Date().toISOString(),
        }
        items.set(id, updated)
        return delay(clone(materializeItem(updated)))
      },
      async remove(id) {
        findItem(id)
        items.delete(id)
        await delay(null)
      },
    },

    authors: {
      async get(id) {
        currentUser()
        const author = authors.get(id)
        if (!author) throw notFound()
        return delay(clone(author))
      },
      async getBooks(id) {
        currentUser()
        return delay(
          clone(
            libraryItems()
              .map((i) => i.book)
              .filter((b) => b.authors.some((a) => a.id === id)),
          ),
        )
      },
      async updateCountries(id, request) {
        currentUser()
        const author = authors.get(id)
        if (!author) throw notFound()
        const hasBookInLibrary = [...items.values()].some((i) =>
          books.get(i.bookId)?.authorIds.includes(id),
        )
        assertCanEditGeo(
          author.countries.filter((c) => c.relationship === 'Citizenship'),
          hasBookInLibrary,
          'author.not_in_library',
        )
        author.countries = [
          ...author.countries.filter((c) => c.relationship !== 'Citizenship'),
          ...[...new Set(request.citizenshipIsos)].map((iso): AuthorCountryDto => ({
            ...relation(iso, 'Manual', 1),
            relationship: 'Citizenship',
          })),
        ]
        refreshStatuses()
        return delay(clone(author))
      },
    },

    countries: {
      async list() {
        return delay(clone(MOCK_COUNTRIES), 50)
      },
    },

    maps: {
      async get(mapType) {
        currentUser()
        return delay(computeMapData(libraryItems(), mapType, countries))
      },
      async getCountryDetail(mapType, iso) {
        currentUser()
        if (!countries.has(iso)) throw notFound()
        return delay(computeCountryDetail(libraryItems(), mapType, iso, countries))
      },
      async createShare({ mapType }) {
        const user = currentUser()
        if (user.profile.mapVisibility !== 'LinkOnly') {
          throw new ApiError({
            status: 403,
            title: 'Perfil privado',
            code: 'share.private_profile',
          })
        }
        const token = Math.random().toString(36).slice(2) + Math.random().toString(36).slice(2)
        const share = {
          token,
          mapType,
          url: `${globalThis.location?.origin ?? ''}/share/${token}`,
          createdAt: new Date().toISOString(),
          userId: user.profile.id,
        }
        shares.set(token, share)
        const { userId: _userId, ...dto } = share
        return delay(dto)
      },
      async listShares() {
        const user = currentUser()
        return delay(
          [...shares.values()]
            .filter((s) => s.userId === user.profile.id)
            .map(({ userId: _userId, ...dto }) => dto),
        )
      },
      async revokeShare(token) {
        currentUser()
        shares.delete(token)
        await delay(null)
      },
      async getPublic(token) {
        const share = shares.get(token)
        const owner = share ? users.get(share.userId) : undefined
        if (!share || !owner || owner.profile.mapVisibility !== 'LinkOnly') {
          await delay(null)
          throw notFound()
        }
        const map = computeMapData(libraryItems(), share.mapType, countries)
        return delay({
          displayName: owner.profile.name,
          mapType: map.mapType,
          totalReadBooks: map.totalReadBooks,
          exploredCountries: map.exploredCountries,
          maxBookCount: map.maxBookCount,
          countries: map.countries.map(({ iso, name, continent, bookCount }) => ({
            iso,
            name,
            continent,
            bookCount,
          })),
        })
      },
    },

    statistics: {
      async dashboard() {
        currentUser()
        return delay(computeDashboard(libraryItems(), countries, MOCK_TOTAL_COUNTRIES))
      },
      async passports() {
        currentUser()
        return delay(computePassports(libraryItems(), countries, MOCK_TOTAL_COUNTRIES))
      },
    },
  }
}
