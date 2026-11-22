# Atlas Literário

Plataforma web de catalogação de livros e exploração geográfica da literatura: sua biblioteca pessoal vira um atlas, com dois mapas-múndi independentes — **nacionalidade dos autores** e **origem das obras**.

| Camada | Stack |
|---|---|
| Backend | C# · .NET 10 · ASP.NET Core Web API · EF Core · PostgreSQL · JWT · OpenAPI/Swagger |
| Frontend | Vue 3 · TypeScript · Vite · Pinia · Vue Router · Axios · MapLibre GL JS |
| Infra | Docker Compose |

## Estrutura

```
backend/                       # solução .NET (Domain, Application, Infrastructure, API + tests)
frontend/atlas-literario-web/  # SPA Vue 3
docs/                          # arquitetura, banco, contratos, integrações, regras, plano
docker-compose.yml
```

Documentação principal: [`docs/architecture.md`](docs/architecture.md) · [`docs/api-contracts.md`](docs/api-contracts.md) · [`docs/business-rules.md`](docs/business-rules.md) · [`docs/development-plan.md`](docs/development-plan.md).

## Pré-requisitos

- .NET SDK 10.0.1xx
- Node.js 22+ e npm
- Docker Desktop (PostgreSQL e/ou stack completa)
- `dotnet-ef` (`dotnet tool install --global dotnet-ef`)

## Rodando em desenvolvimento

```bash
cp .env.example .env                  # ajuste JWT_SECRET
docker compose up -d db               # PostgreSQL em localhost:5432

# Backend (http://localhost:5080, Swagger em /swagger)
cd backend
dotnet user-secrets --project src/AtlasLiterario.API set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=atlas_literario;Username=atlas;Password=atlas_dev_password"
dotnet user-secrets --project src/AtlasLiterario.API set "Jwt:Secret" "<segredo com 32+ caracteres>"
dotnet run --project src/AtlasLiterario.API   # em Development aplica as migrations (com seed de 250 países) no startup

# Frontend (http://localhost:5173)
cd frontend/atlas-literario-web
npm ci                                # usa o package-lock.json (ver nota abaixo)
npm run dev                           # VITE_USE_MOCKS=true (padrão do .env.example) usa dados simulados sem backend
                                      # VITE_USE_MOCKS=false usa a API real via proxy /api → :5080
```

O GeoJSON dos países já está em `frontend/atlas-literario-web/public/geo/`; `npm run geo:fetch` o baixa de novo do Natural Earth.

> **npm:** com o npm 10 e o Vitest 4.1.10 ou mais novo, `npm install` falha com o erro `edgesOut`. Use `npm ci` (lê o lockfile) ou o npm 11 (`npx npm@11 install`).

Stack completa em containers: `docker compose up --build` → web em http://localhost:8080, API em http://localhost:5080.

## Testes

```bash
cd backend && dotnet test                                    # 93 testes (domínio, aplicação, integração com EF InMemory)
cd frontend/atlas-literario-web && npm run type-check && npm run lint && npm run test -- --run && npm run build
```

## Fontes de dados

Google Books, Open Library e Wikidata (consultados apenas pelo backend). GeoJSON: Natural Earth.
