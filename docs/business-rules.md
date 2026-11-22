# Regras de negócio — Atlas Literário

## 1. Biblioteca

- O **catálogo bibliográfico** (`Book`, `Author`, países) é global e compartilhado; a **experiência de leitura** (`UserBook`) é individual.
- Um usuário não pode ter o mesmo `Book` duas vezes na biblioteca (único `UserId + BookId`; 409 em duplicidade).
- Status: `WantToRead`, `Reading`, `Read`, `Abandoned`.
- `Rating`: inteiro 1–5, opcional. `Review`: texto opcional (≤ 10.000 caracteres).
- `FinishedDate` só faz sentido com status `Read`. Ao mudar para `Read` sem `FinishedDate`, o backend preenche com a data atual (UTC). Ao sair de `Read`, `FinishedDate` é limpa (evita leituras "concluídas" fantasmas na evolução histórica).
- `StartDate ≤ FinishedDate` quando ambas existirem (400 caso contrário).
- Somente o dono acessa/edita seu `UserBook` (404 para recursos de outros usuários — não revelar existência).

## 2. Geografia

- Países identificados por **ISO 3166-1 alpha-3** (`Country.IsoCode`). Nunca relacionar por nome.
- Tipos de relação autor→país (`AuthorCountryRelationship`): `Citizenship` (usada no mapa), `BirthPlace`, `Residence` (informativas, **não** entram no mapa).
- Relação obra→país (`BookOriginCountry`): origem literária da obra. Não usar local da narrativa, país de publicação nem nacionalidade do autor como substituto automático.
- Procedência obrigatória em cada relação: `Source` (`Wikidata`, `OpenLibrary`, `GoogleBooks`, `Manual`), `Confidence` (0–1), `IsVerified`.
- **Dados ausentes ≠ dados negativos**: autor sem cidadania conhecida e obra sem origem conhecida aparecem em `unknown` nos mapas/estatísticas e nunca reduzem contagens nem marcam países como não explorados.
- **Entidades históricas** (ex.: Áustria-Hungria, URSS, Iugoslávia, Império Otomano): o Wikidata frequentemente retorna Estados que não existem mais. Mapeamento explícito em tabela `HistoricalCountryMapping` (QID histórico → um ou mais ISO atuais), com `Confidence` reduzida (0.6) e `IsVerified=false`. Ex.: Kafka (P27 = Áustria-Hungria, Tchecoslováquia) → AUT/CZE conforme o mapeamento configurado; o usuário pode corrigir manualmente. O mapeamento é consultado quando o país do Wikidata **não tem P298** ou quando o P298 **não existe na tabela `country`**: o Wikidata usa códigos ISO 3166-3 de Estados extintos (SUN, CSK, DDR, YUG).
- Correção manual: cria/atualiza relações com `Source = Manual`, `Confidence = 1`, `IsVerified = true`. Relações manuais não são sobrescritas por reprocessamentos automáticos.
- **Papéis:** `User` (padrão) e `Moderator`. O papel fica em `user.role`; a checagem de permissão lê o papel **no banco** (não só no token), para que um rebaixamento valha na hora. Não há tela de administração no MVP: um moderador é promovido por script/SQL documentado no README (não por e-mail configurado, já que o e-mail não é verificado e isso permitiria sequestrar o papel).
- **Quem pode alterar países (origem da obra e cidadania dos autores):**
  - **Primeira informação** ("a pessoa que cadastra tem a confiança de inserir a primeira vez"): qualquer usuário que tenha o livro na biblioteca pode **preencher** um conjunto que esteja **vazio/desconhecido** (livro manual recém-criado ou dado que as fontes não encontraram).
  - **Alterar ou remover** países que já constam (de qualquer fonte: Manual, Wikidata, Open Library): **somente `Moderator`** (`403 geo.moderator_required`). Moderador pode editar qualquer livro/autor, mesmo sem tê-lo na biblioteca.
  - A mesma regra vale para `PUT /books/{id}` quando `originCountryIsos` é enviado e para `POST /books/{id}/enrich` não muda dados Manual.
  - Relações gravadas pelo usuário/moderador têm `Source = Manual`, `Confidence = 1`, `IsVerified = true`, com `UpdatedByUserId` para auditoria.
- Pendência pós-MVP: fluxo de "sugerir correção" para usuários comuns e fila de revisão para moderadores; tela para promover/rebaixar moderadores.

## 3. Contagens dos mapas (sem duplicidade)

Conjunto base: `R = { UserBook do usuário com Status = Read }`.

### Mapa de nacionalidades dos autores (`authors`)
- Para cada livro `b ∈ R`, `C_a(b) = ⋃ países de cidadania de todos os autores de b` (conjunto — sem repetição).
- `bookCount(país) = |{ b ∈ R : país ∈ C_a(b) }|` → um livro conta **uma vez por país**, mesmo com dois autores do mesmo país.
- `authorCount(país) = |{ autores distintos de livros em R com cidadania no país }|`.
- Autor com duas cidadanias: o livro conta em ambos os países.
- `unknownBookCount` = livros em `R` sem nenhum autor com cidadania conhecida.

### Mapa de origem das obras (`origins`)
- `C_o(b)` = países de origem de b. `bookCount(país) = |{ b ∈ R : país ∈ C_o(b) }|`.
- `authorCount` = autores distintos dos livros desse país (informativo).
- `unknownBookCount` = livros em `R` sem origem conhecida.

### Indicadores
- `exploredCountries` (por mapa) = número de países com `bookCount ≥ 1` — cada país conta uma vez.
- `distinctCountriesBothMaps` = `|países(authors) ∪ países(origins)|`.
- `continentsExplored` = continentes distintos dos países em `distinctCountriesBothMaps` (por mapa também disponível).
- A **soma** de `bookCount` entre países pode exceder o total de livros lidos (livros multinacionais) — o total de livros lidos é sempre `|R|`, nunca a soma.
- Evolução histórica: para cada mês, países cumulativamente explorados considerando livros com `FinishedDate ≤ fim do mês`. Livros `Read` sem `FinishedDate` (não deve ocorrer, mas por segurança) entram no mês de `UpdatedAt`.

## 4. Escala de cores (frontend)

- `intensity = log(1 + n) / log(1 + max)` (0–1), onde `max` é o maior `bookCount` do mapa atual. País com `n = 0` usa a cor "inexplorado".
- Interpolação contínua entre 5 paradas (quase preto → azul muito claro), definidas em `docs/api-contracts.md` §9.
- A normalização logarítmica evita que um país com muitos livros "apague" os demais.

## 5. Compartilhamento e privacidade

- Perfil possui `MapVisibility`: `Private` (padrão) ou `LinkOnly`.
- Link público exige `LinkOnly` e token ativo; expõe apenas: nome de exibição, tipo de mapa, países e contagens, total de livros lidos e de países. Nunca títulos, resenhas, notas ou datas.
- A geração da imagem (local, no navegador) não depende de link público.
