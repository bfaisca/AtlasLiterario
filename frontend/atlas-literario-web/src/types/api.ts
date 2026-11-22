/**
 * Tipos dos contratos HTTP — espelho exato de `docs/api-contracts.md` (v1).
 * Qualquer divergência com o OpenAPI do backend deve ser reportada ao agente principal.
 */

// ---------------------------------------------------------------- 0. Convenções

export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export interface ProblemDetails {
  type?: string
  title: string
  status: number
  detail?: string
  instance?: string
  /** apenas em 400 de validação */
  errors?: Record<string, string[]>
  /** código estável para o frontend, ex.: "auth.invalid_credentials" */
  code?: string
}

// ---------------------------------------------------------------- 1. Enums

export type ReadingStatus = 'WantToRead' | 'Reading' | 'Read' | 'Abandoned'
/** Rota usa minúsculo: /maps/authors, /maps/origins (ver `MapRouteType`). */
export type MapType = 'Authors' | 'Origins'
/** Filtro de visualização do mapa: um status de leitura ou `All` (os quatro). Padrão: `Read`. */
export type MapStatusFilter = ReadingStatus | 'All'
/** GET /library?geoMissing= — livros sem país conhecido (obra e/ou autor). */
export type LibraryGeoMissing = 'any' | 'origin' | 'author' | 'both'
export type Continent =
  'Africa' | 'Asia' | 'Europe' | 'NorthAmerica' | 'SouthAmerica' | 'Oceania' | 'Antarctica'
export type GeoSource = 'Manual' | 'Wikidata' | 'OpenLibrary' | 'GoogleBooks'
export type AuthorCountryRelationship = 'Citizenship' | 'BirthPlace' | 'Residence'
export type ExternalProvider = 'GoogleBooks' | 'OpenLibrary' | 'Wikidata' | 'Local'
export type GeoEnrichmentStatus = 'Pending' | 'Completed' | 'Partial' | 'Failed'
/** v1.12: `Public` = perfil descobrível por apelido e mapas completos visíveis a usuários autenticados. */
export type MapVisibility = 'Private' | 'LinkOnly' | 'Public'
export type SearchType = 'all' | 'title' | 'author' | 'isbn'
export type LibrarySort =
  'title' | 'author' | 'createdAt' | 'finishedDate' | 'rating' | 'publicationYear'

export const READING_STATUSES: readonly ReadingStatus[] = [
  'WantToRead',
  'Reading',
  'Read',
  'Abandoned',
] as const

/** Segmento de rota dos mapas (`/maps/{type}`, `mapType` em `GET /library`). */
export type MapRouteType = 'authors' | 'origins'
export type SortOrder = 'asc' | 'desc'

// ---------------------------------------------------------------- 2. Autenticação

export interface RegisterRequest {
  name: string
  email: string
  /** ≥ 8 caracteres, com letra e número */
  password: string
}
export interface LoginRequest {
  email: string
  password: string
}
export interface RefreshRequest {
  refreshToken: string
}
export interface ForgotPasswordRequest {
  email: string
}
export interface ResetPasswordRequest {
  token: string
  newPassword: string
}
export interface ChangePasswordRequest {
  currentPassword: string
  newPassword: string
}
export interface UpdateProfileRequest {
  name: string
  mapVisibility: MapVisibility
  /** v1.12: omitido = mantém; `null`/"" = remove (só se `mapVisibility` ≠ `Public`). */
  handle?: string | null
}

export interface AuthResponse {
  accessToken: string
  /** ISO UTC */
  accessTokenExpiresAt: string
  refreshToken: string
  user: UserProfileDto
}
/** Papel do usuário (contrato v1.7). Somente `Moderator` altera países já informados. */
export type UserRole = 'User' | 'Moderator'

export interface UserProfileDto {
  id: string
  name: string
  email: string
  mapVisibility: MapVisibility
  role: UserRole
  createdAt: string
  /** v1.12: apelido único (`^[a-z0-9_]{3,30}$`); `null` em contas sem apelido. */
  handle: string | null
  followersCount: number
  followingCount: number
}

// ---------------------------------------------------------------- 3. Livros (catálogo)

export interface ExternalSourceRef {
  provider: ExternalProvider
  externalId: string
}

export interface BookSearchParams {
  q: string
  type?: SearchType
  limit?: number
}

export interface BookSearchResultDto {
  /** "local:{bookId}" | "GoogleBooks:{volumeId}" | "OpenLibrary:{workId}" */
  resultKey: string
  /** preenchido se já existe no catálogo */
  bookId: string | null
  /** já está na biblioteca do usuário */
  inLibrary: boolean
  /** UserBook.Id quando inLibrary = true (abre o item direto) */
  libraryItemId: string | null
  title: string
  subtitle: string | null
  authors: string[]
  publisher: string | null
  publicationYear: number | null
  isbn13: string | null
  isbn10: string | null
  coverUrl: string | null
  /** truncada em 500 caracteres */
  description: string | null
  source: ExternalSourceRef
}

