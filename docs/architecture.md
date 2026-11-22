# Arquitetura — Atlas Literário

> Status: **v0.1 (Etapa 1)** — definida pelo agente principal. Mudanças estruturais devem ser registradas aqui.

## 1. Visão geral

```
┌──────────────────────────┐      HTTPS / JSON (REST)      ┌─────────────────────────────────────┐
│  frontend (Vue 3 + Vite) │  ───────────────────────────▶ │  backend (ASP.NET Core Web API .NET10)│
│  Pinia · Router · Axios  │  ◀───────────────────────────  │  Clean Architecture                  │
│  MapLibre GL JS          │        JWT Bearer               │                                     │
└──────────────────────────┘                                 │  API → Application → Domain         │
                                                             │        ↑                            │
                                                             │  Infrastructure (EF Core, HTTP)     │
                                                             └──────┬────────────────┬─────────────┘
                                                                    │                │
                                                             PostgreSQL 17     Google Books · Open Library · Wikidata
```

- Frontend e backend são **desacoplados**; a API REST é o único canal de comunicação.
- **Todas** as chamadas a fontes externas são feitas pelo backend (o frontend nunca chama Google Books/Open Library/Wikidata).
- O **OpenAPI** gerado pelo backend é a referência oficial dos contratos. `docs/api-contracts.md` é a especificação acordada antes da implementação; divergências devem ser resolvidas pelo agente principal.

## 2. Backend — camadas

| Projeto | Responsabilidade | Depende de |
|---|---|---|
| `AtlasLiterario.Domain` | Entidades, enums, value objects, regras invariantes (ex.: transição de status, validação de nota 1–5). Sem dependências externas. | — |
| `AtlasLiterario.Application` | Casos de uso (serviços), DTOs, interfaces (`IAppDbContext`/repositórios, `IBookMetadataProvider`, `IGeoResolver`, `IPasswordHasher`, `ITokenService`, `IClock`, `ICurrentUser`, `IEmailSender`), validação, agregação dos mapas e estatísticas. | Domain |
| `AtlasLiterario.Infrastructure` | EF Core + Npgsql (DbContext, configurações, migrations, seed de países), clientes HTTP (Google Books, Open Library, Wikidata), cache, hashing, JWT. | Application, Domain |
| `AtlasLiterario.API` | Controllers finos, autenticação/autorização, middleware de erros (ProblemDetails), OpenAPI/Swagger, CORS, DI, health check. | Application, Infrastructure |

Testes (`backend/tests`):
- `AtlasLiterario.Domain.Tests` / `AtlasLiterario.Application.Tests` — xUnit, regras de negócio e agregações (EF InMemory ou SQLite para consultas).
- `AtlasLiterario.API.IntegrationTests` — `WebApplicationFactory` (+ Testcontainers PostgreSQL quando o Docker estiver disponível).

Decisões:
- **Sem MediatR/CQRS** no MVP: serviços de aplicação simples (`ILibraryService`, `IBookService`, `IMapService`...). Menos dependências, menos cerimônia.
- **Validação**: DataAnnotations/validação manual no Application; erros retornam `ValidationProblemDetails` (400).
- **Erros**: middleware único → RFC 7807 `ProblemDetails` (`application/problem+json`). Exceções de domínio mapeadas: `NotFoundException`→404, `ConflictException`→409, `ForbiddenException`→403, `ValidationException`→400.
- **Autenticação**: JWT Bearer de acesso (60 min) + refresh token opaco (30 dias, armazenado com hash, rotacionado). Senhas com `PasswordHasher<T>` do ASP.NET Core Identity (PBKDF2) — sem adotar o Identity completo.
- **Datas**: UTC (`timestamptz`); datas de leitura são `DateOnly` (`date`).
- **Configuração sensível** por variáveis de ambiente (`ConnectionStrings__Default`, `Jwt__Secret`, `GoogleBooks__ApiKey`).

## 3. Frontend — organização

