import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ApiError, AtlasClient } from '../src/api.mjs';
import { processRow } from '../src/importer.mjs';

const result = (o) => ({ resultKey: `${o.title}`, bookId: null, inLibrary: false, libraryItemId: null, subtitle: null, publisher: null, isbn13: null, source: { provider: 'GoogleBooks', externalId: 'g1' }, ...o });
const row = { row: 2, title: 'Dom Casmurro', author: 'Machado de Assis', publisher: '', isbn: null, status: null, rating: null, finishedDate: null };
const book = { id: 'b1', geoEnrichmentStatus: 'Completed', authors: [{ nationalities: [{ countryIso: 'BRA' }] }], originCountries: [{ countryIso: 'BRA' }] };

function fakeClient({ search = () => [], add = () => ({ item: { id: 'i1', book }, attempts: 1 }), getItem } = {}) {
  const calls = { search: [], add: [] };
  return {
    calls,
    search: async (q, type) => { calls.search.push([q, type]); return search(q, type); },
    addToLibrary: async (p) => { calls.add.push(p); return add(p); },
    getLibraryItem: getItem ?? (async () => ({ id: 'i1', book })),
  };
}

test('importa o acerto e devolve os países; usa source quando não é local', async () => {
  const c = fakeClient({ search: () => [result({ title: 'Dom Casmurro', authors: ['Machado de Assis'] })] });
  const r = await processRow(c, row, { status: 'WantToRead' });
  assert.equal(r.outcome, 'imported');
  assert.deepEqual(r.originCountries, ['BRA']);
  assert.deepEqual(c.calls.add[0], { source: { provider: 'GoogleBooks', externalId: 'g1' }, status: 'WantToRead', rating: null, finishedDate: null });
  assert.equal(c.calls.search.length, 1); // parou na primeira busca
});

test('livro do catálogo local entra por bookId; finishedDate só com status Read', async () => {
  const c = fakeClient({ search: () => [result({ title: 'Dom Casmurro', authors: ['Machado de Assis'], bookId: 'b9', source: { provider: 'Local', externalId: 'b9' } })] });
  await processRow(c, { ...row, status: 'WantToRead', finishedDate: '2024-01-01' }, { status: 'Read' });
  assert.equal(c.calls.add[0].bookId, 'b9');
  assert.equal(c.calls.add[0].finishedDate, null);
  await processRow(c, { ...row, status: 'Read', finishedDate: '2024-01-01', rating: 5 }, { status: 'WantToRead' });
  assert.deepEqual([c.calls.add[1].status, c.calls.add[1].finishedDate, c.calls.add[1].rating], ['Read', '2024-01-01', 5]);
});

test('dry-run não grava; já na biblioteca não grava', async () => {
  const c = fakeClient({ search: () => [result({ title: 'Dom Casmurro', authors: ['Machado de Assis'] })] });
  assert.equal((await processRow(c, row, { status: 'Read', dryRun: true })).outcome, 'dryrun');
  const c2 = fakeClient({ search: () => [result({ title: 'Dom Casmurro', authors: ['Machado de Assis'], inLibrary: true, libraryItemId: 'i7' })] });
  const r = await processRow(c2, row, { status: 'Read' });
  assert.equal(r.outcome, 'already');
  assert.equal(c2.calls.add.length, 0);
});

test('busca em etapas: título sozinho é tentado quando a primeira não acha', async () => {
  const c = fakeClient({ search: (q, type) => (type === 'title' ? [result({ title: 'Dom Casmurro', authors: ['Machado de Assis'] })] : []) });
  const r = await processRow(c, row, { status: 'Read' });
  assert.equal(r.outcome, 'imported');
  assert.deepEqual(c.calls.search.map((s) => s[1]), ['all', 'title']);
});

test('falha de fonte nunca vira "não encontrado"', async () => {
  const c = fakeClient({ search: () => { throw new Error('502'); } });
  const r = await processRow(c, row, { status: 'Read' });
  assert.equal(r.outcome, 'error');
});

test('409 duplicado: já existia; 409 após repetição por timeout: conta como importado', async () => {
  const dup = (attempts) => () => { const e = new ApiError(409, { code: 'library.duplicate', libraryItemId: 'i5' }); e.attempts = attempts; throw e; };
  const s = () => [result({ title: 'Dom Casmurro', authors: ['Machado de Assis'] })];
  assert.equal((await processRow(fakeClient({ search: s, add: dup(1) }), row, { status: 'Read' })).outcome, 'already');
  const r = await processRow(fakeClient({ search: s, add: dup(2) }), row, { status: 'Read' });
  assert.equal(r.outcome, 'imported');
  assert.equal(r.libraryItemId, 'i5');
});

test('ambíguo e não encontrado ficam pendentes com alternativas', async () => {
  const c = fakeClient({ search: () => [result({ title: 'Dom Casmurro', authors: ['Machado de Assis'], resultKey: 'a' }), result({ title: 'Dom Casmurro Ilustrado Outro', authors: ['Machado de Assis'], resultKey: 'b' })] });
  const r = await processRow(c, row, { status: 'Read', dryRun: true });
  assert.ok(['dryrun', 'ambiguous'].includes(r.outcome));
  assert.equal((await processRow(fakeClient(), row, { status: 'Read' })).outcome, 'notfound');
});

// ---------- cliente HTTP ----------

function http(responses) {
  const seen = [];
  const impl = async (url, init) => {
    seen.push({ url: String(url), method: init.method, auth: init.headers.Authorization });
    const next = responses.shift();
    if (next instanceof Error) throw next;
    return new Response(next.body === undefined ? '' : JSON.stringify(next.body), { status: next.status, headers: next.headers });
  };
  return { impl, seen };
}
const login = { status: 200, body: { accessToken: 't1', accessTokenExpiresAt: new Date(Date.now() + 3600e3).toISOString(), refreshToken: 'r', user: { name: 'U', email: 'u@x' } } };

test('cliente: repete em 502 e em timeout, e refaz o login em 401', async () => {
  const { impl, seen } = http([login, { status: 502, body: { code: 'external.unavailable' } }, new Error('x'), { status: 200, body: [] }, { status: 401 }, login, { status: 200, body: [1] }]);
  const waits = [];
  const c = new AtlasClient({ baseUrl: 'http://api', email: 'e', password: 'p', fetchImpl: impl, sleep: async (ms) => waits.push(ms) });
  assert.deepEqual(await c.search('a'), []);
  assert.equal(waits.length, 2);
  assert.deepEqual(await c.search('b'), [1]);
  assert.ok(seen.every((s, i) => i === 0 || s.url.includes('/login') || s.auth === 'Bearer t1'));
});

test('cliente: 400 não repete e o erro leva o code', async () => {
  const { impl } = http([login, { status: 400, body: { code: 'validation.failed', title: 'ruim' } }]);
  const c = new AtlasClient({ baseUrl: 'http://api', email: 'e', password: 'p', fetchImpl: impl, sleep: async () => {} });
  await assert.rejects(() => c.search('a'), (e) => e.code === 'validation.failed');
});
