# Contratos da API — Atlas Literário (v1)

> Especificação acordada **antes** da implementação. O OpenAPI do backend (`/openapi/v1.json`, UI em `/swagger`) será a referência oficial; ele deve refletir exatamente este documento. Mudanças incompatíveis precisam ser registradas aqui (seção "Changelog") e comunicadas ao agente principal.

## 0. Convenções

- Base: `/api`. JSON em **camelCase**. Enums serializados como **string** (`"Read"`, `"Citizenship"`).
- Datas: `DateOnly` → `"2025-03-14"`; instantes → ISO-8601 UTC `"2025-03-14T18:20:00Z"`.
- IDs: `uuid` como string; `countryIso` = ISO 3166-1 alpha-3 (`"BRA"`).
- Autenticação: `Authorization: Bearer <accessToken>`. Todas as rotas exigem autenticação, **exceto** as marcadas 🌐.
- Paginação (listas): query `page` (1-based, padrão 1), `pageSize` (padrão 20, máx. 100). Resposta `PagedResult<T>`.
- Erros: RFC 7807 (`application/problem+json`).

```ts
interface PagedResult<T> { items: T[]; page: number; pageSize: number; totalCount: number; totalPages: number }

interface ProblemDetails {
  type?: string; title: string; status: number; detail?: string; instance?: string;
  errors?: Record<string, string[]>;   // apenas em 400 de validação
  code?: string;                        // código estável para o frontend, ex.: "auth.invalid_credentials"
}
```

Códigos usados: `400` validação · `401` não autenticado/token inválido · `403` · `404` · `409` conflito (`library.duplicate`, `book.duplicate_isbn`, `auth.email_taken`) · `501` endpoint ainda não implementado (`feature.not_implemented`) · `502` falha em fonte externa (`external.unavailable`).

Catálogo de `code` (estável; o frontend pode traduzir por código):

| Área | Códigos |
|---|---|
| Geral | `validation.failed`, `feature.not_implemented`, `external.unavailable`, `rate_limit.exceeded` (429, com `Retry-After`; busca de perfis: 30 req/min por usuário, configurável) |
| Auth | `auth.unauthorized`, `auth.invalid_credentials`, `auth.email_taken`, `auth.invalid_refresh_token`, `auth.invalid_reset_token`, `auth.invalid_password`, `auth.user_not_found` |
| Usuário | `user.invalid_name`, `user.invalid_email`, `user.invalid_map_visibility`, `profile.handle_invalid` (400), `profile.handle_required` (400), `profile.handle_taken` (409) |
| Perfis/seguidores | `profile.not_found` (404; também quando o perfil não é `Public`), `follow.self` (400) |
| Biblioteca | `library.not_found`, `library.invalid_book_reference`, `library.duplicate`, `library.invalid_rating`, `library.invalid_dates`, `library.invalid_status`, `library.review_too_long` |
| Moderação | `geo.moderator_required` (403) |
| Capa | `cover.invalid_type` (400), `cover.too_large` (413), `cover.not_found` (404) |
| Livros/autores | `book.invalid_isbn13`, `book.invalid_isbn10`, `book.duplicate_isbn`, `book.not_found`, `book.not_in_library`, `book.source_not_found`, `book.invalid_source`, `book.import_conflict`, `author.invalid_name`, `author.not_found`, `author.not_in_library`, `country.unknown_iso` |
| Compartilhamento | `share.private_profile` |

## 1. Enums

```ts
type ReadingStatus = "WantToRead" | "Reading" | "Read" | "Abandoned";
type MapType = "Authors" | "Origins";                       // rota usa minúsculo: /maps/authors, /maps/origins
type Continent = "Africa" | "Asia" | "Europe" | "NorthAmerica" | "SouthAmerica" | "Oceania" | "Antarctica";
type GeoSource = "Manual" | "Wikidata" | "OpenLibrary" | "GoogleBooks";
type AuthorCountryRelationship = "Citizenship" | "BirthPlace" | "Residence";
type ExternalProvider = "GoogleBooks" | "OpenLibrary" | "Wikidata" | "Local";
type GeoEnrichmentStatus = "Pending" | "Completed" | "Partial" | "Failed";
type MapVisibility = "Private" | "LinkOnly" | "Public";   // v1.12: Public = perfil descobrível e mapas completos visíveis a usuários autenticados
type UserRole = "User" | "Moderator";
type SearchType = "all" | "title" | "author" | "isbn";
type LibrarySort = "title" | "author" | "createdAt" | "finishedDate" | "rating" | "publicationYear";
```

## 2. Autenticação — `/api/auth`

| Método | Rota | Body | Resposta |
|---|---|---|---|
| POST 🌐 | `/auth/register` | `RegisterRequest` | `201 AuthResponse` · `409 auth.email_taken` |
| POST 🌐 | `/auth/login` | `LoginRequest` | `200 AuthResponse` · `401 auth.invalid_credentials` |
| POST 🌐 | `/auth/refresh` | `RefreshRequest` | `200 AuthResponse` · `401` |
| POST | `/auth/logout` | `RefreshRequest` | `204` (revoga o refresh token) |
| POST 🌐 | `/auth/forgot-password` | `ForgotPasswordRequest` | `202` (sempre, não revela se o e-mail existe) |
| POST 🌐 | `/auth/reset-password` | `ResetPasswordRequest` | `204` · `400 auth.invalid_reset_token` |
| GET | `/auth/me` | — | `200 UserProfileDto` |
| PUT | `/auth/me` | `UpdateProfileRequest` | `200 UserProfileDto` |
| PUT | `/auth/me/password` | `ChangePasswordRequest` | `204` · `400 auth.invalid_password` |

