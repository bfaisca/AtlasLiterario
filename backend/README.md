# Atlas Literário — Backend

ASP.NET Core Web API (.NET 10), Clean Architecture, EF Core 10 + PostgreSQL.

```
src/
  AtlasLiterario.Domain          entities, enums, invariants, domain exceptions (no dependencies)
  AtlasLiterario.Application     services (AuthService, CountryService), DTOs = API contracts, interfaces, MapAggregator
  AtlasLiterario.Infrastructure  AppDbContext, configurations, migrations, seeds, JWT, hashing, external clients (skeletons)
  AtlasLiterario.API             controllers, ProblemDetails, JWT Bearer, OpenAPI/Swagger, CORS, health check
tests/
  AtlasLiterario.Domain.Tests          domain rules (UserBook, ISBN, normalization)
  AtlasLiterario.Application.Tests     MapAggregator, AuthService, seed/model (EF InMemory)
  AtlasLiterario.API.IntegrationTests  WebApplicationFactory + EF InMemory (no PostgreSQL needed)
```

## Requirements

- .NET SDK 10 (`global.json`: 10.0.100, rollForward latestFeature)
- PostgreSQL 17 (`docker compose up db` from the repository root)

## Configuration (secrets never go into appsettings)

| Key | Env var | Notes |
|---|---|---|
| `ConnectionStrings:Default` | `ConnectionStrings__Default` | required |
| `Jwt:Secret` | `Jwt__Secret` | required, ≥ 32 chars (startup fails otherwise) |
| `Cors:AllowedOrigins` | `Cors__AllowedOrigins__0` | default `http://localhost:5173` |
| `App:FrontendBaseUrl` | `App__FrontendBaseUrl` | used in the reset-password link |
| `Database:ApplyMigrationsOnStartup` | `Database__ApplyMigrationsOnStartup` | `true` in Development |
| `GoogleBooks:ApiKey` | `GoogleBooks__ApiKey` | optional but recommended; sent in the `x-goog-api-key` header (never in the URL, so it cannot leak through HttpClient logs). Without it Google Books answers 429 often and search degrades to Open Library |
| `ExternalSources:EnrichmentBudgetSeconds` | `ExternalSources__EnrichmentBudgetSeconds` | total time of `POST /api/library` on external sources (import + enrichment), counted from the start of the request; default 12. Unfinished work stays Pending/Partial/Failed, the response is still 201 |
| `GoogleBooks:Country` | `GoogleBooks__Country` | default `BR`; required because Google cannot always geolocate the server IP (403 otherwise) |
| `ExternalSources:Contact` | `ExternalSources__Contact` | contact appended to the User-Agent (Wikidata policy) |

Local development with user-secrets:

```bash
cd src/AtlasLiterario.API
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=atlas_literario;Username=atlas;Password=<senha>"
dotnet user-secrets set "Jwt:Secret" "<32+ caracteres aleatórios>"
```

## Run

```bash
dotnet run --project src/AtlasLiterario.API      # http://localhost:5080
```

- Swagger UI: http://localhost:5080/swagger — OpenAPI (official contract): http://localhost:5080/openapi/v1.json
- Health: http://localhost:5080/health
- In Development the reset-password link is written to the log (`[DEV] Password reset link ...`).

## External sources and library (Etapa 3)

- Clients (Infrastructure/ExternalSources): Google Books, Open Library, Wikidata (SPARQL). 5 s timeout per attempt
  (10 s SPARQL), 1 retry (Google 429 is not retried), User-Agent with `ExternalSources:Contact`, results cached in
  `external_lookup_cache` (searches 24 h, entities 30 d).
- Search degrades: a failing provider is logged and skipped; `502` only if every provider fails and nothing is local.
- `POST /api/library` commits the book and the library item before the best-effort enrichment, and commits never depend on
  `RequestAborted`. A 429 with `Retry-After` > 2 s is not waited for and pauses the source for 5–10 min; a 409
  `library.duplicate` carries `libraryItemId`.
- Import is synchronous and idempotent (dedupe by ISBN-13 / external ids); Wikidata failure leaves the book saved with
  `geoEnrichmentStatus` Pending/Failed (`POST /api/books/{id}/enrich` retries). Manual corrections are never overwritten.
- Implemented: `/api/books/*`, `/api/library/*`, `/api/authors/*`, `/api/maps/{authors|origins}` and
  `/api/maps/{type}/countries/{iso}`. Map sharing, public maps and statistics still return 501.
- Never put the Google key in files: `export GoogleBooks__ApiKey=...` only in the shell that starts the API.

