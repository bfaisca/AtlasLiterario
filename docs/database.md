# Modelagem do banco — Atlas Literário

> PostgreSQL 17 · EF Core 10 (Npgsql) · nomes de tabelas em `snake_case` (via `UseSnakeCaseNamingConvention` ou configuração explícita) · PKs `uuid` (exceto `country` = `smallint`).
> Esta é a modelagem inicial acordada. O agente de backend pode ajustar índices/restrições, registrando aqui.

## Diagrama (resumo)

```
user 1──* user_book *──1 book 1──* book_author *──1 author
  │                       │                              │
  │ 1──* refresh_token     ├──* book_origin_country *──1 country 1──* author_country *──┘
  │ 1──* password_reset    ├──* external_book_identifier
  │ 1──* map_share         └──* external_author_identifier (em author)
                                 historical_country_mapping *──1 country
                                 external_lookup_cache (independente)
```

## Tabelas

### `user`
| Coluna | Tipo | Regras |
|---|---|---|
| id | uuid PK | |
| name | varchar(100) | obrigatório (nome de exibição) |
| email | varchar(254) | obrigatório; **único** (índice em `lower(email)`) |
| password_hash | text | obrigatório |
| map_visibility | smallint (enum `MapVisibility`: 0 Private, 1 LinkOnly, 2 Public) | padrão 0; `Public` só por escolha explícita e exige `handle` |
| handle | varchar(30), nulo | apelido público (v1.12): minúsculas `^[a-z0-9_]{3,30}$`; **índice único** (NULL permitido; contas existentes ficam sem apelido) |
| normalized_name | varchar(100) | nome sem acento/caixa para a busca de perfis; recalculado em `UpdateProfile`; contas antigas ficam `''` até a primeira atualização de perfil (só importa para perfis `Public`) |
| role | smallint (enum `UserRole`: 0 User, 1 Moderator) | padrão 0; promoção só por script/SQL |
| created_at / updated_at | timestamptz | |

### `user_follow` (v1.12)
| Coluna | Tipo | Regras |
|---|---|---|
| follower_id | uuid FK → user | PK composta; ON DELETE CASCADE |
| followed_id | uuid FK → user | PK composta; ON DELETE CASCADE; índice em `followed_id` |
| created_at | timestamptz | |

CHECK `follower_id <> followed_id`. Relação assimétrica (sem aceite, sem amizade). Contagens são calculadas (`COUNT`), não armazenadas. Migration `AddProfilesAndFollows` apenas adiciona colunas/tabela/índices.

### `refresh_token`
id uuid PK · user_id FK→user (cascade) · token_hash varchar(128) **único** · expires_at · created_at · revoked_at null · replaced_by_token_hash null.

### `password_reset_token`
id uuid PK · user_id FK (cascade) · token_hash **único** · expires_at (1 h) · used_at null · created_at.

### `book`
| Coluna | Tipo | Regras |
|---|---|---|
| id | uuid PK | |
| title | varchar(500) | obrigatório |
| subtitle | varchar(500) null | |
| isbn13 | char(13) null | **único parcial** (`WHERE isbn13 IS NOT NULL`) |
| isbn10 | char(10) null | índice |
| publisher | varchar(300) null | |
| publication_year | smallint null | |
| description | text null | |
| cover_url | varchar(1000) null | |
| original_language | varchar(10) null | código BCP-47/ISO 639 |
| genres | text[] | padrão `{}` (MVP: array; normalizar depois se necessário) |
| geo_enrichment_status | smallint (`Pending`, `Completed`, `Partial`, `Failed`) | |
| created_at / updated_at | timestamptz | |

> Decisão: o modelo inicial tinha um único `ISBN`; separamos ISBN-13 (chave de deduplicação) e ISBN-10.

### `author`
id uuid PK · name varchar(300) obrigatório · birth_date date null · created_at / updated_at · índice em `lower(name)`.

### `book_author`
book_id FK (cascade) · author_id FK (restrict) · position smallint (ordem de exibição) · **PK (book_id, author_id)** · índice em author_id.

### `country`
| Coluna | Tipo | Regras |
|---|---|---|
| id | smallint PK | |
| name | varchar(100) | nome em inglês (o frontend localiza via `Intl.DisplayNames`) |
| name_pt | varchar(100) | nome em português |
| iso_code | char(3) | **ISO 3166-1 alpha-3, único** |
| iso_code2 | char(2) | alpha-2, único |
| continent | smallint (enum `Continent`: Africa, Asia, Europe, NorthAmerica, SouthAmerica, Oceania, Antarctica) | |
| geo_json_id | varchar(10) | id da feature no GeoJSON do frontend (= ISO_A3) |
| wikidata_qid | varchar(20) null | ex.: `Q155` (Brasil), índice único |