```
frontend/atlas-literario-web/src/
  api/            # cliente Axios centralizado, interceptors (JWT, refresh, erros), um módulo por recurso
  mocks/          # dados simulados + adaptador de mock (VITE_USE_MOCKS=true)
  types/          # tipos TypeScript espelhando docs/api-contracts.md
  stores/         # Pinia: auth, library, maps, statistics, ui
  composables/    # useColorScale, useBreakpoint, useShareImage, ...
  components/     # ui/ (botões, inputs, cards), books/, map/, stats/
  layouts/        # AppLayout (autenticado), AuthLayout
  pages/          # Dashboard, Library, BookDetails, Search, Map, Passports, Profile, Login, Register...
  router/         # rotas + guards de autenticação
  styles/         # tokens CSS (variáveis), reset, tipografia
public/geo/       # GeoJSON dos países (Natural Earth 1:110m, domínio público), carregado via fetch
```

Decisões:
- **MapLibre GL JS** sem mapa-base de tiles: um único source GeoJSON de países com camadas `fill` + `line` sobre fundo escuro. Vantagens: WebGL (fluido em mobile), *feature-state* para hover/seleção sem re-render, `preserveDrawingBuffer` para exportar o canvas na geração da imagem, sem dependência de provedor de tiles/chave. Alternativa considerada: D3 + SVG (mais simples, porém pior desempenho com zoom/pan em mobile e exportação de PNG mais trabalhosa).
- O componente de mapa é **agnóstico de regra de negócio**: recebe `CountryMapDatum[]` (ver contratos) + escala de cor, emite `country-hover`/`country-select`.
- Mocks: camada de serviço com implementação real (Axios) e mock com a mesma interface, selecionada por `VITE_USE_MOCKS`.
- Testes: Vitest + Vue Test Utils (componentes e stores críticos).

## 4. Fluxo de cadastro automatizado de um livro

1. Usuário pesquisa (`GET /api/books/search?q=...&type=title|author|isbn`).
2. Backend consulta o **catálogo local** primeiro; em seguida Google Books (+ Open Library em ISBN). Resultados consolidados e deduplicados por ISBN-13 / IDs externos → `BookSearchResultDto[]` (sem persistir).
3. Usuário escolhe um resultado → `POST /api/library` com `{ source: { provider, externalId, isbn13 } }` (ou `bookId` se já estiver no catálogo).
4. Backend: importa/encontra o `Book` (deduplicação), resolve autores (Open Library → Wikidata QID), resolve geografia (Wikidata P27 para autores, P495 para obras), grava procedência (`Source`, `Confidence`, `IsVerified=false`) e cria o `UserBook`.
5. Dados ausentes ficam **desconhecidos**; o frontend sinaliza "origem desconhecida" e permite correção manual (`PUT /api/books/{id}/origin-countries`, `PUT /api/authors/{id}/countries`).

O enriquecimento geográfico é síncrono no MVP, com timeout curto por fonte (≈5 s) e degradação graciosa: se o Wikidata falhar, o livro é salvo e a geografia fica `pending`, podendo ser reprocessada (`POST /api/books/{id}/enrich`).

## 5. Mapas e estatísticas

- Calculados **sob demanda** a partir de `UserBook (Status = Read)` com consultas agregadas (sem tabelas materializadas no MVP). Mudanças de status ou de dados geográficos refletem imediatamente — atende às regras 4 e 12 sem invalidação de cache.
- Índices garantem desempenho: `UserBook(UserId, Status)`, `BookAuthor(AuthorId)`, `AuthorCountry(CountryId)`, `BookOriginCountry(CountryId)`.
- Regras de contagem: ver `docs/business-rules.md` §3.

## 6. Compartilhamento

- A **imagem** é gerada no frontend (canvas do MapLibre + composição em `<canvas>` 1200×630 e 1080×1350) — sem renderização de mapa no servidor no MVP.
- `POST /api/maps/share` cria um **link público opcional** com token aleatório que expõe **somente** os agregados do mapa (países e contagens), nunca títulos, resenhas ou a biblioteca. O usuário pode revogar (`DELETE /api/maps/share/{token}`).

## 7. Ambiente

- `docker-compose.yml`: `db` (postgres:17), `api` (build de `backend/`), `web` (build de `frontend/atlas-literario-web`, servido por nginx).
- Desenvolvimento local: `docker compose up db` + `dotnet run` + `npm run dev` (proxy do Vite `/api` → `http://localhost:5080`).
- Portas: API `5080`, Web `5173` (dev) / `8080` (container), Postgres `5432`.
