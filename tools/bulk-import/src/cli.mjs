#!/usr/bin/env node
// Importação em lote para o Atlas Literário (ferramenta externa de debug).
import fs from 'node:fs';
import path from 'node:path';
import readline from 'node:readline';
import { parseArgs } from 'node:util';
import { AtlasClient } from './api.mjs';
import { processRow } from './importer.mjs';
import { OUTCOME_LABEL, PENDING, summarize, writeReports } from './report.mjs';
import { readSheet } from './sheet.mjs';
import { normalize } from './text.mjs';

const HELP = `Uso: node src/cli.mjs <planilha.xlsx|.csv> [opções]

Lê Título/Autor/Editora (e opcionalmente ISBN, Status, Nota, Término), procura cada livro
pela API do Atlas, importa o que achar com segurança e gera relatorio.xlsx + pendentes.xlsx.

Opções
  --api <url>          URL da API (padrão: $ATLAS_API_URL ou http://localhost:5080)
  --email <e-mail>     Conta do Atlas (ou $ATLAS_EMAIL)
  --password <senha>   Senha (ou $ATLAS_PASSWORD; se faltar, é perguntada sem eco)
  --status <status>    Status dos livros sem coluna Status: WantToRead (padrão), Reading, Read, Abandoned
  --dry-run            Só procura e mostra o que seria importado; não grava nada
  --accept <0-1>       Score mínimo para importar sozinho (padrão 0.85)
  --review <0-1>       Score mínimo para aparecer como sugestão nos pendentes (padrão 0.6)
  --delay <ms>         Pausa entre linhas (padrão 300)
  --limit <n>          Processa só as n primeiras linhas (para testar)
  --reenrich           No fim, tenta de novo os dados geográficos dos importados que ficaram incompletos
  --fresh              Ignora o progresso salvo e refaz tudo
  --out <pasta>        Pasta dos resultados (padrão: <planilha>.atlas-import/)
  -h, --help           Esta ajuda`;

const STATUSES = ['WantToRead', 'Reading', 'Read', 'Abandoned'];

function fail(message, code = 1) {
  console.error(`\nErro: ${message}`);
  process.exit(code);
}

function askHidden(prompt) {
  return new Promise((resolve) => {
    const rl = readline.createInterface({ input: process.stdin, output: process.stdout, terminal: true });
    rl._writeToOutput = (s) => { if (s.includes(prompt)) rl.output.write(s); };
    rl.question(prompt, (answer) => { rl.close(); process.stdout.write('\n'); resolve(answer); });
  });
}

const rowKey = (input) => `${input.row}|${normalize(input.title)}|${normalize(input.author)}|${input.isbn ?? ''}`;
const FINAL = new Set(['imported', 'already']);

function loadProgress(file) {
  const map = new Map();
  if (!fs.existsSync(file)) return map;
  for (const line of fs.readFileSync(file, 'utf8').split('\n')) {
    if (!line.trim()) continue;
    try { const rec = JSON.parse(line); map.set(rec.key, rec); } catch { /* linha truncada por Ctrl+C */ }
  }
  return map;
}