export interface GeoRelationDto {
  /** ISO 3166-1 alpha-3, ex.: "COL" */
  countryIso: string
  /** nome em português */
  countryName: string
  source: GeoSource
  /** 0–1 */
  confidence: number
  isVerified: boolean
}

export interface AuthorSummaryDto {
  id: string
  name: string
  /** somente Citizenship; vazio = desconhecido */
  nationalities: GeoRelationDto[]
}

export interface BookDetailsDto {
  id: string
  title: string
  subtitle: string | null
  isbn13: string | null
  isbn10: string | null
  publisher: string | null
  publicationYear: number | null
  description: string | null
  coverUrl: string | null
  originalLanguage: string | null
  genres: string[]
  authors: AuthorSummaryDto[]
  /** vazio = desconhecido */
  originCountries: GeoRelationDto[]
  geoEnrichmentStatus: GeoEnrichmentStatus
  externalIds: ExternalSourceRef[]
}

/** Também usado em PUT /books/{id} */
export interface CreateBookRequest {
  title: string
  subtitle?: string | null
  isbn13?: string | null
  isbn10?: string | null
  publisher?: string | null
  publicationYear?: number | null
  description?: string | null
  coverUrl?: string | null
  originalLanguage?: string | null
  genres?: string[]
  /** ≥ 1 */
  authorNames: string[]
  /**
   * v1.10: opcional; MESMO tamanho e ordem de `authorNames`. Id não nulo = usar este autor já
   * cadastrado (escolhido no autocomplete); null = resolver/criar por nome.
   */
  authorIds?: (string | null)[] | null
  /** gravados como Manual/verificados */
  /** Em PUT: omitido/null = não alterar as origens; array (inclusive vazio) = substituir. */
  originCountryIsos?: string[] | null
}
export interface UpdateOriginCountriesRequest {
  countryIsos: string[]
}

// ---------------------------------------------------------------- 4. Biblioteca

export interface LibraryQueryParams {
  q?: string
  /** repetível: status=Read&status=Reading */
  status?: ReadingStatus[]
  countryIso?: string
  /** usado com countryIso */
  mapType?: MapRouteType
  genre?: string
  minRating?: number
  /** omitido = sem filtro; combina com os demais */
  geoMissing?: LibraryGeoMissing
  /** padrão createdAt */
  sort?: LibrarySort
  /** padrão desc */
  order?: SortOrder
  page?: number
  pageSize?: number
}

export interface LibraryItemDto {
  /** UserBook.Id */
  id: string
  status: ReadingStatus
  /** 1–5 */
  rating: number | null
  review: string | null
  /** "YYYY-MM-DD" */
  startDate: string | null
  finishedDate: string | null
  createdAt: string
  updatedAt: string
  book: BookDetailsDto
}

export interface AddToLibraryRequest {
  /** exatamente um de bookId ou source */
  bookId?: string | null
  source?: ExternalSourceRef | null
  status: ReadingStatus
  rating?: number | null
  review?: string | null
  startDate?: string | null
  finishedDate?: string | null
}

export interface UpdateLibraryItemRequest {
  status: ReadingStatus
  rating: number | null
  review: string | null
  startDate: string | null
  finishedDate: string | null
}

// ---------------------------------------------------------------- 5. Autores

export interface AuthorCountryDto extends GeoRelationDto {
  relationship: AuthorCountryRelationship
}
export interface AuthorDetailsDto {
  id: string
  name: string
  birthDate: string | null
  /** todas as relações (Citizenship, BirthPlace, Residence) */
  countries: AuthorCountryDto[]
  externalIds: ExternalSourceRef[]
}
/** v1.10 — sugestão do autocomplete (`GET /authors?q=&limit=`). */
export interface AuthorSuggestionDto {
  id: string
  name: string
  /** somente Citizenship; vazio = desconhecido */
  nationalityIsos: string[]
  /** nomes em português, mesma ordem de `nationalityIsos` */
  nationalityNames: string[]
}

export interface UpdateAuthorCountriesRequest {
  citizenshipIsos: string[]
}

// ---------------------------------------------------------------- 6. Países

export interface CountryDto {
  iso: string
  iso2: string
  name: string
  namePt: string
  continent: Continent
  geoJsonId: string
}

// ---------------------------------------------------------------- 7. Mapas

/** Formato consumido pela camada de mapa (independente da biblioteca visual) */
export interface CountryMapDatum {
  /** = geoJsonId (ISO_A3) */
  iso: string
  /** português */
  name: string
  continent: Continent
  bookCount: number
  authorCount: number
}

export interface MapDataDto {
  mapType: MapType
  /** filtro de status aplicado (eco; `Read` quando omitido na requisição) */
  status: MapStatusFilter
  /** |R| (total de livros do conjunto do status escolhido; nome mantido por compatibilidade) */
  totalReadBooks: number
  /** países com bookCount ≥ 1 */
  exploredCountries: number
  /** para a escala de cores */
  maxBookCount: number
  /** livros lidos sem país conhecido neste mapa */
  unknownBookCount: number
  /** apenas países com bookCount ≥ 1 */
  countries: CountryMapDatum[]
}

