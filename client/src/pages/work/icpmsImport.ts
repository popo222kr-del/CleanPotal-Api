import ExcelJS from 'exceljs';
import { ICP_ELEMENTS, type IcpmsUploadRow } from '../../api/types';
import { loadWorkbookCompat } from '../xlsxCompat';
import { lineOf } from './icpmsCopy';

// "AETS QA ICP-MS Data" 엑셀(7번 METAL · 8번 N-METAL)을 읽는다.
// 시트 이름 = 설비(MDC01, "NDC03(A급 전용)", "MDC08 (2)"), 블록 제목 "5. ICP-MS 측정 결과 (26/09/01)" = 날짜,
// 그 아래 머리글(Chemical Name·SPEC·원소…)과 DI·HF·S2 줄. 값이 "-" 뿐인 줄은 측정하지 않은 것이라 넣지 않고,
// 오늘 뒤 날짜 블록은 미리 채워 둔 칸이라 뺀다. 읽은 값은 설비 ICP-MS 와 같은 측정 자료로 들어간다.

const ELSET = new Map(ICP_ELEMENTS.map(e => [e.toUpperCase(), e]));
const pad = (n: number) => String(n).padStart(2, '0');
const ymd = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
function txt(v: ExcelJS.CellValue): string {
  if (v === null || v === undefined) return '';
  if (typeof v === 'object' && 'result' in v) return String(v.result ?? '').trim();
  if (typeof v === 'object' && 'richText' in v) return v.richText.map(t => t.text).join('').trim();
  if (typeof v === 'object' && 'text' in v) return String(v.text ?? '').trim();
  return String(v).trim();
}

export interface IcpmsParsed { rows: IcpmsUploadRow[]; sheets: string[]; future: number; from: string; to: string }

export async function parseIcpmsReportFile(file: File): Promise<IcpmsParsed> {
  const wb = await loadWorkbookCompat(file);
  const rows: IcpmsUploadRow[] = [];
  const sheets: string[] = [];
  let future = 0;
  const today = ymd(new Date());
  wb.eachSheet(ws => {
    const eqId = /^([A-Z]+\d*(?:-\d+)?)/.exec(ws.name.toUpperCase().replace(/\s+/g, ''))?.[1] ?? '';
    if (!eqId) return;
    let date = '';
    let els: { col: number; el: string }[] = [];
    let got = 0;
    for (let r = 1; r <= ws.rowCount; r++) {
      const row = ws.getRow(r);
      const a = txt(row.getCell(1).value);
      const t = /ICP-MS\s*측정\s*결과\s*\(\s*(\d{2,4})[./-](\d{1,2})[./-](\d{1,2})\s*\)/.exec(a);
      if (t) {
        date = `${t[1].length === 2 ? `20${t[1]}` : t[1]}-${pad(Number(t[2]))}-${pad(Number(t[3]))}`;
        els = [];
        continue;
      }
      if (!date) continue;
      if (/^Chemical/i.test(a)) {
        els = [];
        row.eachCell((c, col) => { const el = ELSET.get(txt(c.value).toUpperCase()); if (el) els.push({ col, el }); });
        continue;
      }
      if (!a || els.length === 0) continue;
      const values: Record<string, number> = {};
      let any = false;
      for (const { col, el } of els) {
        const v = row.getCell(col).value;
        const n = typeof v === 'number' ? v : v && typeof v === 'object' && 'result' in v && typeof v.result === 'number' ? v.result : NaN;
        if (Number.isFinite(n)) { values[el] = n; any = true; } else values[el] = 0;
      }
      if (!any) continue;
      if (date > today) { future++; continue; }
      rows.push({ processType: lineOf(eqId), eqId, bathGb: a.toUpperCase(), category: '', unit: 'ppb', analysisDate: date, values });
      got++;
    }
    if (got) sheets.push(eqId);
  });
  const dates = rows.map(r => r.analysisDate).sort();
  return { rows, sheets, future, from: dates[0] ?? '', to: dates[dates.length - 1] ?? '' };
}
