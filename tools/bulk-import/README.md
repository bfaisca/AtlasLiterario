# Importação em lote (ferramenta de debug)

Ferramenta **externa ao app**: lê uma lista de livros (planilha ou texto), procura cada um pela API do
Atlas, **importa para a biblioteca o que achar com segurança** e deixa o resto numa planilha de
pendentes. Fala só com a API REST (login, `GET /books/search`, `POST /library`), então usa as mesmas
regras, fontes e enriquecimento geográfico do app. Não altera backend nem frontend.

## Formatos de entrada

| Formato | Como |
|---|---|
| `.txt` | 3 linhas por livro (**título, autor, editora**), livros separados por linha em branco. É o formato de `data/livros.txt`. |
| `.xlsx` | 1ª aba, cabeçalho na primeira linha. Modelo: `data/modelo-livros.xlsx` (`npm run template`). |
| `.csv` | Mesmo cabeçalho do xlsx; separador `,` `;` ou tab; UTF-8 ou Windows-1252. |

Colunas do xlsx/csv (nomes em pt ou en): **Título** (obrigatória), Autor, Editora, e opcionais **ISBN**
(acerto garantido), **Status** (Quero ler / Lendo / Lido / Abandonado), **Nota** (1–5), **Término**
(só vale para "Lido"; sem data, o backend usa hoje). Várias pessoas no autor: separe com `;`, `/`, `&`, ` e ` ou vírgula entre nomes completos.

## Como rodar

### Com Docker (nada para instalar)

Com o Atlas no ar (`docker compose up -d`), conta já criada no app:

```powershell
# PowerShell, na raiz do projeto
$env:ATLAS_EMAIL="voce@email.com"; $env:ATLAS_PASSWORD="sua-senha"
docker compose --profile tools run --rm bulk-import livros.txt --dry-run   # só simula
docker compose --profile tools run --rm bulk-import livros.txt --status Read
```

Coloque a lista em `tools/bulk-import/data/` (já existe `livros.txt`). Os resultados também saem lá.
Se não definir a senha, ela é pedida sem eco (use `docker compose --profile tools run --rm -it ...`).

### Com Node 20+

```bash
cd tools/bulk-import && npm install
ATLAS_EMAIL=voce@email.com node src/cli.mjs data/livros.txt --dry-run   # a senha é perguntada
```

## Opções principais

- `--dry-run` procura e mostra o que seria importado, sem gravar. **Recomendado na primeira vez.**
- `--status Read|Reading|WantToRead|Abandoned` status dos livros sem coluna Status (padrão `WantToRead`,
  que não entra nos mapas; só `Read` conta nos mapas e estatísticas).
- `--reenrich` no fim, tenta de novo os dados geográficos dos importados que ficaram `Partial`/`Failed`
  (ex.: Wikidata limitou as requisições).
- `--limit 20` só as 20 primeiras linhas; `--fresh` ignora o progresso salvo; `--api`, `--email`, `--accept`, `--review`, `--delay`, `--out` (veja `--help`).

## O que sai

Em `<arquivo>.atlas-import/` (ao lado da lista):

- `relatorio.xlsx` — resumo + uma linha por livro: resultado, motivo, score, o que foi encontrado, fonte,
  status geográfico, países do autor e da obra, alternativas.
- `pendentes.xlsx` — só o que não foi importado, **no mesmo formato de entrada** (Título/Autor/Editora/…)
  com o motivo e sugestões. Corrija e rode de novo com esse arquivo.
- `progresso.jsonl` — andamento. **Pode interromper com Ctrl+C e rodar o mesmo comando: continua de onde parou**
  (o que já foi importado não é refeito; pendentes são tentados de novo).

Resultados possíveis: Importado · Já estava na biblioteca · Não encontrado · Ambíguo (duas obras igualmente
prováveis) · Baixa confiança (parecido, ou só o autor confere — típico de título traduzido) · Erro (fonte
indisponível; tentar de novo — falha de fonte nunca é tratada como "não existe").

## Critério de decisão (`src/match.mjs`)

Score = 60% título + 40% autor (+0,05 se a editora bate; editora nunca penaliza, pois edições variam).
Sem autor na lista, só título praticamente idêntico é aceito. Importa sozinho com score ≥ 0,85 **e** título ≥ 0,75
**e** autor ≥ 0,5. Parênteses no título da lista são ignorados; no resultado eles distinguem obras
(`1984 (adaptation)` ≠ `1984`). Várias edições da mesma obra não são ambiguidade: vence a edição da editora
informada, depois a que já está no catálogo/biblioteca, depois a com ISBN. Em dúvida, vai para pendentes — nunca chuta.

## Observações

- Sequencial de propósito (respeita o limite das fontes). Cada livro novo leva alguns segundos por causa do
  enriquecimento geográfico (orçamento de até ~13 s); 165 livros ≈ 20–40 min. Sem a chave do Google Books os
  títulos em português são achados muito menos; confira `GOOGLE_BOOKS_API_KEY` no `.env`.
- Re-rodar é seguro: o backend recusa duplicatas (`library.duplicate`).
- Dependência única: `exceljs` (ler/gravar `.xlsx`). Testes: `npm test` (31, sem rede nem backend).
- Não é uma funcionalidade do produto; se virar, o caminho natural é um endpoint de importação no backend
  reaproveitando `src/match.mjs` — ver `docs/development-plan.md`.
