#!/usr/bin/env node
// Aplica correções geográficas revisadas (arquivo gerado por geo-export + proposal preenchido).
// Padrão = simulação. Use --apply para gravar. Só PREENCHE o que está vazio (PUT origin-countries / authors/{id}/countries).
// Uso: node src/geo-apply.mjs data/sem-pais.revisado.json [--apply]
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

const { values, positionals } = parseArgs({ allowPositionals: true, options: {
  apply: { type: 'boolean' }, api: { type: 'string' }, email: { type: 'string' }, password: { type: 'string' },
} });
if (!positionals[0]) { console.error('Uso: node src/geo-apply.mjs <arquivo.json> [--apply]'); process.exit(1); }
const file = path.resolve(positionals[0]);
const data = JSON.parse(fs.readFileSync(file, 'utf8'));
const ISO3 = /^[A-Z]{3}$/;
const bad = (list) => list.filter((i) => !ISO3.test(i));

const plan = [];
const doneAuthors = new Set();
for (const r of data.rows) {
  const p = r.proposal ?? {};
  const origin = (p.originIsos ?? []).map((s) => s.toUpperCase());
  if (origin.length && !r.originCountries.length) plan.push({ kind: 'origin', id: r.bookId, isos: origin, label: r.title });
  for (const a of p.authors ?? []) {
    const isos = (a.citizenshipIsos ?? []).map((s) => s.toUpperCase());
    const cur = r.authors.find((x) => x.authorId === a.authorId);
    if (!cur || !isos.length || cur.citizenships.length || doneAuthors.has(a.authorId)) continue;
    doneAuthors.add(a.authorId);
    plan.push({ kind: 'author', id: a.authorId, isos, label: cur.name });
  }
}
const invalid = plan.filter((x) => bad(x.isos).length);
if (invalid.length) { console.error('ISO inválido (use alpha-3, ex.: BRA):', invalid.map((x) => `${x.label}: ${x.isos}`).join('; ')); process.exit(1); }

console.log(`${plan.length} alteração(ões) planejada(s)${values.apply ? '' : ' (simulação; use --apply para gravar)'}:`);
for (const x of plan) console.log(` - ${x.kind === 'origin' ? 'obra ' : 'autor'} ${x.label} → ${x.isos.join(', ')}`);
if (!values.apply || !plan.length) process.exit(0);

const api = values.api ?? process.env.ATLAS_API_URL ?? 'http://localhost:5080';
const email = values.email ?? process.env.ATLAS_EMAIL ?? await new Promise((r) => { const rl = readline.createInterface({ input: process.stdin, output: process.stdout }); rl.question('E-mail do Atlas: ', (a) => { rl.close(); r(a); }); });
const password = values.password ?? process.env.ATLAS_PASSWORD ?? await askHidden('Senha: ');
const client = new AtlasClient({ baseUrl: api, email, password });
await client.login();

const log = [];
let ok = 0;
for (const x of plan) {
  try {
    if (x.kind === 'origin') await client.request('PUT', `/api/books/${x.id}/origin-countries`, { body: { countryIsos: x.isos }, retries: 1 });
    else await client.request('PUT', `/api/authors/${x.id}/countries`, { body: { citizenshipIsos: x.isos }, retries: 1 });
    ok++; log.push({ ...x, result: 'ok' });
  } catch (e) {
    console.error(`  falhou: ${x.label}: ${e.message}`);
    log.push({ ...x, result: 'erro', error: e.message });
  }
}
fs.writeFileSync(file.replace(/\.json$/, '') + '.aplicado.json', JSON.stringify(log, null, 2));
console.log(`Concluído: ${ok}/${plan.length} gravadas.`);