```ts
interface RegisterRequest { name: string; email: string; password: string }   // senha ≥ 8, com letra e número
interface LoginRequest { email: string; password: string }
interface RefreshRequest { refreshToken: string }
interface ForgotPasswordRequest { email: string }
interface ResetPasswordRequest { token: string; newPassword: string }
interface ChangePasswordRequest { currentPassword: string; newPassword: string }
interface UpdateProfileRequest { name: string; mapVisibility: MapVisibility; handle?: string | null }   // v1.12: handle omitido = mantém; null/"" = remove (só se mapVisibility != Public)

interface AuthResponse {
  accessToken: string;
  accessTokenExpiresAt: string;   // ISO UTC
  refreshToken: string;
  user: UserProfileDto;
}
interface UserProfileDto { id: string; name: string; email: string; role: UserRole; mapVisibility: MapVisibility; createdAt: string; handle: string | null; followersCount: number; followingCount: number }   // v1.12: handle/contagens
```

> Recuperação de senha no MVP: o token é enviado por `IEmailSender`; em desenvolvimento a implementação apenas registra o link no log (`{FrontendBaseUrl}/reset-password?token=...`).

## 3. Livros (catálogo) — `/api/books`

| Método | Rota | Descrição | Resposta |
|---|---|---|---|
| GET | `/books/search?q=&type=all\|title\|author\|isbn&limit=20` | Pesquisa consolidada (catálogo local + Google Books + Open Library). Não persiste. | `200 BookSearchResultDto[]` |
| GET | `/books/{id}` | Detalhes do livro do catálogo | `200 BookDetailsDto` |
| POST | `/books` | Cadastro manual (sem fonte externa) | `201 BookDetailsDto` |
| PUT | `/books/{id}` | Correção dos dados bibliográficos | `200 BookDetailsDto` |
| PUT | `/books/{id}/origin-countries` | Define as origens. **Permissão:** usuário comum só pode quando as origens atuais estão vazias (primeira informação) e o livro está na sua biblioteca; alterar/remover origens existentes exige `Moderator` (`403 geo.moderator_required`). Moderador edita qualquer livro. | `200 BookDetailsDto` |
| POST | `/books/{id}/cover` | Envia a capa (`multipart/form-data`, campo `file`). JPEG, PNG ou WebP, até 2 MB, validado pelos bytes iniciais (nunca pelo nome/Content-Type; SVG não é aceito). Exige o livro na biblioteca do usuário (`403 book.not_in_library`). Substitui a capa anterior. | `200 BookDetailsDto` (com `coverUrl` interno) · `400 cover.invalid_type` · `413 cover.too_large` |
| DELETE | `/books/{id}/cover` | Remove a capa enviada (`coverUrl` volta a `null`) | `200 BookDetailsDto` |
| GET 🌐 | `/books/{id}/cover` | Bytes da imagem (para `<img>`, que não envia o token). `Content-Type` da imagem validada, `X-Content-Type-Options: nosniff`, `ETag` e `Cache-Control: public, max-age=31536000, immutable` (a URL muda com `?v=`). | `200` imagem · `404` |
| POST | `/books/{id}/enrich` | Reprocessa autores/geografia nas fontes externas | `200 BookDetailsDto` |

