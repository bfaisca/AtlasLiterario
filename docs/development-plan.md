# Plano de desenvolvimento — Atlas Literário

Legenda: ✅ concluído e verificado · 🟡 parcial · ⏳ pendente

## Agentes e limites

| Agente | Diretório exclusivo | Não pode alterar |
|---|---|---|
| Principal (arquiteto/coordenador) | `docs/`, `docker-compose.yml`, `README.md`, `.gitignore`, `.env.example` | — (revisa tudo) |
| Backend Developer | `backend/` | `frontend/`, `docs/` (propõe mudanças ao principal) |
| Frontend Developer | `frontend/` | `backend/`, `docs/` (propõe mudanças ao principal) |

## Etapas

### Etapa 1 — Arquitetura e planejamento ✅
- Arquitetura, modelagem, contratos, integrações e regras documentados em `docs/`.
- Agentes de backend e frontend criados e executados em paralelo.

### Etapa 2 — Estrutura inicial ✅ (2026-11-15)

**Backend:** solução .NET 10 com 4 camadas e 3 projetos de teste; 15 entidades; DbContext com snake_case, índices e CHECKs; seed de 250 países e 19 mapeamentos históricos; migration `InitialCreate`; autenticação completa (register/login/refresh com rotação e detecção de reuso/logout/me/troca e redefinição de senha); `GET /api/countries`; demais rotas do contrato expostas no OpenAPI respondendo `501`; `MapAggregator` (regra de contagem dos mapas) implementado e testado; ProblemDetails com `code`; OpenAPI + Swagger UI; Dockerfile.

**Frontend:** Vite + Vue 3 + TS; tipos de todos os contratos; cliente Axios com refresh automático e normalização de erros; camada de mocks com agregação idêntica às regras; Pinia; Router com guards; tema escuro/identidade visual; layouts responsivos; componentes base; páginas de autenticação funcionais; dashboard, biblioteca, detalhes, mapa e passaportes em esqueleto com mocks; `WorldMap` (MapLibre 6) com escala log; GeoJSON de países; Dockerfile + nginx.

**Verificações executadas (pelos agentes e repetidas pelo agente principal):**
| Verificação | Resultado |
|---|---|
| `dotnet build` | 0 erros, 0 warnings |
| `dotnet test` | 93/93 (Domain 40, Application 43, Integração 10) |
| Migration aplicada em PostgreSQL real (16.15, ambiente sem Docker) | OK; 250 países |
| `npm run type-check`, `lint`, `format:check` | sem erros |
| `npm run test -- --run` | 47/47 |
| `npm run build` | OK (index 74 kB gzip; MapLibre carregado sob demanda) |
| Contratos OpenAPI × `types/api.ts` (script) | todos os schemas, enums e rotas compatíveis |
| E2E frontend real × API real (Playwright) | cadastro 201 → dashboard; recarregar página → refresh 200, sessão restaurada; rota protegida OK |

**Não verificado:** `docker build`/`docker compose` (não havia daemon Docker); testes de integração contra PostgreSQL via Testcontainers.

### Etapa 3 — Biblioteca e pesquisa ✅ (2026-11-16)
Backend: clientes Google Books / Open Library / Wikidata com cache e resiliência; consolidação e deduplicação; `GET /books/search`; importação (`POST /library` com `source`); resolução de autores e geografia; CRUD de biblioteca com filtros/paginação; correção manual de países.
Frontend: pesquisa e seleção; adicionar à biblioteca; biblioteca com filtros/ordenação; detalhes e edição (status, nota, resenha, datas); correção de países.

### Etapa 4 — Mapas literários ✅ (2026-11-16)
Backend: endpoints de mapa e de detalhe por país usando o `MapAggregator`. Frontend: painel de país, ajuste do enquadramento em mobile (Américas cortadas em retrato).

### Etapa 5 — Dashboard e Passaportes ⏳
### Etapa 6 — Compartilhamento e finalização ⏳

## Etapas 3 e 4 — resultados verificados