Seed: ~250 países/territórios ISO via `HasData` (migration).

### `historical_country_mapping`
id · wikidata_qid varchar(20) (Estado histórico, ex.: `Q28513` Áustria-Hungria) · country_id FK · note. Índice em wikidata_qid. Seed inicial: Áustria-Hungria, URSS, Iugoslávia, Tchecoslováquia, Império Otomano, Império Russo, Prússia/Império Alemão, Reino da Prússia, Império Britânico (→ GBR), Império Austríaco.

### `author_country`
| Coluna | Tipo | Regras |
|---|---|---|
| author_id | uuid FK (cascade) | |
| country_id | smallint FK | |
| relationship_type | smallint (`Citizenship`=0, `BirthPlace`=1, `Residence`=2) | |
| source | smallint (`Manual`=0, `Wikidata`=1, `OpenLibrary`=2, `GoogleBooks`=3) | |
| confidence | numeric(3,2) | 0–1 |
| is_verified | boolean | |
| updated_by_user_id | uuid null | auditoria de correção manual |
| created_at / updated_at | timestamptz | |

**PK (author_id, country_id, relationship_type)**; índice em (country_id, relationship_type).

### `book_origin_country`
book_id FK (cascade) · country_id FK · source · confidence · is_verified · updated_by_user_id · created_at/updated_at. **PK (book_id, country_id)**; índice em country_id.

### `user_book`
| Coluna | Tipo | Regras |
|---|---|---|
| id | uuid PK | |
| user_id | uuid FK (cascade) | |
| book_id | uuid FK (restrict) | |
| status | smallint (`WantToRead`=0, `Reading`=1, `Read`=2, `Abandoned`=3) | |
| rating | smallint null | CHECK 1–5 |
| review | text null | |
| start_date / finished_date | date null | CHECK start ≤ finished |
| created_at / updated_at | timestamptz | |

**Único (user_id, book_id)**; índice (user_id, status); índice (user_id, finished_date).

### `book_cover`
book_id uuid **PK**, FK→book (cascade) · content_type varchar(20) (`image/jpeg|png|webp`) · data bytea · size_bytes int (≤ 2 097 152, CHECK) · sha256 char(64) (usado no `?v=` e no ETag) · updated_at timestamptz · updated_by_user_id uuid null. Capas enviadas ficam no PostgreSQL (sem volume extra no Docker). `book.cover_url` guarda o caminho interno `/api/books/{id}/cover?v={sha256[..12]}` quando há capa enviada.

### `external_book_identifier`
id · book_id FK (cascade) · provider smallint (`GoogleBooks`, `OpenLibraryWork`, `OpenLibraryEdition`, `Wikidata`) · external_id varchar(100). **Único (provider, external_id)**; índice book_id.

### `external_author_identifier`
id · author_id FK (cascade) · provider (`OpenLibrary`, `Wikidata`, `Viaf`) · external_id. **Único (provider, external_id)**.

### `map_share`
id uuid · user_id FK (cascade) · map_type smallint (`Authors`, `Origins`) · token varchar(43) único (32 bytes base64url) · created_at · revoked_at null.

### `external_lookup_cache`
key varchar(300) PK (ex.: `googlebooks:search:title:dom casmurro`) · payload jsonb · fetched_at · expires_at. Usado pelo cache das integrações (TTL: busca 24 h, entidades Wikidata 30 dias). Alternativa: `IDistributedCache` sobre Postgres; tabela própria é mais simples.

## Ajustes aplicados na implementação (Etapa 2)

- `user.email` é gravado em minúsculas com índice único simples (no lugar do índice em `lower(email)`).
- `author.normalized_name` (sem acentos, minúsculo) com índice, no lugar do índice em `lower(name)`.
- `country.id` = código **numérico** ISO 3166-1 (estável); Kosovo `XKX` usa id 900.
- Índice adicional `map_share(user_id, map_type)`.
- `review` é `text`; o limite de 10.000 caracteres é validado no domínio.
- Seed: 250 países e 19 mapeamentos históricos (QIDs conferidos via SPARQL no Wikidata).
- Migration inicial: `InitialCreate` em `backend/src/AtlasLiterario.Infrastructure/Persistence/Migrations`.

## Deduplicação

- Livro: ISBN-13 → `external_book_identifier` (GoogleBooks/OpenLibraryWork) → não cria automático por título.
- Autor: `external_author_identifier` (Wikidata QID, OpenLibrary) → se não houver id externo, compara nome normalizado (sem acentos, minúsculo) **somente** junto com co-ocorrência na mesma obra; caso contrário cria novo autor.

## Exclusões

- Remover um `user_book` não remove o `book` do catálogo.
- `book`/`author` não são excluídos pelos usuários no MVP.
