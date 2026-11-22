#!/usr/bin/env node
// Une autores duplicados (mesmo nome normalizado) sem perder vínculos com livros. Requer conta Moderator.
// Padrão: SIMULAÇÃO (só lista o plano). Com --apply executa POST /api/authors/merge, um grupo por vez.
// Uso: node src/authors-dedupe.mjs [--apply] [--exclude "nome normalizado,outro"] [--out log.json] [--api url] [--email e] [--password p]
// O alvo de cada grupo é o primeiro autor devolvido pela API (mais livros; empate: o mais antigo).
// Atenção: homônimos de pessoas diferentes seriam unidos; use --exclude para pular esses grupos.
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
  apply: { type: 'boolean' }, exclude: { type: 'string' }, out: { type: 'string' },
  api: { type: 'string' }, email: { type: 'string' }, password: { type: 'string' },
} });
const apply = values.apply === true;
const exclude = new Set((values.exclude ?? '').split(',').map((s) => s.trim().toLowerCase()).filter(Boolean));
const api = values.api ?? process.env.ATLAS_API_URL ?? 'http://localhost:5080';
const email = values.email ?? process.env.ATLAS_EMAIL ?? await new Promise((r) => { const rl = readline.createInterface({ input: process.stdin, output: process.stdout }); rl.question('E-mail do Atlas (moderador): ', (a) => { rl.close(); r(a); }); });
const password = values.password ?? process.env.ATLAS_PASSWORD ?? await askHidden('Senha: ');
const out = path.resolve(values.out ?? `data/authors-dedupe.${apply ? 'aplicado' : 'simulacao'}.json`);

const client = new AtlasClient({ baseUrl: api, email, password });
await client.login();

const groups = await client.request('GET', '/api/authors/duplicates');
const plan = groups.filter((g) => !exclude.has(g.normalizedName.toLowerCase()));
console.log(`${groups.length} grupo(s) de duplicados; ${plan.length} no plano (${groups.length - plan.length} excluído(s)).\n`);

const log = [];
for (const g of plan) {
  const [target, ...sources] = g.authors;
  const fmt = (a) => `${a.name} [${a.id.slice(0, 8)}] livros=${a.bookCount} países=${a.nationalityIsos.join('/') || '—'}`;
  console.log(`• ${g.normalizedName}\n    alvo:   ${fmt(target)}\n${sources.map((s) => `    une:    ${fmt(s)}`).join('\n')}`);
  const entry = { normalizedName: g.normalizedName, targetId: target.id, sourceIds: sources.map((s) => s.id), result: null, error: null };
  if (apply) {
    try {
      entry.result = await client.request('POST', '/api/authors/merge', { body: { targetId: target.id, sourceIds: entry.sourceIds }, timeoutMs: 60000, retries: 0 });
      console.log(`    ✔ ${JSON.stringify(entry.result)}`);
    } catch (e) {
      entry.error = String(e?.message ?? e);
      console.log(`    ✘ ${entry.error}`);
    }
  }
  log.push(entry);
}

fs.mkdirSync(path.dirname(out), { recursive: true });
fs.writeFileSync(out, JSON.stringify({ at: new Date().toISOString(), apply, log }, null, 2));
console.log(apply ? `\nAplicado. Log em ${out}` : `\nSIMULAÇÃO: nada foi alterado. Revise e rode com --apply. Plano salvo em ${out}`);
