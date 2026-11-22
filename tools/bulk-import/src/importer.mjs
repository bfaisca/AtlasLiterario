// Orquestra uma linha: buscar -> decidir -> (importar). Sem I/O além do cliente da API.
import { ApiError } from './api.mjs';
import { decide, splitAuthors } from './match.mjs';

function summarizeCandidate(cand) {
  return cand && {
    title: cand.title, authors: cand.authors ?? [], publisher: cand.publisher ?? null, year: cand.publicationYear ?? null,
    isbn13: cand.isbn13 ?? null, provider: cand.source?.provider ?? null, resultKey: cand.resultKey,
  };
}
const summarizeAlt = (s) => ({ ...summarizeCandidate(s.cand), score: s.score.total });

export function geoOf(book) {
  const authorCountries = [...new Set((book?.authors ?? []).flatMap((a) => (a.nationalities ?? []).map((n) => n.countryIso)))];
  const originCountries = (book?.originCountries ?? []).map((c) => c.countryIso);
  return { geoStatus: book?.geoEnrichmentStatus ?? null, authorCountries, originCountries };
}

/** Busca em até três etapas e para na primeira que dá um acerto seguro. */
export async function findBook(client, row, options) {
  const found = new Map();
  const errors = [];
  const collect = (list) => list.forEach((c) => found.set(c.resultKey, c));
  const attempt = async (q, type) => {
    try { collect(await client.search(q, type)); } catch (err) { errors.push(err.message); }
  };
  const firstAuthor = splitAuthors(row.author)[0];
  const steps = [];
  if (row.isbn) steps.push([row.isbn, 'isbn']);
  if (row.title) {
    steps.push([firstAuthor ? `${row.title} ${firstAuthor}` : row.title, 'all']);
    if (firstAuthor) steps.push([row.title, 'title']);
  }

  let decision = decide(row, []);
  for (const [q, type] of steps) {
    await attempt(q, type);
    decision = decide(row, [...found.values()], options);
    if (decision.outcome === 'match') break;
  }
  // Dado ausente nunca vira "não existe": falha de fonte é erro, para tentar de novo depois.
  if (decision.outcome === 'notfound' && errors.length > 0) {
    return { ...decision, outcome: 'error', reason: `Fonte indisponível: ${errors[0]}` };
  }
  return decision;
}

function fill(base, decision) {
  const alternatives = (decision.alternatives ?? []).map(summarizeAlt);
  return { ...base, reason: decision.reason, alternatives };
}

export async function processRow(client, row, { status, dryRun, ...matchOptions }) {
  const base = { input: row, outcome: 'error', reason: '', score: null, matched: null, alternatives: [] };
  let decision;
  try {
    decision = await findBook(client, row, matchOptions);
  } catch (err) {
    return { ...base, reason: err.message };
  }
  if (decision.outcome !== 'match') return { ...fill(base, decision), outcome: decision.outcome };

  const { cand, score } = decision.pick;
  const matched = summarizeCandidate(cand);
  const common = { ...fill(base, decision), reason: '', score: score.total, matched, bookId: cand.bookId ?? null };
  if (cand.inLibrary) return { ...common, outcome: 'already', libraryItemId: cand.libraryItemId };
  if (dryRun) return { ...common, outcome: 'dryrun' };

  const finalStatus = row.status ?? status;
  const payload = {
    ...(cand.bookId ? { bookId: cand.bookId } : { source: cand.source }),
    status: finalStatus,
    rating: row.rating ?? null,
    finishedDate: finalStatus === 'Read' ? row.finishedDate ?? null : null,
  };
  try {
    const { item } = await client.addToLibrary(payload);
    return { ...common, outcome: 'imported', libraryItemId: item.id, bookId: item.book.id, ...geoOf(item.book) };
  } catch (err) {
    if (err instanceof ApiError && err.code === 'library.duplicate') {
      const id = err.body?.libraryItemId ?? null;
      // 409 depois de uma repetição por timeout = o primeiro envio chegou a gravar.
      if (err.attempts > 1 && id) {
        const item = await client.getLibraryItem(id).catch(() => null);
        return { ...common, outcome: 'imported', libraryItemId: id, bookId: item?.book?.id ?? common.bookId, ...geoOf(item?.book), reason: 'Confirmado após nova tentativa.' };
      }
      return { ...common, outcome: 'already', libraryItemId: id };
    }
    return { ...common, outcome: 'error', reason: err.message };
  }
}