```ts
interface BookSearchResultDto {
  /** chave estável p/ o frontend: "local:{bookId}" | "GoogleBooks:{volumeId}" | "OpenLibrary:{workId}" */
  resultKey: string;
  bookId: string | null;            // preenchido se já existe no catálogo
  inLibrary: boolean;               // já está na biblioteca do usuário
  libraryItemId: string | null;     // UserBook.Id quando inLibrary = true (permite abrir o item direto)
  title: string;
  subtitle: string | null;
  authors: string[];
  publisher: string | null;
  publicationYear: number | null;
  isbn13: string | null;
  isbn10: string | null;
  coverUrl: string | null;
  description: string | null;      // truncada em 500 caracteres
  source: ExternalSourceRef;
}
interface ExternalSourceRef { provider: ExternalProvider; externalId: string }
// Open Library: o próprio id distingue obra/edição/autor pelo sufixo (OL45883W = obra, OL7353617M = edição, OL23919A = autor).
// VIAF é armazenado internamente, mas não é exposto no MVP.

// coverUrl: URL externa (https://...) OU, quando a capa foi enviada, o caminho interno `/api/books/{id}/cover?v={hash}`
// (relativo; funciona atrás do nginx e do proxy do Vite). Em PUT /books/{id}, enviar de volta o coverUrl interno
// existente o mantém; enviar uma URL externa substitui (e apaga) a capa enviada; null/vazio remove a capa.
interface BookDetailsDto {
  id: string;
  title: string;
  subtitle: string | null;
  isbn13: string | null;
  isbn10: string | null;
  publisher: string | null;
  publicationYear: number | null;
  description: string | null;
  coverUrl: string | null;
  originalLanguage: string | null;
  genres: string[];
  authors: AuthorSummaryDto[];
  originCountries: GeoRelationDto[];          // vazio = desconhecido
  geoEnrichmentStatus: GeoEnrichmentStatus;
  externalIds: ExternalSourceRef[];
}

interface AuthorSummaryDto {
  id: string;
  name: string;
  nationalities: GeoRelationDto[];            // somente Citizenship; vazio = desconhecido
}

interface GeoRelationDto {
  countryIso: string;     // "COL"
  countryName: string;    // nome em português
  source: GeoSource;
  confidence: number;     // 0–1
  isVerified: boolean;
}

// Em PUT /books/{id}: `originCountryIsos` omitido ou null = NÃO alterar as origens (têm endpoint próprio);
// array (inclusive vazio) = substituir. Em POST /books com ISBN-13 já existente → 409 `book.duplicate_isbn`.
interface CreateBookRequest {                 // também usado em PUT /books/{id}
  title: string;
  subtitle?: string | null;
  isbn13?: string | null;
  isbn10?: string | null;
  publisher?: string | null;
  publicationYear?: number | null;
  description?: string | null;
  coverUrl?: string | null;
  originalLanguage?: string | null;
  genres?: string[];
  authorNames: string[];                      // ≥ 1; autores existentes são reutilizados por id externo/nome
  authorIds?: (string | null)[] | null;       // v1.10: opcional, MESMO tamanho e ordem de authorNames; id não nulo = usar este autor já cadastrado (escolhido no autocomplete) em vez de criar/resolver por nome
  originCountryIsos?: string[] | null;        // gravados como Manual/verificados
}
interface UpdateOriginCountriesRequest { countryIsos: string[] }   // substitui as origens (Source=Manual)
```

## 4. Biblioteca — `/api/library`

| Método | Rota | Descrição | Resposta |
|---|---|---|---|
| GET | `/library` | Lista paginada/filtrável | `200 PagedResult<LibraryItemDto>` |
| GET | `/library/{id}` | Item da biblioteca (id = `UserBook.Id`) | `200 LibraryItemDto` |
| POST | `/library` | Adiciona livro (importando da fonte externa se necessário). Responde `201` mesmo com enriquecimento geográfico incompleto (`geoEnrichmentStatus` Pending/Partial/Failed; reprocessar com `POST /books/{id}/enrich`). Orçamento de importação+enriquecimento: 12 s (`ExternalSources:EnrichmentBudgetSeconds`), pior caso ≈ 13 s; o cliente aceita até 45 s. O item é gravado antes do enriquecimento e não se perde se o cliente abortar. Re-adicionar por `bookId` um livro não Completed tenta enriquecer de novo. | `201 LibraryItemDto` · `409 library.duplicate` (ProblemDetails com extensão `libraryItemId`: uuid do item já existente) |
| PUT | `/library/{id}` | Atualiza status/avaliação/resenha/datas | `200 LibraryItemDto` |
| DELETE | `/library/{id}` | Remove da biblioteca | `204` |

Query de `GET /library` (`countryIso` sem `mapType` casa com qualquer um dos dois mapas): `q` (título/autor), `status` (repetível: `status=Read&status=Reading`), `countryIso`, `mapType` (`authors|origins`, usado com `countryIso`), `genre`, `minRating`, `sort` (`LibrarySort`, padrão `createdAt`), `order` (`asc|desc`, padrão `desc`), `page`, `pageSize`, `geoMissing` (v1.9).

`geoMissing` (opcional, sem diferenciar maiúsculas): `any` (sem origem **ou** sem nacionalidade de autor), `origin` (obra sem nenhum país de origem), `author` (nenhum autor do livro tem relação `Citizenship`; livro sem autores cadastrados conta como sem país do autor), `both` (sem origem **e** sem nacionalidade de autor). "Sem país" = **sem relação gravada** (dado desconhecido, não ausência afirmada), exatamente o que o filtro expõe para o usuário completar. Combina com os demais filtros (E) e é traduzido para SQL. Valor inválido → `400 validation.failed` com erro em `errors.geoMissing`.

```ts
interface LibraryItemDto {
  id: string;                 // UserBook.Id
  status: ReadingStatus;
  rating: number | null;      // 1–5
  review: string | null;
  startDate: string | null;   // "YYYY-MM-DD"
  finishedDate: string | null;
  createdAt: string;
  updatedAt: string;
  book: BookDetailsDto;
}

interface AddToLibraryRequest {
  /** exatamente um de bookId ou source */
  bookId?: string | null;
  source?: ExternalSourceRef | null;
  status: ReadingStatus;
  rating?: number | null;
  review?: string | null;
  startDate?: string | null;
  finishedDate?: string | null;
}

interface UpdateLibraryItemRequest {
  status: ReadingStatus;
  rating: number | null;
  review: string | null;
  startDate: string | null;
  finishedDate: string | null;
}
```

