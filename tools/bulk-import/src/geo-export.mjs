#!/usr/bin/env node
// Exporta (somente leitura) os livros da biblioteca sem país da obra e/ou do autor.
// Uso: node src/geo-export.mjs [--geo both|origin|author|any|all]  (all = biblioteca inteira, sem filtro) [--out arquivo.json] [--api url] [--email e] [--password p]
import fs from 'node:fs';
import path from 'node:path';
import readline from 'node:readline';
import { parseArgs } from 'node:util';
import { AtlasClient } from './api.mjs';

function askHidden(prompt) {
  return new Promise((resolve) => {
    const rl = readline.createInterface({ input: process.stdin, output: process.stdout, terminal: true });
    rl._writeToOutput = (s) => { if (s.includes(prompt)) rl.output.write(s); };
    rl.question(prompt, (a) => { rl.close(); process.stdout.write('\n'); resolve(a); });
  });
}

const { values } = parseArgs({ options: {
  geo: { type: 'string' }, out: { type: 'string' }, api: { type: 'string' }, email: { type: 'string' }, password: { type: 'string' },
} });
const geo = values.geo ?? 'both';
if (!['both', 'origin', 'author', 'any', 'all'].includes(geo)) { console.error('--geo inválido'); process.exit(1); }
const api = values.api ?? process.env.ATLAS_API_URL ?? 'http://localhost:5080';
const email = values.email ?? process.env.ATLAS_EMAIL ?? await new Promise((r) => { const rl = readline.createInterface({ input: process.stdin, output: process.stdout }); rl.question('E-mail do Atlas: ', (a) => { rl.close(); r(a); }); });
const password = values.password ?? process.env.ATLAS_PASSWORD ?? await askHidden('Senha: ');
const out = path.resolve(values.out ?? 'data/sem-pais.json');

const client = new AtlasClient({ baseUrl: api, email, password });
await client.login();

const items = [];
for (let page = 1; ; page++) {
  const res = await client.request('GET', '/api/library', { query: { ...(geo === 'all' ? {} : { geoMissing: geo }), page, pageSize: 50, sort: 'createdAt', order: 'asc' } });
  items.push(...res.items);
  if (page >= res.totalPages) break;
}

const rows = items.map((it) => ({
  libraryItemId: it.id,
  status: it.status,
  bookId: it.book.id,
  title: it.book.title,
  subtitle: it.book.subtitle,
  isbn13: it.book.isbn13,
  publisher: it.book.publisher,
  year: it.book.publicationYear,
  originalLanguage: it.book.originalLanguage,
  geoEnrichmentStatus: it.book.geoEnrichmentStatus,
  originCountries: it.book.originCountries.map((c) => c.countryIso),
  authors: it.book.authors.map((a) => ({ authorId: a.id, name: a.name, citizenships: a.nationalities.map((n) => n.countryIso) })),
  externalIds: it.book.externalIds,
  // preencher na revisão (deixe vazio para não alterar):
  proposal: { originIsos: [], authors: [], source: '', confidence: '' },
}));

fs.mkdirSync(path.dirname(out), { recursive: true });
fs.writeFileSync(out, JSON.stringify({ exportedAt: new Date().toISOString(), geo, count: rows.length, rows }, null, 2));
console.log(`${rows.length} livro(s) exportado(s) para ${out} (somente leitura; nada foi alterado).`);
