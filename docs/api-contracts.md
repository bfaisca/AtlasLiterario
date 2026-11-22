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
| Geral | `validation.failed`, `feature.not_implemented`, `external.unavailable` |
| Auth | `auth.unauthorized`, `auth.invalid_credentials`, `auth.email_taken`, `auth.invalid_refresh_token`, `auth.invalid_reset_token`, `auth.invalid_password`, `auth.user_not_found` |
| Usuário | `user.invalid_name`, `user.invalid_email`, `user.invalid_map_visibility` |
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
type MapVisibility = "Private" | "LinkOnly";
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
interface UpdateProfileRequest { name: string; mapVisibility: MapVisibility }

interface AuthResponse {
  accessToken: string;
  accessTokenExpiresAt: string;   // ISO UTC
  refreshToken: string;
  user: UserProfileDto;
}
interface UserProfileDto { id: string; name: string; email: string; role: UserRole; mapVisibility: MapVisibility; createdAt: string }
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

Query de `GET /library` (`countryIso` sem `mapType` casa com qualquer um dos dois mapas): `q` (título/autor), `status` (repetível: `status=Read&status=Reading`), `countryIso`, `mapType` (`authors|origins`, usado com `countryIso`), `genre`, `minRating`, `sort` (`LibrarySort`, padrão `createdAt`), `order` (`asc|desc`, padrão `desc`), `page`, `pageSize`.

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
| GET | `/authors/{id}` | `200 AuthorDetailsDto` |
| GET | `/authors/{id}/books` | `200 BookDetailsDto[]` (somente os livros deste autor que estão na biblioteca do usuário) |
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
| GET | `/maps/authors` | `200 MapDataDto` |
| GET | `/maps/origins` | `200 MapDataDto` |
| GET | `/maps/{type}/countries/{iso}` (`type` = `authors\|origins`) | `200 CountryDetailDto` (painel lateral ao clicar). País válido sem livros → `200` com `bookCount: 0` e `books: []`; ISO desconhecido ou malformado → `400 country.unknown_iso`; `type` inválido → `404`. O ISO é aceito em qualquer caixa; `books` ordenado por `finishedDate` desc (nulos por último, desempate por título). |
| POST | `/maps/share` | body `CreateMapShareRequest` → `201 MapShareDto` · `403 share.private_profile` |
| GET | `/maps/share` | `200 MapShareDto[]` (links ativos do usuário) |
| DELETE | `/maps/share/{token}` | `204` |
| GET 🌐 | `/public/maps/{token}` | `200 PublicMapDto` · `404` |

```ts
interface MapDataDto {
  mapType: MapType;
  totalReadBooks: number;          // |R|
  exploredCountries: number;       // países com bookCount ≥ 1
  maxBookCount: number;            // para a escala de cores
  unknownBookCount: number;        // livros lidos sem país conhecido neste mapa
  countries: CountryMapDatum[];    // apenas países com bookCount ≥ 1
}

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

## 9. Escala de cores e GeoJSON (frontend)

- GeoJSON: Natural Earth **1:110m Admin 0 – Countries** (domínio público), normalizado por `scripts/fetch-geo.mjs` para a propriedade `iso` (alpha-3). Quando `ISO_A3 = -99`, usa `ADM0_A3` (França `FRA`, Noruega `NOR`); Kosovo `KOS` → `XKX` (mesmo código do seed do backend). Chipre do Norte (`CYN`) e Somalilândia (`SOL`) não têm ISO e nunca são coloridos. Arquivo: `public/geo/countries-110m.geojson`, servido em `/geo/countries-110m.geojson` (fora do bundle JS).
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

- v1.8 (backend de capa/moderação, verificado) — `POST /books/{id}/cover`: o multipart é lido manualmente na action (para devolver `413 cover.too_large` em vez de 400 genérico), por isso o OpenAPI não lista o campo `file`; limite do request 3 MB, regra exata de 2 MB no serviço. Reenviar exatamente o conjunto atual de países é no-op `200` para usuário comum. `PUT /books/{id}` (dados bibliográficos) exige o livro na biblioteca, inclusive para moderador; só os endpoints de países dispensam isso para moderador. A checagem de biblioteca vem antes da regra de moderador para usuário comum (`book.not_in_library`). O papel exibido na tela só muda após novo login/refresh, mas a permissão no servidor vale na hora (lida do banco). Promoção/rebaixamento só por `backend/scripts/promote-moderator.sh|.ps1`.
- v1.7 — papéis: `UserProfileDto.role` (`User|Moderator`, claim `role` no JWT; permissão lida do banco); regra de países: primeira informação por qualquer usuário com o livro, alterar/remover só `Moderator` (`403 geo.moderator_required`).
- v1.6 — upload de capa: `POST/DELETE/GET /books/{id}/cover`, armazenada no PostgreSQL (`book_cover`), `coverUrl` interno relativo; códigos `cover.*`. Compatível (aditivo). A busca (`GET /books/search`) já consulta o catálogo local primeiro, então livros cadastrados manualmente aparecem nela.
- v1.5 (robustez da Etapa 3/4) — `POST /library`: orçamento de 12 s, 201 com enriquecimento parcial, gravação independente de abort do cliente; `409 library.duplicate` traz `libraryItemId`. Fontes: 429 de Open Library/Wikidata pausa a fonte por 5–10 min (a busca degrada para as demais).
- v1.4 (Etapa 4) — endpoints `GET /maps/authors|origins` e `GET /maps/{type}/countries/{iso}` implementados; semântica de país sem livros / ISO inválido; definição de `authorCount` por mapa.
- v1.3 (fechamento da Etapa 3) — catálogo de erros completo; semântica de `countryIso` sem `mapType`; `GET /authors/{id}/books` só devolve livros da biblioteca do usuário.
- v1.2 (Etapa 3, agente principal) — `BookSearchResultDto.libraryItemId`; semântica de `originCountryIsos` em `PUT /books/{id}`; `409 book.duplicate_isbn`; códigos `book.not_in_library` e `author.not_in_library` (403, correção manual exige o item na biblioteca). Mudanças compatíveis (campos aditivos).
- v1 (Etapa 1) — versão inicial.
- v1.1 (Etapa 2, revisão do agente principal) — catálogo de códigos de erro; `501 feature.not_implemented` para rotas ainda não implementadas; nota sobre ids Open Library/VIAF; caminho e normalização do GeoJSON. Nenhuma mudança incompatível. Conferido automaticamente: todos os schemas do `/openapi/v1.json` batem campo a campo (nomes, enums, nulabilidade) com `frontend/.../src/types/api.ts`, e todas as rotas chamadas pelo frontend existem no OpenAPI.
