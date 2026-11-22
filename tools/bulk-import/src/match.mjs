// Critérios explícitos para decidir se um resultado da busca é o livro da planilha.
// Regras (documentadas no README):
//  - título pesa 60%, autor 40%; editora só dá bônus (edições diferem) e nunca penaliza;
//  - sem autor na planilha o score máximo é 0.9 × título, então só título (quase) idêntico é aceito;
//  - duas obras diferentes igualmente boas => ambíguo (vai para pendentes, nunca chuta);
//  - várias edições da MESMA obra não são ambiguidade: escolhe-se a melhor edição.
import { normalize, titleTokens } from './text.mjs';

export const DEFAULTS = { acceptScore: 0.85, reviewScore: 0.6, minTitle: 0.75, minAuthor: 0.5 };

// ---------- título ----------

// Parênteses são ignorados só no título da PLANILHA ("(edição de bolso)"); no candidato eles
// distinguem obras ("1984 (adaptation)" não é "1984").
function titleVariants(title, subtitle, { stripParens = false } = {}) {
  const base = String(title ?? '');
  const variants = new Set([base, base.split(/[:,–—]| - /)[0]]);
  if (stripParens) variants.add(base.replace(/\([^)]*\)|\[[^\]]*\]/g, ' '));
  if (subtitle) variants.add(`${base} ${subtitle}`);
  return [...variants].map(titleTokens).filter((t) => t.length > 0);
}

function tokenScore(a, b) {
  if (a.join(' ') === b.join(' ')) return 1;
  const [small, large] = a.length <= b.length ? [a, b] : [b, a];
  const largeSet = new Set(large);
  if (small.every((t) => largeSet.has(t))) return 0.5 + 0.4 * (small.length / large.length);
  const sa = new Set(a);
  const common = [...new Set(b)].filter((t) => sa.has(t)).length;
  return (2 * common) / (sa.size + new Set(b).size);
}

export function titleScore(wanted, candidateTitle, candidateSubtitle) {
  let best = 0;
  for (const w of titleVariants(wanted, null, { stripParens: true })) {
    for (const c of titleVariants(candidateTitle, candidateSubtitle)) best = Math.max(best, tokenScore(w, c));
  }
  return best;
}

// ---------- autor ----------

/** Lista de autores de uma célula: separadores `;`, `/`, `|`, ` & `, ` e `. */
export function splitAuthors(cell) {
  const parts = String(cell ?? '')
    .replace(/\((org|orgs|ed|trad)\.?\)/gi, ' ')
    .split(/;|\/|\||\s&\s|\se\s/i)
    .map((s) => s.trim())
    .filter(Boolean);
  // "Karl Marx, Friedrich Engels" são dois autores; "Assis, Machado de" é um só (sobrenome primeiro).
  return parts.flatMap((p) => {
    const bits = p.split(',').map((b) => b.trim()).filter(Boolean);
    return bits.length > 1 && bits.every((b) => b.split(/\s+/).length >= 2) ? bits : [p];
  });
}

function nameTokens(name) {
  let text = String(name ?? '');
  const comma = text.match(/^([^,]+),(.+)$/); // "Assis, Machado de" -> "Machado de Assis"
  if (comma) text = `${comma[2]} ${comma[1]}`;
  return normalize(text).split(' ').filter(Boolean);
}

function singleAuthorScore(wanted, candidate) {
  const w = nameTokens(wanted);
  const c = nameTokens(candidate);
  if (w.length === 0 || c.length === 0) return 0;
  if (w.join(' ') === c.join(' ')) return 1;
  let best = 0;
  if (w.at(-1) === c.at(-1)) {
    const rest = w.slice(0, -1);
    const compat = rest.length === 0
      ? 0.5
      : rest.filter((t) => c.some((x) => x === t || (t.length === 1 && x.startsWith(t)))).length / rest.length;
    best = 0.7 + 0.3 * compat;
  }
  const [small, large] = w.length <= c.length ? [w, c] : [c, w];
  if (small.length > 1 && small.every((t) => large.includes(t))) best = Math.max(best, 0.9);
  return best;
}

/** null quando a planilha não informa autor. */
export function authorScore(wantedAuthors, candidateAuthors) {
  if (wantedAuthors.length === 0) return null;
  if (candidateAuthors.length === 0) return 0.3; // desconhecido: não confirma nem descarta
  let best = 0;
  for (const w of wantedAuthors) for (const c of candidateAuthors) best = Math.max(best, singleAuthorScore(w, c));
  return best;
}