## 5. Autores — `/api/authors`

| Método | Rota | Resposta |
|---|---|---|
| GET | `/authors?q=&limit=` | **v1.10** — autocomplete do cadastro de livro. `q` obrigatório (≥ 2 caracteres após trim; comparação sem acento/caixa sobre `normalizedName`, prefixo do nome ou de qualquer palavra), `limit` 1–20 (padrão 10; listagem curta sem paginação por ser sugestão). Ordenado por relevância (prefixo do nome primeiro) e depois por nome. `q` curto/ausente → `400 validation.failed` (`errors.q`); `limit` fora da faixa → `400` (`errors.limit`). `200 AuthorSuggestionDto[]` |
| GET | `/authors/{id}` | `200 AuthorDetailsDto` |
| GET | `/authors/{id}/books` | `200 BookDetailsDto[]` (somente os livros deste autor que estão na biblioteca do usuário) |
| GET | `/authors/duplicates` | **`Moderator`** (`403 author.moderator_required`). Grupos de autores cujo `normalizedName` é igual (≥ 2 registros). → `200 AuthorDuplicateGroupDto[]` ordenado por nome. |
| POST | `/authors/merge` | **`Moderator`**. body `MergeAuthorsRequest` → `200 AuthorMergeResultDto`. Une autores duplicados em um só **sem perder vínculos** (ver regras abaixo). `400 validation.failed` (`errors.sourceIds` vazio/repetido/contém `targetId`), `404 author.not_found`, `403 author.moderator_required`. |
| PUT | `/authors/{id}/countries` | body `UpdateAuthorCountriesRequest` → `200 AuthorDetailsDto`. **Permissão:** igual a `/books/{id}/origin-countries` — usuário comum só preenche cidadanias quando o autor não tem nenhuma (e tem um livro dele na biblioteca); alterar/remover exige `Moderator` (`403 geo.moderator_required`). |

```ts
interface AuthorDetailsDto {
  id: string;
  name: string;
  birthDate: string | null;
  countries: AuthorCountryDto[];    // todas as relações (Citizenship, BirthPlace, Residence)
  externalIds: ExternalSourceRef[];
}
interface AuthorCountryDto extends GeoRelationDto { relationship: AuthorCountryRelationship }
interface UpdateAuthorCountriesRequest { citizenshipIsos: string[] }   // substitui Citizenship (Source=Manual)

// v1.10 — sugestão para o autocomplete (somente o necessário; sem datas nem ids externos)
interface AuthorSuggestionDto {
  id: string;
  name: string;
  nationalityIsos: string[];      // somente Citizenship; vazio = desconhecido
  nationalityNames: string[];     // nomes em português, mesma ordem de nationalityIsos
}
```

## 6. Países — `/api/countries`

| Método | Rota | Resposta |
|---|---|---|
| GET 🌐 | `/countries` | `200 CountryDto[]` (lista completa, para seletores e passaportes; cacheável) |

```ts
interface CountryDto { iso: string; iso2: string; name: string; namePt: string; continent: Continent; geoJsonId: string }
```

## 7. Mapas — `/api/maps`

| Método | Rota | Resposta |
|---|---|---|
| GET | `/maps/authors?status=` | `200 MapDataDto` · `400 validation.failed` (`errors.status`) |
| GET | `/maps/origins?status=` | `200 MapDataDto` · `400 validation.failed` (`errors.status`) |
| GET | `/maps/{type}/countries/{iso}?status=` (`type` = `authors\|origins`) | `200 CountryDetailDto` (painel lateral ao clicar). País válido sem livros → `200` com `bookCount: 0` e `books: []`; ISO desconhecido ou malformado → `400 country.unknown_iso`; `status` inválido → `400 validation.failed`; `type` inválido → `404`. O ISO é aceito em qualquer caixa; `books` ordenado por `finishedDate` desc (nulos por último, desempate por título). |
| POST | `/maps/share` | body `CreateMapShareRequest` → `201 MapShareDto` · `403 share.private_profile` |
| GET | `/maps/share` | `200 MapShareDto[]` (links ativos do usuário) |
| DELETE | `/maps/share/{token}` | `204` |
| GET 🌐 | `/public/maps/{token}` | `200 PublicMapDto` · `404` |

**Query `status` (v1.9, opcional, sem diferenciar maiúsculas)** nos três endpoints acima: `Read` (padrão quando omitido — comportamento idêntico ao anterior), `WantToRead`, `Reading`, `Abandoned` ou `All` (os quatro). É um filtro de **visualização**: as regras de contagem (livro uma vez por país, autor com 2 nacionalidades conta nos dois, `unknownBookCount`, `exploredCountries`, `maxBookCount`) valem sobre o conjunto de livros do status escolhido; `totalReadBooks` passa a ser o total desse conjunto (nome mantido por compatibilidade) e `CountryDetailDto.books` lista os livros do mesmo conjunto. Valor inválido (inclusive número ou lista separada por vírgula) → `400 validation.failed` com `errors.status`. **Não se aplica** a `GET /statistics/*`, a `POST/GET /maps/share` nem a `GET /public/maps/{token}` (todos continuam só `Read`; `GET /statistics/*` implementado na Etapa 5A; compartilhamento e mapas públicos ainda `501`) e não altera nenhuma contagem de Passaporte.

