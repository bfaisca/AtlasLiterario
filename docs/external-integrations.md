# Integrações externas — Atlas Literário

Todas as consultas são feitas pelo backend (`AtlasLiterario.Infrastructure/ExternalSources`), via `HttpClient` tipado (`IHttpClientFactory`) com:
- timeout de 5 s por requisição, 1 retry com backoff para 5xx/timeout (Microsoft.Extensions.Http.Resilience);
- `User-Agent: AtlasLiterario/0.1 (contato configurável)` — exigido pela política do Wikidata e recomendado pela Open Library;
- cache em `external_lookup_cache` (busca: 24 h; entidades Wikidata/Open Library: 30 dias);
- falha de uma fonte **não** derruba a busca: o resultado é parcial e registrado em log.

## Interfaces (Application)

```csharp
public interface IBookSearchProvider        // Google Books, Open Library
{
    ExternalProvider Provider { get; }
    Task<IReadOnlyList<ExternalBookCandidate>> SearchAsync(BookSearchQuery query, CancellationToken ct);
    Task<ExternalBookCandidate?> GetAsync(string externalId, CancellationToken ct);
}
public interface IAuthorIdentityResolver   // Open Library → QIDs do Wikidata
{
    Task<IReadOnlyList<ResolvedAuthor>> ResolveAuthorsAsync(ExternalBookCandidate book, CancellationToken ct);
}
public interface IGeoResolver              // Wikidata
{
    Task<IReadOnlyList<ResolvedCountry>> GetAuthorCitizenshipsAsync(string authorQid, CancellationToken ct);
    Task<IReadOnlyList<ResolvedCountry>> GetWorkOriginsAsync(string workQid, CancellationToken ct);
    Task<string?> FindWorkQidAsync(string? isbn13, string? openLibraryWorkId, string title, string? authorQid, CancellationToken ct);
}
```

## 1. Google Books (identificação/bibliografia)

- `GET https://www.googleapis.com/books/v1/volumes?q={termo}&maxResults=20&printType=books` com `intitle:`, `inauthor:`, `isbn:` conforme `type`. Chave opcional `GoogleBooks:ApiKey` (sem chave há limite menor).
- Campos: `volumeInfo.title/subtitle/authors/publisher/publishedDate/description/industryIdentifiers/imageLinks.thumbnail/language/categories`.
- Capa: forçar `https`, remover `&edge=curl`. Idioma de Google Books é o da **edição**, não o original — não preencher `OriginalLanguage` com ele.

## 2. Open Library (complemento, obras, autores)

- Busca: `GET https://openlibrary.org/search.json?q=&title=&author=&isbn=&fields=key,title,subtitle,author_name,author_key,first_publish_year,isbn,publisher,cover_i,language&limit=20`.
- Edição por ISBN: `GET /isbn/{isbn}.json` → `works[0].key`.
- Obra: `GET /works/{OLxxxW}.json` (descrição, autores).
- Autor: `GET /authors/{OLxxxA}.json` → `remote_ids.wikidata` (QID), `remote_ids.viaf`, `birth_date`.
- Capa: `https://covers.openlibrary.org/b/id/{cover_i}-L.jpg`.
- `first_publish_year` é preferido ao ano da edição para `PublicationYear` quando a obra é identificada.

## 3. Wikidata (geografia)

- Entidade: `GET https://www.wikidata.org/wiki/Special:EntityData/{QID}.json`.
- SPARQL (busca de obra): `https://query.wikidata.org/sparql?format=json&query=...`.
- Propriedades:
  - **P27** country of citizenship → `Citizenship` (usada no mapa de autores).
  - P19 place of birth → `BirthPlace` (informativa; resolver o país via P17 do local).
  - **P495** country of origin (obra) → origem literária.
  - P648 Open Library ID, P212 ISBN-13, P957 ISBN-10, P50 author, P407/P364 language of work (para `OriginalLanguage`).
  - País → ISO: **P298** (alpha-3). Sem P298, ou com P298 ausente da tabela `country` (códigos ISO 3166-3 de Estados extintos: SUN, CSK, DDR, YUG) → consultar `historical_country_mapping`; se não houver mapeamento, descartar e registrar em log (nunca inventar).
  - Particularidades conferidas no seed: o P298 `NLD` está em Q29999 (Reino dos Países Baixos), não em Q55 (mapeado via tabela histórica); Kosovo usa `XKS` no Wikidata e `XKX` no Atlas.