async function main() {
  const { values, positionals } = parseArgs({
    allowPositionals: true,
    options: {
      api: { type: 'string' }, email: { type: 'string' }, password: { type: 'string' }, status: { type: 'string' },
      'dry-run': { type: 'boolean' }, accept: { type: 'string' }, review: { type: 'string' }, delay: { type: 'string' },
      limit: { type: 'string' }, reenrich: { type: 'boolean' }, fresh: { type: 'boolean' }, out: { type: 'string' },
      help: { type: 'boolean', short: 'h' },
    },
  });
  if (values.help || positionals.length === 0) { console.log(HELP); return; }

  const input = path.resolve(positionals[0]);
  if (!fs.existsSync(input)) fail(`arquivo não encontrado: ${input}`);
  const status = values.status ?? 'WantToRead';
  if (!STATUSES.includes(status)) fail(`--status deve ser um de ${STATUSES.join(', ')}.`);
  const matchOptions = {};
  if (values.accept) matchOptions.acceptScore = Number(values.accept);
  if (values.review) matchOptions.reviewScore = Number(values.review);
  const delay = Number(values.delay ?? 300);
  const dryRun = Boolean(values['dry-run']);

  const { rows: allRows, warnings } = await readSheet(input).catch((e) => fail(e.message));
  warnings.forEach((w) => console.warn(`Aviso: ${w}`));
  const rows = values.limit ? allRows.slice(0, Number(values.limit)) : allRows;
  if (rows.length === 0) fail('a planilha não tem nenhuma linha com título ou ISBN.');

  const api = values.api ?? process.env.ATLAS_API_URL ?? 'http://localhost:5080';
  const email = values.email ?? process.env.ATLAS_EMAIL;
  if (!email) fail('informe o e-mail da conta (--email ou ATLAS_EMAIL).');
  const password = values.password ?? process.env.ATLAS_PASSWORD ?? (process.stdin.isTTY ? await askHidden('Senha: ') : null);
  if (!password) fail('informe a senha (ATLAS_PASSWORD ou --password).');

  const client = new AtlasClient({
    baseUrl: api, email, password,
    onRetry: ({ attempt, wait, reason }) => console.log(`    … nova tentativa ${attempt} em ${Math.round(wait / 1000)}s (${reason})`),
  });
  let user;
  try { user = await client.login(); } catch (e) { fail(`não consegui entrar em ${api}: ${e.message}`, 2); }

  const outDir = path.resolve(values.out ?? `${input.replace(/\.[^.]+$/, '')}.atlas-import`);
  fs.mkdirSync(outDir, { recursive: true });
  const progressFile = path.join(outDir, 'progresso.jsonl');
  if (values.fresh && fs.existsSync(progressFile)) fs.rmSync(progressFile);
  const progress = loadProgress(progressFile);

  console.log(`\n${dryRun ? 'SIMULAÇÃO' : 'Importando'} ${rows.length} linha(s) para ${user.name} <${user.email}> em ${api}`);
  console.log(`Status padrão: ${status}. Resultados em ${outDir}\n`);

  let stop = false;
  process.on('SIGINT', () => { if (stop) process.exit(130); stop = true; console.log('\nInterrompendo após a linha atual… (Ctrl+C de novo para sair já)'); });

  const records = [];
  let consecutiveErrors = 0;
  for (let i = 0; i < rows.length && !stop; i++) {
    const row = rows[i];
    const key = rowKey(row);
    const label = `[${i + 1}/${rows.length}] ${row.title || `ISBN ${row.isbn}`}${row.author ? ` — ${row.author}` : ''}`;
    const saved = progress.get(key);
    if (saved && FINAL.has(saved.outcome) && !dryRun) {
      records.push(saved);
      console.log(`${label}\n    ↷ já resolvido antes (${OUTCOME_LABEL[saved.outcome]})`);
      continue;
    }
    const rec = { key, ...(await processRow(client, row, { status, dryRun, ...matchOptions })), at: new Date().toISOString() };
    records.push(rec);
    fs.appendFileSync(progressFile, JSON.stringify(rec) + '\n');
    console.log(`${label}\n    ${icon(rec.outcome)} ${OUTCOME_LABEL[rec.outcome]}${detail(rec)}`);
    consecutiveErrors = rec.outcome === 'error' ? consecutiveErrors + 1 : 0;
    if (consecutiveErrors >= 5) {
      console.log('\nCinco erros seguidos: a API ou as fontes parecem indisponíveis. Parando; rode de novo mais tarde para continuar de onde parou.');
      break;
    }
    if (delay > 0) await new Promise((r) => setTimeout(r, delay));
  }

  if (values.reenrich && !dryRun) await reenrich(client, records, progressFile);

  const meta = { input, api, user: `${user.name} <${user.email}>`, dryRun, status, generatedAt: new Date().toISOString() };
  const { pending } = await writeReports(outDir, records, meta);
  const { counts } = summarize(records);
  console.log('\n──────── Resumo ────────');
  for (const [k, label] of Object.entries(OUTCOME_LABEL)) if (counts[k]) console.log(`${label}: ${counts[k]}`);
  if (records.length < rows.length) console.log(`Não processadas (interrompido): ${rows.length - records.length}`);
  console.log(`\nRelatório: ${path.join(outDir, 'relatorio.xlsx')}`);
  if (pending > 0) console.log(`Pendentes (${pending}): ${path.join(outDir, 'pendentes.xlsx')} — corrija e rode de novo com esse arquivo.`);
}

function icon(outcome) {
  return { imported: '✔', already: '=', dryrun: '≈', notfound: '✘', ambiguous: '?', lowconf: '?', error: '!' }[outcome] ?? '·';
}

function detail(rec) {
  if (rec.matched && ['imported', 'already', 'dryrun'].includes(rec.outcome)) {
    const geo = rec.geoStatus ? ` · geo ${rec.geoStatus}${rec.originCountries?.length ? ` · origem ${rec.originCountries.join('/')}` : ''}${rec.authorCountries?.length ? ` · autor ${rec.authorCountries.join('/')}` : ''}` : '';
    return ` → "${rec.matched.title}" (${rec.matched.authors.join(', ')}; ${rec.matched.provider}; score ${rec.score.toFixed(2)})${geo}`;
  }
  return rec.reason ? ` — ${rec.reason}` : '';
}

async function reenrich(client, records, progressFile) {
  const todo = records.filter((r) => r.outcome === 'imported' && r.bookId && r.geoStatus && r.geoStatus !== 'Completed');
  if (todo.length === 0) return;
  console.log(`\nRe-tentando dados geográficos de ${todo.length} livro(s)…`);
  for (const rec of todo) {
    try {
      const book = await client.enrich(rec.bookId);
      rec.geoStatus = book.geoEnrichmentStatus;
      rec.authorCountries = [...new Set(book.authors.flatMap((a) => a.nationalities.map((n) => n.countryIso)))];
      rec.originCountries = book.originCountries.map((c) => c.countryIso);
      fs.appendFileSync(progressFile, JSON.stringify(rec) + '\n');
      console.log(`  ${rec.matched.title}: geo ${rec.geoStatus}`);
    } catch (e) {
      console.log(`  ${rec.matched.title}: não foi possível agora (${e.message})`);
    }
    await new Promise((r) => setTimeout(r, 1500));
  }
}

main().catch((e) => fail(e.stack ?? String(e), 1));