```ts
interface MapDataDto {
  mapType: MapType;
  totalReadBooks: number;          // |R|
  exploredCountries: number;       // países com bookCount ≥ 1
  maxBookCount: number;            // para a escala de cores
  unknownBookCount: number;        // livros lidos sem país conhecido neste mapa
  countries: CountryMapDatum[];    // apenas países com bookCount ≥ 1
  status: MapStatusFilter;         // v1.9: filtro aplicado (eco); sempre presente
}

type MapStatusFilter = "Read" | "WantToRead" | "Reading" | "Abandoned" | "All";

/** Formato consumido pela camada de mapa (independente da biblioteca visual) */
// authorCount: no mapa de autores, autores com cidadania no país; no de origens, todos os autores dos livros daquele país.
interface CountryMapDatum {
  iso: string;            // = geoJsonId (ISO_A3)
  name: string;           // português
  continent: Continent;
  bookCount: number;
  authorCount: number;
}

interface CountryDetailDto {
  mapType: MapType;
  iso: string;
  name: string;
  continent: Continent;
  bookCount: number;
  authorCount: number;
  books: CountryBookDto[];    // ordenado por finishedDate desc
  status: MapStatusFilter;    // v1.9: filtro aplicado (eco); sempre presente
  authors: CountryAuthorDto[]; // v1.10: mapa `authors` = autores com Citizenship neste país (de livros do status filtrado), ordem alfabética sem acento; mapa `origins` = []
}
// v1.10 — card do autor no painel do mapa de autores (só nome e país); v1.11: clicável, expande os livros do autor
interface CountryAuthorDto {
  authorId: string;
  name: string;
  countryIso: string;         // = iso do painel
  countryName: string;        // nome em português
  books: CountryBookDto[];    // v1.11: livros DESTE autor no conjunto filtrado por status (o mesmo de `books` do painel), finishedDate desc; só os do usuário logado; nunca vazio
}
// v1.11 — deduplicação de autores (moderador)
interface AuthorDuplicateDto {
  id: string;
  name: string;
  bookCount: number;          // livros do catálogo vinculados (todos os usuários)
  nationalityIsos: string[];  // só Citizenship
  createdAt: string;
}
interface AuthorDuplicateGroupDto { normalizedName: string; authors: AuthorDuplicateDto[]; }  // authors: mais livros primeiro, depois mais antigo
interface MergeAuthorsRequest { targetId: string; sourceIds: string[]; }   // 1–50 ids distintos, ≠ targetId
interface AuthorMergeResultDto {
  targetId: string;
  mergedAuthors: number;      // registros removidos
  booksRelinked: number;      // vínculos movidos para o alvo
  booksAlreadyLinked: number; // livros que o alvo já tinha (vínculo duplicado descartado)
  countriesAdded: number;     // relações de país trazidas para o alvo
}
interface CountryBookDto {
  libraryItemId: string;
  bookId: string;
  title: string;
  coverUrl: string | null;
  authors: string[];
  finishedDate: string | null;
  rating: number | null;
}

interface CreateMapShareRequest { mapType: MapType }
interface MapShareDto { token: string; mapType: MapType; url: string; createdAt: string }

interface PublicMapDto {
  displayName: string;
  mapType: MapType;
  totalReadBooks: number;
  exploredCountries: number;
  maxBookCount: number;
  countries: Pick<CountryMapDatum, "iso" | "name" | "continent" | "bookCount">[];
}
```

## 8. Estatísticas — `/api/statistics`

| Método | Rota | Resposta |
|---|---|---|
| GET | `/statistics/dashboard` | `200 DashboardDto` |
| GET | `/statistics/passports` | `200 PassportsDto` |

```ts
interface DashboardDto {
  totalReadBooks: number;
  totalAuthors: number;                 // autores distintos dos livros lidos
  countriesByAuthors: number;
  countriesByOrigins: number;
  distinctCountries: number;            // união dos dois mapas
  continentsExplored: number;           // sobre a união
  readingNow: number;                   // status Reading
  wantToRead: number;
  recentlyFinished: CountryBookDto[];   // até 6
  topCountries: TopCountryDto[];        // até 5, por mapa
  passportProgress: PassportProgressDto;
}
interface TopCountryDto { iso: string; name: string; bookCount: number; mapType: MapType }

interface PassportProgressDto {
  exploredCountries: number;   // distinctCountries
  totalCountries: number;      // países do seed (≈ 250) — frontend pode exibir também "de 195 Estados soberanos" se desejar
  percentage: number;          // 0–100, 1 casa decimal
}

interface PassportsDto {
  authors: PassportMapStatsDto;
  origins: PassportMapStatsDto;
  distinctCountries: number;
  continents: ContinentProgressDto[];       // todos os continentes (exceto Antarctica), inclusive com 0
  timeline: TimelinePointDto[];             // mensal, desde o primeiro livro lido
  progress: PassportProgressDto;
}
interface PassportMapStatsDto {
  exploredCountries: number;
  topCountry: TopCountryDto | null;
  booksByCountry: CountryMapDatum[];        // ordenado por bookCount desc
  unknownBookCount: number;
}
interface ContinentProgressDto {
  continent: Continent;
  exploredCountries: number;                // união dos dois mapas
  totalCountries: number;
}
interface TimelinePointDto {
  month: string;                            // "2025-03"
  booksFinished: number;                    // no mês
  cumulativeBooks: number;
  cumulativeCountriesAuthors: number;
  cumulativeCountriesOrigins: number;
  cumulativeDistinctCountries: number;
}
```

