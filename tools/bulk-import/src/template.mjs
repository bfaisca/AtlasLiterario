// Gera data/modelo-livros.xlsx com os cabeçalhos aceitos e exemplos.
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import ExcelJS from 'exceljs';

const out = process.argv[2] ?? path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'data', 'modelo-livros.xlsx');
const wb = new ExcelJS.Workbook();
const ws = wb.addWorksheet('Livros');
ws.columns = [['Título', 36], ['Autor', 28], ['Editora', 22], ['ISBN', 16], ['Status', 14], ['Nota', 7], ['Término', 12]].map(([header, width]) => ({ header, width }));
ws.addRows([
  ['Dom Casmurro', 'Machado de Assis', 'Penguin-Companhia', '', 'Lido', 5, '2024-03-10'],
  ['Cem Anos de Solidão', 'Gabriel García Márquez', 'Record', '', 'Lido', '', ''],
  ['O Processo', 'Franz Kafka', '', '', '', '', ''],
  ['', '', '', '9788535914849', 'Quero ler', '', ''],
]);
ws.getRow(1).font = { bold: true };
await wb.xlsx.writeFile(out);
console.log(`Modelo gravado em ${out}`);
