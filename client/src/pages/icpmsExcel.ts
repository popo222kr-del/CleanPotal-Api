import ExcelJS from 'exceljs';
import { ICP_ELEMENTS, type IcpmsMeasurement, type IcpmsUploadRow } from '../api/types';

const ELSET = new Map(ICP_ELEMENTS.map(e => [e.toUpperCase(), e]));

function txt(v: ExcelJS.CellValue): string {
  if (v === null || v === undefined) return '';
  if (v instanceof Date) return ymd(v);
  if (typeof v === 'object' && 'result' in v) return String((v as { result: unknown }).result ?? '').trim();
  if (typeof v === 'object' && 'text' in v) return String((v as { text: unknown }).text ?? '').trim();
  return String(v).trim();
}
function ymd(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}
function toDate(v: ExcelJS.CellValue): string {
  if (v instanceof Date) return ymd(v);
  const s = txt(v);
  const m = s.match(/(\d{4})[.\-/](\d{1,2})[.\-/](\d{1,2})/);
  if (m) return `${m[1]}-${m[2].padStart(2, '0')}-${m[3].padStart(2, '0')}`;
  return s;
}
function num(v: ExcelJS.CellValue): number {
  if (typeof v === 'number') return v;
  const n = Number(txt(v).replace(/,/g, ''));
  return Number.isFinite(n) ? n : 0;
}

/** 다중 시트(시트명=설비유형) 업로드 파싱. 헤더 부분일치 규칙(대소문자 무시). */
export async function parseIcpmsUpload(file: File): Promise<IcpmsUploadRow[]> {
  const wb = new ExcelJS.Workbook();
  await wb.xlsx.load(await file.arrayBuffer());
  const out: IcpmsUploadRow[] = [];

  wb.eachSheet((ws) => {
    const processType = ws.name.trim();
    const header = ws.getRow(1);
    let eqCol = -1, bathCol = -1, unitCol = -1, dateCol = -1, catCol = -1;
    const elCols: { col: number; el: string }[] = [];
    const used = new Set<number>();
    header.eachCell((cell, col) => {
      const h = txt(cell.value);
      const U = h.toUpperCase();
      if (eqCol < 0 && U.includes('EQ_ID')) { eqCol = col; used.add(col); return; }
      if (bathCol < 0 && U.includes('BATH')) { bathCol = col; used.add(col); return; }
      if (unitCol < 0 && U === 'UNIT') { unitCol = col; used.add(col); return; }
      if (dateCol < 0 && (U.includes('DT') || U.includes('DATE'))) { dateCol = col; used.add(col); return; }
      const el = ELSET.get(U);
      if (el) { elCols.push({ col, el }); used.add(col); return; }
    });
    if (eqCol < 0) return;                          // EQ_ID 없으면 시트 스킵
    // 남은 첫 컬럼 = 구분(category)
    header.eachCell((_c, col) => { if (catCol < 0 && !used.has(col)) catCol = col; });

    for (let r = 2; r <= ws.rowCount; r++) {
      const row = ws.getRow(r);
      const eqId = txt(row.getCell(eqCol).value);
      if (!eqId) continue;
      const values: Record<string, number> = {};
      for (const { col, el } of elCols) values[el] = num(row.getCell(col).value);
      out.push({
        processType, eqId,
        bathGb: bathCol > 0 ? txt(row.getCell(bathCol).value) : '',
        category: catCol > 0 ? txt(row.getCell(catCol).value) : '',
        unit: unitCol > 0 ? (txt(row.getCell(unitCol).value) || 'ppb') : 'ppb',
        analysisDate: dateCol > 0 ? toDate(row.getCell(dateCol).value) : '',
        values,
      });
    }
  });
  return out;
}

/** 현재 필터 결과를 process_type별 시트로 내보내기 (헤더 고정). */
export async function exportIcpms(rows: IcpmsMeasurement[]): Promise<void> {
  const wb = new ExcelJS.Workbook();
  const header = ['설비 유형', 'EQ_ID', 'Bath_GB', '구분', 'Unit', 'EQ_IN_DT', ...ICP_ELEMENTS];
  const byPt = new Map<string, IcpmsMeasurement[]>();
  for (const r of rows) { const k = r.processType || '기타'; (byPt.get(k) ?? byPt.set(k, []).get(k)!).push(r); }
  if (byPt.size === 0) byPt.set('데이터', []);

  for (const [pt, list] of byPt) {
    const ws = wb.addWorksheet(pt.slice(0, 30) || '시트', { views: [{ state: 'frozen', ySplit: 1 }] });
    ws.addRow(header);
    const hr = ws.getRow(1);
    hr.font = { bold: true };
    hr.eachCell(c => { c.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FFE2E8F0' } }; c.alignment = { horizontal: 'center' }; });
    for (const r of list)
      ws.addRow([r.processType, r.eqId, r.bathGb, r.category, r.unit, r.analysisDate, ...ICP_ELEMENTS.map(e => r.values[e] ?? 0)]);
    ws.columns.forEach((c, i) => { c.width = i < 6 ? 12 : 7; });
  }

  const buf = await wb.xlsx.writeBuffer();
  const url = URL.createObjectURL(new Blob([buf], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }));
  const a = document.createElement('a');
  a.href = url; a.download = `ICPMS_${ymd(new Date())}.xlsx`;
  document.body.appendChild(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