"Países ainda não explorados" é derivado no frontend: `GET /countries` menos os países presentes nos mapas.

## 8.1 Perfis públicos e seguidores — `/api/profiles` (v1.12)

Decisões de produto: relação **assimétrica** (seguir / deixar de seguir; "seguir de volta" = seguir quem já te segue; **sem amizade**, sem aceite). Perfis são encontrados por **apelido (`handle`) único**. Um perfil `Public` pode ser visto por **qualquer usuário autenticado** (não exige seguir) com o **mapa completo**: países, autores, títulos e resenhas dos livros `Read`. Perfil que não é `Public` responde `404 profile.not_found` a terceiros (não revela existência). Todos os endpoints exigem autenticação; o próprio usuário sempre vê o próprio perfil. Padrão de todas as contas: `Private` (nunca migrar contas para `Public`).

**Apelido:** `^[a-z0-9_]{3,30}$` após `Trim` + minúsculas (a entrada é normalizada em minúsculas); único sem diferenciar maiúsculas (`409 profile.handle_taken`); formato inválido `400 profile.handle_invalid` (também `errors.handle`); `mapVisibility = Public` exige apelido (`400 profile.handle_required`). Nomes reservados (`admin`, `atlas`, `api`, `me`, `profiles`, `support`, `moderator`) → `profile.handle_invalid`. Alterar o apelido invalida o anterior. `LinkOnly`/`Private` não exigem apelido. Criar link de compartilhamento exige `mapVisibility != Private` (`403 share.private_profile`).

| Método | Rota | Descrição | Resposta |
|---|---|---|---|
| GET | `/profiles?q=&limit=` | Busca perfis `Public` por prefixo do apelido ou de qualquer palavra do nome (sem acento/caixa). `q` ≥ 2 caracteres, `limit` 1–20 (padrão 10). Exclui o próprio usuário. Ordem: apelido exato/prefixo primeiro, depois nome. Rate limit por usuário. | `200 ProfileSummaryDto[]` · `400 validation.failed` (`errors.q`/`errors.limit`) |
| GET | `/profiles/{handle}` | Cabeçalho do perfil. | `200 ProfileDto` · `404 profile.not_found` |
| POST | `/profiles/{handle}/follow` | Seguir (idempotente). | `204` · `400 follow.self` · `404 profile.not_found` |
| DELETE | `/profiles/{handle}/follow` | Deixar de seguir (idempotente, mesmo se o perfil virou privado). | `204` |
| GET | `/profiles/{handle}/followers?page=&pageSize=` | Seguidores (somente os que são `Public`). | `200 PagedResult<ProfileSummaryDto>` · `404` |
| GET | `/profiles/{handle}/following?page=&pageSize=` | Quem o perfil segue (somente os `Public`). | `200 PagedResult<ProfileSummaryDto>` · `404` |
| GET | `/profiles/{handle}/maps/{authors\|origins}` | Mapa do perfil (sempre só `Read`; sem `status`). Mesma forma do mapa próprio. | `200 MapDataDto` · `404` |
| GET | `/profiles/{handle}/maps/{authors\|origins}/countries/{iso}` | Painel do país do perfil, com livros, autores e resenhas. | `200 ProfileCountryDetailDto` · `404` |
| GET | `/me/followers?page=&pageSize=` · `/me/following?page=&pageSize=` | Minhas listas (visíveis mesmo com perfil não público; contas `Private` na lista são omitidas). | `200 PagedResult<ProfileSummaryDto>` |

```ts
interface ProfileSummaryDto {
  handle: string;
  name: string;
  followersCount: number;
  readBooksCount: number;        // livros Read
  isFollowing: boolean;          // o usuário logado segue este perfil
  followsYou: boolean;           // este perfil segue o usuário logado
}
interface ProfileDto extends ProfileSummaryDto {
  followingCount: number;
  exploredCountriesAuthors: number;
  exploredCountriesOrigins: number;
  memberSince: string;           // ISO
  isSelf: boolean;               // quando true, isFollowing/followsYou = false
}
interface ProfileBookDto {       // sem libraryItemId: não expõe ids da biblioteca de terceiros
  bookId: string;
  title: string;
  coverUrl: string | null;
  authors: string[];
  finishedDate: string | null;
  rating: number | null;
  review: string | null;         // texto da resenha (público neste perfil)
}
interface ProfileCountryAuthorDto { authorId: string; name: string; countryIso: string; countryName: string; books: ProfileBookDto[]; }
interface ProfileCountryDetailDto {
  mapType: MapType; iso: string; name: string; continent: Continent;
  bookCount: number; authorCount: number;
  books: ProfileBookDto[];                 // finishedDate desc
  authors: ProfileCountryAuthorDto[];      // mapa authors; [] no mapa origins
  status: "Read";
}
```