export interface CountryBookDto {
  libraryItemId: string
  bookId: string
  title: string
  coverUrl: string | null
  authors: string[]
  finishedDate: string | null
  rating: number | null
}

/** v1.10/v1.11 — card do autor no painel do mapa de autores (clicável: expande `books`). */
export interface CountryAuthorDto {
  authorId: string
  name: string
  /** = iso do painel */
  countryIso: string
  /** português */
  countryName: string
  /** v1.11: livros DESTE autor no filtro de status (finishedDate desc), só do usuário; nunca vazio */
  books: CountryBookDto[]
}

export interface CountryDetailDto {
  mapType: MapType
  /** filtro de status aplicado (eco) */
  status: MapStatusFilter
  iso: string
  name: string
  continent: Continent
  bookCount: number
  authorCount: number
  /** ordenado por finishedDate desc (devolvido nos dois mapas; a UI só o exibe em Origins) */
  books: CountryBookDto[]
  /** v1.10: mapa Authors = autores com Citizenship no país (ordem alfabética sem acento); Origins = [] */
  authors: CountryAuthorDto[]
}

export interface CreateMapShareRequest {
  mapType: MapType
}
export interface MapShareDto {
  token: string
  mapType: MapType
  url: string
  createdAt: string
}

export type PublicCountryMapDatum = Pick<
  CountryMapDatum,
  'iso' | 'name' | 'continent' | 'bookCount'
>

export interface PublicMapDto {
  displayName: string
  mapType: MapType
  totalReadBooks: number
  exploredCountries: number
  maxBookCount: number
  countries: PublicCountryMapDatum[]
}

// ---------------------------------------------------------------- 8. Estatísticas

export interface TopCountryDto {
  iso: string
  name: string
  bookCount: number
  mapType: MapType
}

export interface PassportProgressDto {
  /** distinctCountries */
  exploredCountries: number
  /** países do seed (≈ 250) */
  totalCountries: number
  /** 0–100, 1 casa decimal */
  percentage: number
}

export interface DashboardDto {
  totalReadBooks: number
  /** autores distintos dos livros lidos */
  totalAuthors: number
  countriesByAuthors: number
  countriesByOrigins: number
  /** união dos dois mapas */
  distinctCountries: number
  /** sobre a união */
  continentsExplored: number
  /** status Reading */
  readingNow: number
  wantToRead: number
  /** até 6 */
  recentlyFinished: CountryBookDto[]
  /** até 5, por mapa */
  topCountries: TopCountryDto[]
  passportProgress: PassportProgressDto
}

export interface PassportMapStatsDto {
  exploredCountries: number
  topCountry: TopCountryDto | null
  /** ordenado por bookCount desc */
  booksByCountry: CountryMapDatum[]
  unknownBookCount: number
}

export interface ContinentProgressDto {
  continent: Continent
  /** união dos dois mapas */
  exploredCountries: number
  totalCountries: number
}

export interface TimelinePointDto {
  /** "2025-03" */
  month: string
  /** no mês */
  booksFinished: number
  cumulativeBooks: number
  cumulativeCountriesAuthors: number
  cumulativeCountriesOrigins: number
  cumulativeDistinctCountries: number
}

export interface PassportsDto {
  authors: PassportMapStatsDto
  origins: PassportMapStatsDto
  distinctCountries: number
  /** todos os continentes (exceto Antarctica), inclusive com 0 */
  continents: ContinentProgressDto[]
  /** mensal, desde o primeiro livro lido */
  timeline: TimelinePointDto[]
  progress: PassportProgressDto
}

// ---------------------------------------------------------------- 8.1 Perfis públicos e seguidores (v1.12)

export interface ProfileSummaryDto {
  handle: string
  name: string
  followersCount: number
  /** livros `Read` */
  readBooksCount: number
  /** o usuário logado segue este perfil */
  isFollowing: boolean
  /** este perfil segue o usuário logado */
  followsYou: boolean
}

export interface ProfileDto extends ProfileSummaryDto {
  followingCount: number
  exploredCountriesAuthors: number
  exploredCountriesOrigins: number
  /** ISO */
  memberSince: string
  /** quando `true`, `isFollowing`/`followsYou` = `false` */
  isSelf: boolean
}

/** Sem `libraryItemId`: não expõe ids da biblioteca de terceiros. */
export interface ProfileBookDto {
  bookId: string
  title: string
  coverUrl: string | null
  authors: string[]
  finishedDate: string | null
  rating: number | null
  /** texto da resenha (público neste perfil) */
  review: string | null
}

export interface ProfileCountryAuthorDto {
  authorId: string
  name: string
  countryIso: string
  countryName: string
  books: ProfileBookDto[]
}

export interface ProfileCountryDetailDto {
  mapType: MapType
  iso: string
  name: string
  continent: Continent
  bookCount: number
  authorCount: number
  /** finishedDate desc */
  books: ProfileBookDto[]
  /** mapa `Authors`; `[]` em `Origins` */
  authors: ProfileCountryAuthorDto[]
  status: 'Read'
}