## Maps (Etapa 4)

`MapService` loads, for the authenticated user only, the `Read` library items and their relations with flat
`AsNoTracking` projections (no descriptions/reviews, 3 queries per map: library, book-author, and
citizenships (authors map, `Citizenship` only) or book origins (origins map)), then `MapAggregator` counts
(docs/business-rules.md §3). Country detail: valid country without books → `200` with `bookCount: 0`;
unknown ISO → `400 country.unknown_iso`. No new indexes were needed (`user_book(user_id,status)`, PKs of
`book_author`, `author_country`, `book_origin_country`).

## Covers (upload)

- `POST /api/books/{id}/cover` (multipart, field `file`; JPEG/PNG/WebP only, **max 2 MB**, validated by the leading
  bytes, never by name/Content-Type; needs the book in the caller's library) → `200 BookDetailsDto`;
  `DELETE` removes it (idempotent); `GET` is **anonymous** (an `<img>` cannot send the token) and returns only the
  image with `X-Content-Type-Options: nosniff`, `ETag` (sha256, `If-None-Match` → 304) and
  `Cache-Control: public, max-age=31536000, immutable`; no `Content-Disposition`.
- Errors: `400 cover.invalid_type`, `400 validation.failed` (empty/missing file), `413 cover.too_large`
  (Kestrel/MVC limit is 3 MB for multipart overhead; the exact 2 MB rule is in `BookCoverService`),
  `403 book.not_in_library`, `404 cover.not_found`.
- Stored in PostgreSQL (`book_cover.data`, bytea); `book.cover_url` becomes `/api/books/{id}/cover?v={sha256[..12]}`.
  There is no server-side resizing (the web app shrinks before sending): plan the database size accordingly
  (≤ 2 MB per book). Imports and enrichment never overwrite an uploaded cover.
- `PUT /api/books/{id}` `coverUrl`: the book's own internal URL keeps the upload; an external https URL replaces
  it (and deletes the upload); null/empty removes it; any other relative path is rejected.

## Roles and moderators

`user.role` is `User` (default) or `Moderator`. Authorization **reads the role from the database** on every
country edit (the `role` claim in the JWT is informational), so a demotion is immediate. There is no API/endpoint
and no configured e-mail list to get the role (e-mails are not verified). Promote or demote with the script:

```bash
./scripts/promote-moderator.sh pessoa@exemplo.com              # promote (docker compose exec db psql)
./scripts/promote-moderator.sh pessoa@exemplo.com User         # demote
PSQL_URL=postgresql://atlas:senha@localhost:5432/atlas_literario ./scripts/promote-moderator.sh pessoa@exemplo.com
./scripts/promote-moderator.ps1 -Email pessoa@exemplo.com [-Role User]   # PowerShell
```

Equivalent SQL: `UPDATE "user" SET role = 1 WHERE email = 'pessoa@exemplo.com';` (0 = User). Permission changes apply at once;
the web app shows the new role after a new login or token refresh. Country rules: a user with the book in the
library may fill an **empty** origin/citizenship set once (or resend the current set: no-op); changing or
removing existing countries needs a moderator (`403 geo.moderator_required`); moderators may edit any book/author.

## Migrations

The initial migration (`InitialCreate`, with the 250-country and historical-mapping seeds) is in
`src/AtlasLiterario.Infrastructure/Persistence/Migrations`. It is applied on startup when
`Database:ApplyMigrationsOnStartup=true`, or manually:

```bash
dotnet tool restore
dotnet ef database update -p src/AtlasLiterario.Infrastructure -s src/AtlasLiterario.API
```

New migration: `./scripts/add-migration.sh <Name>` (or `./scripts/add-migration.ps1 -Name <Name>`).

## Tests

```bash
dotnet test AtlasLiterario.sln
dotnet test --filter "Category!=Integration"   # unit tests only
```

Projects: Domain, Application, Infrastructure (HttpClient parsing/import pipeline against recorded fixtures in
`tests/AtlasLiterario.Infrastructure.Tests/Fixtures`, with API keys stripped) and API integration tests.

Integration tests run the real HTTP pipeline with EF Core InMemory. They do **not** cover PostgreSQL-specific
behavior (unique indexes, check constraints, `text[]`/`jsonb`, migrations). Those require a PostgreSQL instance
(Testcontainers is planned once a Docker daemon is available in CI).

## Docker

`Dockerfile` (multi-stage sdk:10.0 → aspnet:10.0, port 5080) is built by the root `docker-compose.yml` (`api` service).