Capas: `coverUrl` de livro de terceiros continua o caminho interno `GET /books/{id}/cover`, que é do catálogo compartilhado (qualquer usuário autenticado já pode lê-lo). Dados bibliográficos nunca são privados; só a experiência de leitura (resenha, nota, datas, status) é do usuário.

## 9. Escala de cores e GeoJSON (frontend)

- GeoJSON (frontend; sem impacto nos contratos HTTP): dois arquivos gerados por `scripts/fetch-geo.mjs` (`npm run geo:fetch`) a partir do **Natural Earth 1:50m** (domínio público), servidos em `/geo/` (fora do bundle JS). Ambos usam a propriedade `iso` (ISO 3166-1 alpha-3 = `GeoJsonId` do backend) como chave, uma única feature por `iso`.
  - `public/geo/countries-50m.geojson` — polígonos (≈ 667 kB). Base: `ne_50m_admin_0_countries`; quando `ISO_A3 = -99`, usa `ISO_A3_EH`/`ADM0_A3` (França `FRA`, Noruega `NOR`); Kosovo `KOS` → `XKX`. Chipre do Norte (`CYN`), Somalilândia (`SOL`) e Sianchen (`KAS`) não têm ISO no seed e nunca são coloridos. Territórios que o dataset base embute em outro país são separados via `ne_50m_admin_0_map_units`: `FRA` = França metropolitana + Córsega; `GUF`, `GLP`, `MTQ`, `REU`, `MYT` são features próprias; `SJM` sai de `NOR`; `CXR` e `CCK` substituem "Indian Ocean Ter.". Os polígonos são simplificados e arredondados (2 casas decimais).
  - `public/geo/country-points.geojson` — marcadores (`Point`, ≈ 12 kB) para países/territórios pequenos demais para serem vistos como polígono no mapa-múndi. Critério (constante `POINT_MAX_AREA_DEG2` do script): área < 1,0 °² (≈ 1° × 1°, com correção de cos(latitude)), mais os `iso` de `ne_50m_admin_0_tiny_countries` sem polígono (`BES`, `GIB`, `TKL`). A posição vem do dataset `tiny_countries` quando existe, senão do centro do maior polígono. Maurício (`MUS`), Seicheles, Malta, Singapura, Maldivas, Bahrein, Cabo Verde e São Tomé têm marcador; a Guiana Francesa (≈ 6,8 °²) não precisa e é desenhada como polígono. O polígono continua existindo para quem tem os dois; o marcador é uma camada `circle` acima dele, com o mesmo estado (`feature-state`) e os mesmos eventos de hover/clique.
  - Cidades-estado/territórios sem geometria em nenhum dataset: `BVT` (Ilha Bouvet) e `UMI` (Ilhas Menores Distantes dos EUA) existem no seed, mas não no mapa (continuam na lista lateral).
- `intensity = n === 0 ? 0 : log1p(n) / log1p(maxBookCount)`.
- Paradas (identidade visual):

| intensity | cor | uso |
|---|---|---|
| — (n = 0) | `#0f1a2b` | país inexplorado |
| 0.00 | `#13294b` | poucos livros |
| 0.35 | `#1f4e8c` | intermediário |
| 0.65 | `#3f86d1` | muitos |
| 1.00 | `#bfe0ff` | grande concentração |

- Fundo do mapa/oceano `#060b14`; bordas `rgba(160,190,230,0.18)`, 0.5 px; país selecionado com borda `#e6f1ff` 1.5 px.

## 10. Health

`GET 🌐 /health` → `200 "Healthy"`.

## Changelog

