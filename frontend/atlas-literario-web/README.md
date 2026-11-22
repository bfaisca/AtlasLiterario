# Atlas Literário — Web

Frontend do Atlas Literário: Vue 3 + TypeScript + Vite, Pinia, Vue Router, Axios e MapLibre GL JS.
Arquitetura e contratos: `../../docs/architecture.md` e `../../docs/api-contracts.md` (vinculante para `src/types/api.ts`).

## Requisitos

- Node.js ≥ 20.19 (recomendado 22). O `package-lock.json` foi gerado com **npm 11**; com npm 10, `npm install`
  sem lockfile falha (bug do npm com peer deps opcionais do Vitest 4.1). `npm ci` usa o lockfile e funciona.

## Primeiros passos

```bash
npm ci                    # ou npm install (npm 11+)
cp .env.example .env.local
npm run geo:fetch         # gera public/geo/countries-50m.geojson e country-points.geojson (Natural Earth 1:50m)
npm run dev               # http://localhost:5173 — proxy /api → http://localhost:5080
```

Com `VITE_USE_MOCKS=true` o app roda sem backend (dados em memória). Login de demonstração:
`leitora@atlas.dev` / `atlas1234` (usuária comum). Moderadora de demonstração: `moderadora@atlas.dev` / `atlas1234` — só moderadores alteram países já informados (contrato v1.7); a usuária comum só preenche conjuntos vazios ("Informar país"). Atalhos do mock:

- redefinição de senha: `/reset-password?token=demo-token`;
- pesquisa: "machado", "kafka", "márquez" trazem resultados do catálogo + externos simulados (Google Books/Open Library);
  o ISBN fictício `9780000000002` encontra "Torto Arado"; o termo `offline` simula `502 external.unavailable`; cadastrar um e-mail que comece com `vazia` (ex.: `vazia@atlas.dev`) zera a biblioteca para exercitar os estados vazios;
- livros importados de resultados externos chegam com geografia `Pending` ("Buscar dados novamente" simula o Wikidata).

## Scripts

| Script | Descrição |
|---|---|
| `npm run dev` | Servidor de desenvolvimento |
| `npm run build` | Type-check (`vue-tsc`) + build de produção em `dist/` |
| `npm run type-check` | Somente `vue-tsc --build` |
| `npm run test` | Vitest (jsdom) |
| `npm run lint` / `lint:check` | ESLint (com/sem `--fix`) |
| `npm run format` / `format:check` | Prettier |
| `npm run geo:fetch` | Gera `public/geo/countries-50m.geojson` (polígonos) e `public/geo/country-points.geojson` (marcadores de países pequenos); propriedades `iso`, `name`, `namePt` |

## Variáveis de ambiente

| Variável | Padrão | Uso |
|---|---|---|
| `VITE_API_BASE_URL` | `/api` | Base da API REST |
| `VITE_USE_MOCKS` | `false` | `true` usa `src/mocks/` em vez da API |
| `VITE_DEV_API_PROXY` | `http://localhost:5080` | Alvo do proxy `/api` no `npm run dev` (lida pelo `vite.config.ts`) |

## Estrutura

```
src/
  api/          cliente Axios (JWT, refresh com fila, ApiError) + um módulo por recurso
  mocks/        implementação em memória da mesma fachada + agregação pura dos mapas
  types/api.ts  tipos espelhando docs/api-contracts.md
  stores/       Pinia: auth, ui, library, maps, statistics
  composables/  useColorScale, useCountriesGeo, useFormErrors
  components/   ui/, books/, map/, stats/
  layouts/      AppLayout (sidebar / barra inferior), AuthLayout
  pages/        páginas (auth funcionais; demais em esqueleto)
  router/       rotas lazy + guards (meta.requiresAuth / meta.guestOnly)
  styles/       tokens, reset, base (fontes opcionais em fonts.css)
```

## Docker

`Dockerfile` multi-stage (node:22 → nginx). O nginx serve a SPA com fallback para `index.html` e faz proxy de
`/api` para `http://api:5080` (serviço do `docker-compose.yml`). O build tenta baixar o GeoJSON se ele não existir
(`--build-arg FETCH_GEO=false` para desativar).

## Licenças de dados

Fronteiras: Natural Earth (domínio público). Dados bibliográficos via backend: Google Books, Open Library,
Wikidata (CC0). Capas são exibidas por URL, nunca redistribuídas.