- Backend: 166 testes (Domain 40, Application 54, Integração 22, Infrastructure 50), build 0 erros/0 avisos; smoke real com PostgreSQL 16 e Google Books/Open Library/Wikidata.
- Frontend: 131 testes, type-check/lint/build ok; Playwright com mocks em 3 viewports.
- **E2E real (navegador × API × PostgreSQL × fontes externas):** cadastro → busca real → adicionar como Lido → mapas de autores (AUT, CZE, COL) e de origens (AUT, DEU, ARG) pintados corretamente → painel do país com capa do livro.
- **Defeito encontrado no E2E e CORRIGIDO (verificado no E2E real, 3 livros → Completed, mapas com AUT/CZE/COL/JPN):** com 429 do Wikidata, o POST /library passou do timeout do cliente (20 s) e a ação foi perdida (livro órfão Pending, sem user_book). Correção: orçamento ≤ 12 s, gravação independente do abort do cliente, `409` com `libraryItemId`, timeout de 45 s só nas chamadas lentas, progresso e recuperação no frontend. Testes: backend 166, frontend 131.

## Cadastro manual com capa e papel de moderador (2026-11-16) ✅

- Upload de capa (`POST/DELETE/GET /books/{id}/cover`, tabela `book_cover`), redução da imagem no navegador (≤ 800×1200 JPEG), nginx com `client_max_body_size 3m`.
- Papéis `User`/`Moderator`: primeira informação de país por quem tem o livro; alterar/remover só moderador (`403 geo.moderator_required`); promoção por `backend/scripts/promote-moderator.*`.
- Busca: livro manual aparece em `GET /books/search` (catálogo local primeiro), inclusive para outros usuários.
- Verificado: backend 198 testes, frontend 197 testes, build 0/0; E2E real (navegador × API × PostgreSQL): cadastro manual com capa (1400×2000 → 800×1143), usuário B vê a capa no resultado da busca e adiciona, usuário comum sem botão de edição, após promoção B edita (Brasil → Portugal).

## Pendências registradas
- Validar Dockerfiles e `docker compose up --build` numa máquina com Docker.
- Testcontainers (PostgreSQL) nos testes de integração.
- Chave do Google Books (`GOOGLE_BOOKS_API_KEY`): sem chave, a API devolve 429 com frequência.
- Provedor de e-mail real para a redefinição de senha (hoje o link vai para o log).
- 13 alertas `npm audit` moderados, só em ferramentas de desenvolvimento.
- Pausa por 429 das fontes é em memória (por processo).
- Idempotency-Key no POST /library (proposta do frontend) — pós-MVP.
- Fluxo de "sugerir correção" para usuários comuns + fila de revisão para moderadores; tela para promover/rebaixar moderadores.
- Capas em bytea (banco/backup crescem); sem redimensionamento no servidor.
- Script de promoção: só a versão `.sh` foi executada em teste; `.ps1` e `docker compose exec` não foram.
- Paginar `CountryDetailDto.books` (países com 50+ livros) — pós-MVP.
- Filtro da biblioteca por "livros sem país conhecido" (hoje o aviso do mapa só leva a `status=Read`) — pós-MVP.
- Aggregation dos mapas em memória: mover para SQL se a escala crescer.
- Testes de integração com PostgreSQL real (Testcontainers) no CI.


## Ferramenta externa: importação em lote (`tools/bulk-import`) ✅

Ferramenta de debug, fora do app e sem alterar backend/frontend: lê `.txt` (3 linhas por livro), `.xlsx` ou `.csv`,
busca via `GET /api/books/search`, importa com `POST /api/library` o que passa no critério de confiança
(`src/match.mjs`) e gera `relatorio.xlsx` + `pendentes.xlsx`; retomável. Roda via
`docker compose --profile tools run --rm bulk-import <arquivo>` ou Node 20+. Detalhes em `tools/bulk-import/README.md`.
Testes: 31 (`npm test`), mais execução real contra API + PostgreSQL com amostra de 10 livros.
Se for integrada ao site: endpoint de importação no backend reaproveitando a regra de decisão, com revisão dos pendentes na UI.