- v1.12 (Etapa 5: perfis públicos e seguidores — aditivo, exceto o valor novo do enum) — `MapVisibility` ganha `Public`; `UserProfileDto` ganha `handle`, `followersCount`, `followingCount`; `UpdateProfileRequest.handle` opcional; seção 8.1 (`/profiles`, `/me/followers`, `/me/following`); códigos `profile.*` e `follow.self`. Migration: `user.handle` (único, minúsculas, nulo para contas existentes) e `user_follow(follower_id, followed_id, created_at)` com PK composta e CHECK follower ≠ followed. Contas existentes permanecem `Private` sem apelido. Também `user.normalized_name` (busca sem acento; recalculado a cada atualização de perfil). `followersCount`/`followingCount` contam todas as contas (inclusive `Private`), enquanto as listas omitem as `Private`; por isso `totalCount` da lista pode ser menor que a contagem. Seguir exige alvo `Public`, mas o seguidor pode ter qualquer visibilidade. Clientes antigos que não conhecem `Public` devem tratá-lo como valor desconhecido (o frontend e o backend desta versão o suportam).
- v1.11 (deduplicação de autores e livros do autor no mapa — aditivo, compatível) — `CountryAuthorDto.books` (livros do autor no filtro de status; o card passa a ser clicável); `GET /authors/duplicates` e `POST /authors/merge` (só `Moderator`, `403 author.moderator_required`). Regras do merge (uma transação): (1) cada `book_author` dos sources é movido para o alvo mantendo `position`; se o alvo já está no livro, o vínculo do source é descartado; nenhum livro perde autor. (2) `author_country`: união por (país, relação); em conflito de chave mantém a linha do alvo, preferindo `isVerified`/maior confiança se a do source for melhor. (3) `external_author_identifier`: move os que o alvo não tem do mesmo provedor; descarta os demais. (4) `BirthDate` do alvo só é preenchida se nula. (5) nome do alvo preservado. (6) sources são excluídos. Não altera `UserBook`. Operação idempotente quanto ao alvo; reexecutar com sources já removidos → `404 author.not_found`.
- v1.10 (autocomplete de autores e painel de autores — aditivo, compatível) — `GET /authors?q=&limit=` (sugestões; `400 validation.failed` em `errors.q`/`errors.limit`); `CreateBookRequest.authorIds` opcional (alinhado a `authorNames`; reutiliza autor já cadastrado e evita duplicatas; tamanho diferente de `authorNames`, id inexistente ou id repetido → `400 validation.failed` em `errors.authorIds`); `CountryDetailDto.authors` (`CountryAuthorDto[]`, sempre presente; vazio no mapa `origins`). `books` continua sendo devolvido nos dois mapas (o frontend só o exibe no mapa de origens). Nada removido ou renomeado.
- v1.9 (filtros de dados geográficos e de status — aditivo, compatível) — `GET /library?geoMissing=any|origin|author|both` (livros sem relação geográfica gravada; `400 validation.failed` em `errors.geoMissing` se inválido). `GET /maps/authors|origins` e `GET /maps/{type}/countries/{iso}` ganham `status=Read|WantToRead|Reading|Abandoned|All` (padrão `Read` = comportamento anterior; `400 validation.failed` em `errors.status`) e `MapDataDto`/`CountryDetailDto` ganham o campo `status` (eco do filtro, sempre presente). `totalReadBooks` mantém o nome mas conta o conjunto do status escolhido. Estatísticas, Passaportes, compartilhamento e mapas públicos não mudam (só `Read`). Nada removido ou renomeado.
- v1.8 (backend de capa/moderação, verificado) — `POST /books/{id}/cover`: o multipart é lido manualmente na action (para devolver `413 cover.too_large` em vez de 400 genérico), por isso o OpenAPI não lista o campo `file`; limite do request 3 MB, regra exata de 2 MB no serviço. Reenviar exatamente o conjunto atual de países é no-op `200` para usuário comum. `PUT /books/{id}` (dados bibliográficos) exige o livro na biblioteca, inclusive para moderador; só os endpoints de países dispensam isso para moderador. A checagem de biblioteca vem antes da regra de moderador para usuário comum (`book.not_in_library`). O papel exibido na tela só muda após novo login/refresh, mas a permissão no servidor vale na hora (lida do banco). Promoção/rebaixamento só por `backend/scripts/promote-moderator.sh|.ps1`.
- v1.7 — papéis: `UserProfileDto.role` (`User|Moderator`, claim `role` no JWT; permissão lida do banco); regra de países: primeira informação por qualquer usuário com o livro, alterar/remover só `Moderator` (`403 geo.moderator_required`).
- v1.6 — upload de capa: `POST/DELETE/GET /books/{id}/cover`, armazenada no PostgreSQL (`book_cover`), `coverUrl` interno relativo; códigos `cover.*`. Compatível (aditivo). A busca (`GET /books/search`) já consulta o catálogo local primeiro, então livros cadastrados manualmente aparecem nela.
- v1.5 (robustez da Etapa 3/4) — `POST /library`: orçamento de 12 s, 201 com enriquecimento parcial, gravação independente de abort do cliente; `409 library.duplicate` traz `libraryItemId`. Fontes: 429 de Open Library/Wikidata pausa a fonte por 5–10 min (a busca degrada para as demais).
- v1.4 (Etapa 4) — endpoints `GET /maps/authors|origins` e `GET /maps/{type}/countries/{iso}` implementados; semântica de país sem livros / ISO inválido; definição de `authorCount` por mapa.
- v1.3 (fechamento da Etapa 3) — catálogo de erros completo; semântica de `countryIso` sem `mapType`; `GET /authors/{id}/books` só devolve livros da biblioteca do usuário.
- v1.2 (Etapa 3, agente principal) — `BookSearchResultDto.libraryItemId`; semântica de `originCountryIsos` em `PUT /books/{id}`; `409 book.duplicate_isbn`; códigos `book.not_in_library` e `author.not_in_library` (403, correção manual exige o item na biblioteca). Mudanças compatíveis (campos aditivos).
- v1 (Etapa 1) — versão inicial.
- v1.1 (Etapa 2, revisão do agente principal) — catálogo de códigos de erro; `501 feature.not_implemented` para rotas ainda não implementadas; nota sobre ids Open Library/VIAF; caminho e normalização do GeoJSON. Nenhuma mudança incompatível. Conferido automaticamente: todos os schemas do `/openapi/v1.json` batem campo a campo (nomes, enums, nulabilidade) com `frontend/.../src/types/api.ts`, e todas as rotas chamadas pelo frontend existem no OpenAPI.
