import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import ExcelJS from 'exceljs';
import { parseCsv, parseDate, parseStatus, readSheet, rowsFromTable } from '../src/sheet.mjs';

test('CSV: delimitador ; aspas e quebras de linha em campo', () => {
  const t = parseCsv('Título;Autor\r\n"Dom; Casmurro";"Machado ""de"" Assis"\r\n');
  assert.deepEqual(t, [['Título', 'Autor'], ['Dom; Casmurro', 'Machado "de" Assis']]);
});

test('cabeçalhos em português/inglês, linhas vazias ignoradas, avisos para dados inválidos', () => {
  const { rows, warnings } = rowsFromTable([
    ['Livro', 'Autora', 'Editora', 'ISBN', 'Situação', 'Avaliação', 'Lido em'],
    ['Dom Casmurro', 'Machado de Assis', 'Ática', '', 'Lido', '4', '10/03/2024'],
    [],
    ['', '', '', '', '', '', ''],
    ['X', '', '', '123', 'talvez', '9', '31/02/2024'],
    ['', '', '', '978-0-306-40615-7', '', '', ''],
  ]);
  assert.equal(rows.length, 3);
  assert.deepEqual({ ...rows[0] }, { row: 2, title: 'Dom Casmurro', author: 'Machado de Assis', publisher: 'Ática', isbn: null, status: 'Read', rating: 4, finishedDate: '2024-03-10' });
  assert.equal(rows[1].status, null);
  assert.equal(rows[2].isbn, '9780306406157');
  assert.equal(warnings.length, 4);
});

test('sem coluna de título => erro claro', () => {
  assert.throws(() => rowsFromTable([['Foo', 'Bar'], ['1', '2']]), /coluna do título/);
});

test('status e datas', () => {
  assert.equal(parseStatus('Quero ler'), 'WantToRead');
  assert.equal(parseStatus('LIDO'), 'Read');
  assert.equal(parseStatus('???'), null);
  assert.equal(parseDate(new Date(Date.UTC(2024, 2, 10))), '2024-03-10');
  assert.equal(parseDate('2024-03-10'), '2024-03-10');
  assert.equal(parseDate('31/02/2024'), null);
});

test('xlsx real: lê a primeira aba, datas e texto rico', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'bulk-'));
  const file = path.join(dir, 'l.xlsx');
  const wb = new ExcelJS.Workbook();
  const ws = wb.addWorksheet('Livros');
  ws.addRow(['Título', 'Autor', 'Editora', 'Término']);
  ws.addRow(['Dom Casmurro', { richText: [{ text: 'Machado ' }, { text: 'de Assis' }] }, 'Ática', new Date(Date.UTC(2024, 2, 10))]);
  await wb.xlsx.writeFile(file);
  const { rows } = await readSheet(file);
  assert.equal(rows[0].author, 'Machado de Assis');
  assert.equal(rows[0].finishedDate, '2024-03-10');
});

test('CSV em windows-1252 é decodificado', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'bulk-'));
  const file = path.join(dir, 'l.csv');
  fs.writeFileSync(file, Buffer.from('T\xEDtulo;Autor\nSolid\xE3o;Fulano\n', 'latin1'));
  const { rows } = await readSheet(file);
  assert.equal(rows[0].title, 'Solidão');
});

test('txt em blocos de 3 linhas, com 1 ou 2 linhas em branco entre livros', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'bulk-'));
  const file = path.join(dir, 'l.txt');
  fs.writeFileSync(file, 'Dom Casmurro\nMachado de Assis\nAntofágica\n\n\nCarrie\nStephen King\nSuma\n\nSó título\n\n');
  const { rows, warnings } = await readSheet(file);
  assert.equal(rows.length, 3);
  assert.deepEqual([rows[1].title, rows[1].author, rows[1].publisher], ['Carrie', 'Stephen King', 'Suma']);
  assert.equal(warnings.length, 1);
});
