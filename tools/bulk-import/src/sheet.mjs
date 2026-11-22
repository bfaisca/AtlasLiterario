// Leitura de planilhas (.xlsx / .csv) em linhas { row, title, author, publisher, isbn, status, rating, finishedDate }.
import fs from 'node:fs';
import path from 'node:path';
import ExcelJS from 'exceljs';
import { cleanIsbn, normalize } from './text.mjs';

const ALIASES = {
  title: ['titulo', 'livro', 'nome', 'nome do livro', 'title', 'book', 'obra'],
  author: ['autor', 'autora', 'autor a', 'autores', 'autor es', 'author', 'authors', 'escritor', 'escritora'],
  publisher: ['editora', 'publisher', 'editor', 'casa editorial'],
  isbn: ['isbn', 'isbn 13', 'isbn13', 'isbn 10', 'isbn10'],
  status: ['status', 'situacao', 'estado', 'leitura'],
  rating: ['nota', 'avaliacao', 'rating', 'estrelas'],
  finishedDate: ['termino', 'data de termino', 'data termino', 'terminado em', 'lido em', 'finished', 'data de conclusao', 'conclusao'],
};

const STATUS = new Map([
  ['quero ler', 'WantToRead'], ['want to read', 'WantToRead'], ['wanttoread', 'WantToRead'], ['desejado', 'WantToRead'], ['para ler', 'WantToRead'],
  ['lendo', 'Reading'], ['reading', 'Reading'], ['em andamento', 'Reading'],
  ['lido', 'Read'], ['lida', 'Read'], ['read', 'Read'], ['concluido', 'Read'], ['finalizado', 'Read'],
  ['abandonado', 'Abandoned'], ['abandoned', 'Abandoned'], ['abandonei', 'Abandoned'],
]);

export function parseStatus(value) {
  const key = normalize(value);
  return key ? (STATUS.get(key) ?? null) : null;
}

// ---------- CSV ----------

function decodeText(buffer) {
  try {
    return new TextDecoder('utf-8', { fatal: true }).decode(buffer).replace(/^﻿/, '');
  } catch {
    return new TextDecoder('windows-1252').decode(buffer); // "CSV" padrão do Excel no Windows
  }
}

export function parseCsv(text) {
  const firstLine = text.split(/\r?\n/, 1)[0] ?? '';
  const delimiter = (firstLine.match(/;/g)?.length ?? 0) > (firstLine.match(/,/g)?.length ?? 0) ? ';'
    : (firstLine.match(/\t/g)?.length ?? 0) > (firstLine.match(/,/g)?.length ?? 0) ? '\t' : ',';
  const rows = [];
  let row = [];
  let field = '';
  let quoted = false;
  for (let i = 0; i < text.length; i++) {
    const ch = text[i];
    if (quoted) {
      if (ch === '"' && text[i + 1] === '"') { field += '"'; i++; }
      else if (ch === '"') quoted = false;
      else field += ch;
    } else if (ch === '"') quoted = true;
    else if (ch === delimiter) { row.push(field); field = ''; }
    else if (ch === '\n' || ch === '\r') {
      if (ch === '\r' && text[i + 1] === '\n') i++;
      row.push(field); rows.push(row); row = []; field = '';
    } else field += ch;
  }
  if (field !== '' || row.length > 0) { row.push(field); rows.push(row); }
  return rows;
}

// ---------- texto em blocos ----------

/** Formato .txt: cada livro em um bloco de 3 linhas (título, autor, editora), blocos separados por linha em branco. */
export function parseBlocks(text) {
  const table = [['Título', 'Autor', 'Editora']];
  const warnings = [];
  let block = [];
  let startLine = 0;
  const flush = () => {
    if (block.length === 0) return;
    if (block.length < 2 || block.length > 3) warnings.push(`Linha ${startLine}: bloco com ${block.length} linha(s) (esperado: título, autor, editora): "${block[0]}".`);
    table.push([block[0], block[1] ?? '', block[2] ?? '']);
    block = [];
  };
  text.split(/\r?\n/).forEach((raw, i) => {
    const line = raw.trim();
    if (!line) return flush();
    if (block.length === 0) startLine = i + 1;
    block.push(line);
  });
  flush();
  return { table, warnings };
}

// ---------- células ----------

function cellValue(value) {
  if (value == null) return '';
  if (value instanceof Date) return value;
  if (typeof value === 'object') {
    if (Array.isArray(value.richText)) return value.richText.map((p) => p.text).join('');
    if ('result' in value) return cellValue(value.result);
    if ('text' in value) return cellValue(value.text);
    if ('hyperlink' in value) return String(value.hyperlink);
    return '';
  }
  return value;
}

