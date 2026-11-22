// Normalização de texto compartilhada pelo leitor de planilha e pelo comparador.

/** minúsculas, sem acentos, sem pontuação, espaços colapsados. */
export function normalize(value) {
  return String(value ?? '')
    .normalize('NFD')
    .replace(/\p{M}+/gu, '')
    .toLowerCase()
    .replace(/&/g, ' e ')
    .replace(/[^\p{L}\p{N}]+/gu, ' ')
    .trim()
    .replace(/\s+/g, ' ');
}

const ARTICLES = new Set(['o', 'a', 'os', 'as', 'um', 'uma', 'the', 'el', 'la', 'los', 'las', 'le', 'les', 'il', 'der', 'die', 'das']);

/** tokens de um título, sem artigo inicial. */
export function titleTokens(value) {
  const tokens = normalize(value).split(' ').filter(Boolean);
  if (tokens.length > 1 && ARTICLES.has(tokens[0])) tokens.shift();
  return tokens;
}

/** Dígitos do ISBN (aceita hífens e espaços); null se não houver 10 ou 13 dígitos válidos. */
export function cleanIsbn(value) {
  const raw = String(value ?? '').replace(/[\s-]/g, '').toUpperCase();
  if (/^\d{13}$/.test(raw)) return isValidIsbn13(raw) ? raw : null;
  if (/^\d{9}[\dX]$/.test(raw)) return isbn10To13(raw);
  return null;
}

function isValidIsbn13(isbn) {
  let sum = 0;
  for (let i = 0; i < 12; i++) sum += Number(isbn[i]) * (i % 2 === 0 ? 1 : 3);
  return (10 - (sum % 10)) % 10 === Number(isbn[12]);
}

function isbn10To13(isbn10) {
  let sum = 0;
  for (let i = 0; i < 9; i++) sum += Number(isbn10[i]) * (10 - i);
  sum += isbn10[9] === 'X' ? 10 : Number(isbn10[9]);
  if (sum % 11 !== 0) return null;
  const core = '978' + isbn10.slice(0, 9);
  let s = 0;
  for (let i = 0; i < 12; i++) s += Number(core[i]) * (i % 2 === 0 ? 1 : 3);
  return core + ((10 - (s % 10)) % 10);
}