- Qualificadores de P27 com `end time` (P582) **não** são descartados — cidadania histórica continua válida para a obra.
- Ranks: ignorar `deprecated`; se houver `preferred`, usar apenas os preferred.
- Encontrar a obra (QID): 1) P648 = id da obra Open Library; 2) P212 do ISBN → P629 (edition of) → obra; 3) rótulo exato + P50 = autor QID. Se nada for encontrado → origem desconhecida.

## 4. Consolidação e prioridade

| Campo | Prioridade |
|---|---|
| Título, subtítulo, editora, sinopse, capa | Manual > Google Books > Open Library |
| ISBN-13/10 | Google Books ∪ Open Library (validação de dígito verificador) |
| Ano de publicação | Manual > Open Library `first_publish_year` > Google Books |
| Autores (identidade) | Open Library (author key + QID) > nomes do Google Books |
| Nacionalidade do autor | Manual > Wikidata P27 (confidence 0.9; 0.6 se via mapeamento histórico) |
| Origem da obra | Manual > Wikidata P495 (0.9) |
| Idioma original | Manual > Wikidata P407 da obra |

Deduplicação de resultados de busca: mesma ISBN-13 ou mesma combinação título normalizado + primeiro autor normalizado → um único `BookSearchResultDto` (preferindo o que já existe no catálogo local).

## 5. Uso, limites e licenças

- Google Books: respeitar cota diária; não armazenar conteúdo além de metadados.
- Open Library: sem chave; evitar rajadas (cache + no máx. ~1 req/s por processo em lote).
- Wikidata: dados CC0; enviar User-Agent identificável; SPARQL com timeout 10 s.
- Capas: armazenamos apenas a **URL**, não a imagem (evita redistribuição). Atribuição "Dados: Google Books, Open Library, Wikidata" no rodapé do app.
- Natural Earth (GeoJSON): domínio público.

## 6. Decisões e comportamento verificado (Etapa 3)

- Google Books: chave enviada só no header `x-goog-api-key` (nunca na URL; o log de `System.Net.Http.HttpClient` fica em Warning). `GoogleBooks:Country=BR` é obrigatório: sem ele a API respondeu 403 "Cannot determine user location". Sem chave, a busca degrada para Open Library + catálogo local.
- Open Library devolve marcadores de ordenação C1 (U+0098/U+009C) em títulos; são removidos na leitura (`CleanText`).
- Wikidata é consultado via SPARQL. Resolução do país: QID na tabela de países → P298 presente na tabela → mapeamento histórico (confiança 0,6, não verificado). Relação direta: confiança 0,9.
- Identidade de autor: chave Open Library → `remote_ids` (QID Wikidata, VIAF). Sem chave, busca por nome em `search/authors.json` aceita só com correspondência exata, ≥ 20 obras e 5× mais obras que o segundo colocado; nome de uma palavra só nunca é adivinhado.
- Limitações conhecidas (resolvidas por correção manual ou `POST /books/{id}/enrich`): autores sem QID no Open Library (ex.: Orwell) ficam com nacionalidade desconhecida; importações do Google sem ISBN (ex.: uma edição de Dom Casmurro) ficam com origem desconhecida; o Wikidata informa ARG como origem de *Cem anos de solidão* (dado da fonte, não corrigido). O Wikidata teve 502 intermitentes e importações de até 13 s (orçamento de 25 s); o enriquecimento é síncrono no MVP.
