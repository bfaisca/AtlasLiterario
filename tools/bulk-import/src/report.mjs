// Relatórios: relatorio.xlsx (tudo) e pendentes.xlsx (no mesmo formato da entrada, para corrigir e reenviar).
import path from 'node:path';
import ExcelJS from 'exceljs';

export const OUTCOME_LABEL = {
  imported: 'Importado',
  already: 'Já estava na biblioteca',
  dryrun: 'Seria importado (simulação)',
  notfound: 'Não encontrado',
  ambiguous: 'Ambíguo',
  lowconf: 'Baixa confiança',
  error: 'Erro (tentar de novo)',
};
export const PENDING = new Set(['notfound', 'ambiguous', 'lowconf', 'error']);

const STATUS_LABEL = { WantToRead: 'Quero ler', Reading: 'Lendo', Read: 'Lido', Abandoned: 'Abandonado' };

const describe = (m) => (m ? `${m.title} — ${(m.authors ?? []).join(', ') || 'autor desconhecido'}${m.publisher ? ` (${m.publisher})` : ''}${m.year ? `, ${m.year}` : ''}` : '');

function styleHeader(ws) {
  ws.getRow(1).font = { bold: true };
  ws.getRow(1).fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FFDCE6F2' } };
  ws.views = [{ state: 'frozen', ySplit: 1 }];
}

export function summarize(records) {
  const counts = {};
  const geo = {};
  for (const r of records) {
    counts[r.outcome] = (counts[r.outcome] ?? 0) + 1;
    if (r.outcome === 'imported' && r.geoStatus) geo[r.geoStatus] = (geo[r.geoStatus] ?? 0) + 1;
  }
  return { counts, geo };
}

export async function writeReports(dir, records, meta) {
  const wb = new ExcelJS.Workbook();

  const sum = wb.addWorksheet('Resumo');
  const { counts, geo } = summarize(records);
  sum.addRows([
    ['Arquivo de entrada', meta.input],
    ['API', meta.api],
    ['Usuário', meta.user],
    ['Modo', meta.dryRun ? 'Simulação (nada foi importado)' : 'Importação'],
    ['Status padrão', STATUS_LABEL[meta.status] ?? meta.status],
    ['Gerado em', meta.generatedAt],
    [],
    ['Linhas processadas', records.length],
    ...Object.entries(OUTCOME_LABEL).map(([k, label]) => [label, counts[k] ?? 0]),
    [],
    ['Dados geográficos dos importados', ''],
    ...Object.entries(geo).map(([k, v]) => [`  ${k}`, v]),
  ]);
  sum.getColumn(1).width = 38; sum.getColumn(2).width = 60;

  const ws = wb.addWorksheet('Resultado');
  ws.columns = [
    ['Linha', 7], ['Título (planilha)', 36], ['Autor (planilha)', 28], ['Editora (planilha)', 22], ['ISBN', 15],
    ['Resultado', 24], ['Motivo / erro', 44], ['Score', 8],
    ['Título encontrado', 36], ['Autores encontrados', 30], ['Editora encontrada', 22], ['Ano', 7], ['ISBN-13', 15], ['Fonte', 14],
    ['Geo', 11], ['Países dos autores', 20], ['Países da obra', 20], ['Alternativas', 70],
  ].map(([header, width]) => ({ header, width }));
  for (const r of records) {
    const m = r.matched;
    ws.addRow([
      r.input.row, r.input.title, r.input.author, r.input.publisher, r.input.isbn ?? '',
      OUTCOME_LABEL[r.outcome] ?? r.outcome, r.reason ?? '', r.score == null ? '' : Number(r.score.toFixed(2)),
      m?.title ?? '', (m?.authors ?? []).join(', '), m?.publisher ?? '', m?.year ?? '', m?.isbn13 ?? '', m?.provider ?? '',
      r.geoStatus ?? '', (r.authorCountries ?? []).join(', '), (r.originCountries ?? []).join(', '),
      (r.alternatives ?? []).map(describe).join('\n'),
    ]);
  }
  styleHeader(ws);
  ws.autoFilter = { from: 'A1', to: { row: 1, column: ws.columnCount } };
  await wb.xlsx.writeFile(path.join(dir, 'relatorio.xlsx'));

  const pending = records.filter((r) => PENDING.has(r.outcome));
  const pw = new ExcelJS.Workbook();
  const ps = pw.addWorksheet('Pendentes');
  ps.columns = [
    ['Título', 36], ['Autor', 28], ['Editora', 22], ['ISBN', 15], ['Status', 14], ['Nota', 7], ['Término', 12],
    ['Motivo', 40], ['Sugestões (copie o que estiver certo para as colunas à esquerda)', 80],
  ].map(([header, width]) => ({ header, width }));
  for (const r of pending) {
    ps.addRow([
      r.input.title, r.input.author, r.input.publisher, r.input.isbn ?? '', STATUS_LABEL[r.input.status] ?? '', r.input.rating ?? '', r.input.finishedDate ?? '',
      `${OUTCOME_LABEL[r.outcome]}${r.reason ? ` — ${r.reason}` : ''}`, (r.alternatives ?? []).map(describe).join('\n'),
    ]);
  }
  styleHeader(ps);
  await pw.xlsx.writeFile(path.join(dir, 'pendentes.xlsx'));
  return { pending: pending.length };
}