function publisherMatches(wanted, candidate) {
  const w = normalize(wanted);
  const c = normalize(candidate);
  return w.length > 0 && c.length > 0 && (c.includes(w) || w.includes(c));
}

// ---------- pontuação e decisão ----------

export function scoreCandidate(row, cand, options = {}) {
  const opts = { ...DEFAULTS, ...options };
  const wantedAuthors = splitAuthors(row.author);
  if (row.isbn && (cand.isbn13 === row.isbn)) {
    return { total: 1, title: 1, author: 1, publisher: publisherMatches(row.publisher, cand.publisher), byIsbn: true, eligible: true };
  }
  const title = titleScore(row.title, cand.title, cand.subtitle);
  const author = authorScore(wantedAuthors, cand.authors ?? []);
  const publisher = publisherMatches(row.publisher, cand.publisher);
  const base = author === null ? title * 0.9 : 0.6 * title + 0.4 * author;
  const total = Math.min(1, base + (publisher ? 0.05 : 0));
  const eligible = title >= opts.minTitle && (author === null || author >= opts.minAuthor);
  return { total, title, author, publisher, byIsbn: false, eligible };
}

// Edições da mesma obra variam em subtítulo, vírgula ("Vidas sêcas, romance") e grafia do autor;
// por isso a chave é só o título curto. O autor já foi conferido contra a planilha em scoreCandidate.
function workKey(cand) {
  return titleTokens(String(cand.title ?? '').split(/[:,–—]| - /)[0]).join(' ');
}

const PROVIDER_RANK = { Local: 0, GoogleBooks: 1, OpenLibrary: 2 };

function editionOrder(a, b) {
  return (
    Number(b.score.publisher) - Number(a.score.publisher) ||
    Number(Boolean(b.cand.inLibrary)) - Number(Boolean(a.cand.inLibrary)) ||
    Number(Boolean(b.cand.isbn13)) - Number(Boolean(a.cand.isbn13)) ||
    (PROVIDER_RANK[a.cand.source?.provider] ?? 9) - (PROVIDER_RANK[b.cand.source?.provider] ?? 9) ||
    b.score.total - a.score.total
  );
}

/**
 * @returns {{ outcome: 'match'|'ambiguous'|'lowconf'|'notfound', pick?, alternatives: Array, reason: string }}
 */
export function decide(row, candidates, options = {}) {
  const opts = { ...DEFAULTS, ...options };
  const scored = candidates
    .map((cand) => ({ cand, score: scoreCandidate(row, cand, opts) }))
    .sort((a, b) => b.score.total - a.score.total);

  const viable = scored.filter((s) => s.score.total >= opts.reviewScore);
  if (viable.length === 0) {
    // Título traduzido/diferente nas fontes: se o autor confere forte, sugere sem importar.
    const byAuthor = distinctWorks(scored.filter((s) => (s.score.author ?? 0) >= 0.9));
    if (byAuthor.length > 0) {
      return { outcome: 'lowconf', alternatives: byAuthor.slice(0, 3), reason: 'Só o autor confere; o título pode estar traduzido ou escrito diferente nas fontes.' };
    }
    return { outcome: 'notfound', alternatives: [], reason: scored.length === 0 ? 'Nenhum resultado nas fontes.' : 'Nenhum resultado parecido o bastante.' };
  }

  const alternatives = distinctWorks(viable).slice(0, 3);
  const best = viable[0];

  if (best.score.byIsbn || (best.score.total >= opts.acceptScore && best.score.eligible)) {
    const group = workKey(best.cand);
    const acceptable = viable.filter((s) => s.score.eligible && s.score.total >= opts.acceptScore);
    const rival = acceptable.find((s) => workKey(s.cand) !== group && s.score.total >= best.score.total - 0.05);
    if (rival && !best.score.byIsbn) {
      return { outcome: 'ambiguous', alternatives, reason: 'Mais de uma obra igualmente provável; confira abaixo.' };
    }
    const sameWork = viable
      .filter((s) => workKey(s.cand) === group && s.score.total >= best.score.total - 0.1)
      .sort(editionOrder);
    return { outcome: 'match', pick: sameWork[0], alternatives, reason: '' };
  }
  const why = best.score.eligible ? 'Parecido, mas com pouca certeza.' : 'Título ou autor não conferem o bastante.';
  return { outcome: 'lowconf', alternatives, reason: why };
}

function distinctWorks(scored) {
  const seen = new Set();
  const out = [];
  for (const s of scored) {
    const k = workKey(s.cand);
    if (seen.has(k)) continue;
    seen.add(k);
    out.push(s);
  }
  return out;
}
