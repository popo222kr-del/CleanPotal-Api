import ExcelJS from 'exceljs';

// "CHEMICAL 교체 및 설비 변경점" 엑셀을 읽어 (날짜, 설비, 내용, 메모) 칸으로 바꾼다.
// 시트마다 "일자/요일/설비명" 이 적힌 줄이 머리글(설비 코드가 열 제목)이고, 그 아래 A열에 날짜가 있는 줄이 기록이다.
// 셀 메모(설비 변경점·사용횟수)는 메모 칸으로 옮긴다. 메모 첫 줄의 작성자 이름("에이텍솔루션:")은 뺀다.

export interface ChemicalImportCell { date: string; eqCode: string; content: string; note: string }
export interface ChemicalParsed { cells: ChemicalImportCell[]; codes: string[]; from: string; to: string; sheets: string[] }

type Cell = ExcelJS.CellValue;

function text(v: Cell): string {
  if (v === null || v === undefined) return '';
  if (typeof v === 'string') return v.trim();
  if (typeof v === 'number' || typeof v === 'boolean') return String(v);
  if (v instanceof Date) return ymd(v);
  if (typeof v === 'object') {
    if ('result' in v) return text(v.result as Cell);
    if ('richText' in v) return v.richText.map(t => t.text).join('').trim();
    if ('text' in v) return String(v.text).trim();
  }
  return '';
}
// 엑셀 날짜는 UTC 자정으로 읽힌다 — UTC 기준으로 날짜를 뽑아야 하루 밀리지 않는다.
function ymd(d: Date) {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getUTCFullYear()}-${p(d.getUTCMonth() + 1)}-${p(d.getUTCDate())}`;
}
function noteText(n: ExcelJS.Cell['note']): string {
  if (!n) return '';
  const raw = typeof n === 'string' ? n : (n.texts ?? []).map(t => t.text).join('');
  const lines = raw.replace(/\r/g, '').split('\n');
  // 첫 줄이 "이름:" 뿐이면 작성자 표시 — 뺀다
  if (lines.length > 1 && /^[^:\n]{1,30}:\s*$/.test(lines[0])) lines.shift();
  return lines.map(l => l.trim()).filter(Boolean).join('\n');
}

export async function parseChemicalWorkbook(file: File): Promise<ChemicalParsed> {
  const wb = new ExcelJS.Workbook();
  await wb.xlsx.load(await file.arrayBuffer());
  const cells: ChemicalImportCell[] = [];
  const codes = new Set<string>();
  const sheets: string[] = [];
  wb.eachSheet(ws => {
    let headerRow = 0;
    const cols = new Map<number, string>();
    ws.eachRow((row, r) => {
      if (headerRow === 0) {
        if (text(row.getCell(1).value).includes('일자')) {
          headerRow = r;
          row.eachCell((c, col) => {
            const t = text(c.value).replace(/\s+/g, '');
            if (col > 2 && /^[A-Za-z]{2,}\d/.test(t)) cols.set(col, t.toUpperCase());
          });
        }
        return;
      }
      const d = row.getCell(1).value;
      if (!(d instanceof Date)) return;
      const date = ymd(d);
      for (const [col, code] of cols) {
        const c = row.getCell(col);
        const content = text(c.value);
        const note = noteText(c.note);
        if (!content && !note) continue;
        cells.push({ date, eqCode: code, content, note });
        codes.add(code);
      }
    });
    if (headerRow > 0 && cols.size > 0) sheets.push(ws.name);
  });
  const dates = cells.map(c => c.date).sort();
  return { cells, codes: [...codes], from: dates[0] ?? '', to: dates[dates.length - 1] ?? '', sheets };
}