function asText(value) {
  return value instanceof Date ? '' : String(value ?? '').trim();
}

export function parseDate(value) {
  if (value instanceof Date && !Number.isNaN(value.getTime())) return value.toISOString().slice(0, 10);
  const text = asText(value);
  let m = text.match(/^(\d{4})-(\d{2})-(\d{2})/);
  if (m) return validDate(m[1], m[2], m[3]);
  m = text.match(/^(\d{1,2})[/.-](\d{1,2})[/.-](\d{4})$/);
  if (m) return validDate(m[3], m[2], m[1]);
  return null;
}

function validDate(y, mo, d) {
  const date = new Date(Date.UTC(Number(y), Number(mo) - 1, Number(d)));
  const ok = date.getUTCFullYear() === Number(y) && date.getUTCMonth() === Number(mo) - 1 && date.getUTCDate() === Number(d);
  return ok ? date.toISOString().slice(0, 10) : null;
}

// ---------- tabela ----------

export function rowsFromTable(table) {
  const aliasToField = new Map();
  for (const [field, names] of Object.entries(ALIASES)) for (const n of names) aliasToField.set(n, field);

  const headerIndex = table.findIndex((cells) => cells.some((c) => aliasToField.get(normalize(asText(c))) === 'title'));
  if (headerIndex < 0) {
    throw new Error('Não encontrei a coluna do título. Use um cabeçalho como "Título" (ou "Livro"/"Title") na primeira linha. Colunas aceitas: Título, Autor, Editora, ISBN, Status, Nota, Término.');
  }
  const columns = {};
  table[headerIndex].forEach((cell, index) => {
    const field = aliasToField.get(normalize(asText(cell)));
    if (field && !(field in columns)) columns[field] = index;
  });

  const rows = [];
  const warnings = [];
  for (let i = headerIndex + 1; i < table.length; i++) {
    const get = (field) => (field in columns ? table[i][columns[field]] : undefined);
    const title = asText(get('title'));
    const line = i + 1;
    const rawIsbn = asText(get('isbn'));
    if (!title && !rawIsbn) continue; // linha vazia
    const isbn = rawIsbn ? cleanIsbn(rawIsbn) : null;
    if (rawIsbn && !isbn) warnings.push(`Linha ${line}: ISBN "${rawIsbn}" inválido; ignorado.`);
    if (!title && !isbn) continue;

    const rawStatus = asText(get('status'));
    const status = parseStatus(rawStatus);
    if (rawStatus && !status) warnings.push(`Linha ${line}: status "${rawStatus}" não reconhecido; usando o padrão.`);

    const rawRating = asText(get('rating')).replace(',', '.');
    let rating = null;
    if (rawRating) {
      const n = Number(rawRating);
      if (Number.isInteger(n) && n >= 1 && n <= 5) rating = n;
      else warnings.push(`Linha ${line}: nota "${rawRating}" deve ser inteira de 1 a 5; ignorada.`);
    }
    const rawDate = get('finishedDate');
    const finishedDate = rawDate === undefined || rawDate === '' ? null : parseDate(rawDate);
    if (rawDate !== undefined && rawDate !== '' && !finishedDate) warnings.push(`Linha ${line}: data "${asText(rawDate)}" não reconhecida; ignorada.`);

    rows.push({ row: line, title, author: asText(get('author')), publisher: asText(get('publisher')), isbn, status, rating, finishedDate });
  }
  return { rows, warnings };
}

export async function readSheet(file) {
  const ext = path.extname(file).toLowerCase();
  const buffer = fs.readFileSync(file);
  let table;
  let extraWarnings = [];
  if (ext === '.txt') {
    ({ table, warnings: extraWarnings } = parseBlocks(decodeText(buffer)));
  } else if (ext === '.csv') {
    table = parseCsv(decodeText(buffer));
  } else if (ext === '.xlsx') {
    const wb = new ExcelJS.Workbook();
    await wb.xlsx.load(buffer);
    const ws = wb.worksheets[0];
    if (!ws) throw new Error('A planilha não tem nenhuma aba.');
    table = [];
    ws.eachRow({ includeEmpty: true }, (r, n) => {
      const cells = [];
      for (let c = 1; c <= ws.columnCount; c++) cells.push(cellValue(r.getCell(c).value));
      table[n - 1] = cells;
    });
    for (let i = 0; i < table.length; i++) table[i] ??= [];
  } else {
    throw new Error(`Formato "${ext}" não suportado. Use .xlsx, .csv ou .txt (3 linhas por livro).`);
  }
  const result = rowsFromTable(table);
  return { rows: result.rows, warnings: [...extraWarnings, ...result.warnings] };
}
