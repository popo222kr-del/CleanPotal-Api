import ExcelJS from 'exceljs';

// "가성소다(KOH), 폐액 증가량 및 약액 교체 현황" 엑셀(월별 시트, 2019년~)을 읽는다.
// - 시트 이름에서 연·월(예: "2019년09월 (폐액)", "2026년 08월(폐액)"). 폐액 시트만 읽는다(그을음·Bath 용량 등은 건너뜀).
// - 머리글(3행 '구분' 줄)로 열을 찾는다 — 2022년부터 '前 가소량' 이 '前 KOH량' 으로 바뀌었다.
// - 날짜 칸("9월 01일")은 주·야 두 줄 중 첫 줄에만 있다. 시트마다 주→야, 야→주 순서가 다르다.
// - 값이 모두 빈 줄(앞으로 쓸 빈 양식)은 건너뛴다. 같은 달 시트가 둘이면("(2)" 사본) 사본이 아닌 쪽이 이긴다.

export interface WasteRow {
  date: string; shift: string;
  causticBefore: number | null; causticAfter: number | null; wasteBefore: number | null; wasteAfter: number | null;
  dipEquipment: string; sprayEquipment: string; dailyChange: number | null; note: string;
}
export interface WasteParsed { rows: WasteRow[]; sheets: number; from: string; to: string }

type Cell = ExcelJS.CellValue;
function text(v: Cell): string {
  if (v === null || v === undefined) return '';
  if (typeof v === 'string') return v.trim();
  if (typeof v === 'number' || typeof v === 'boolean') return String(v);
  if (v instanceof Date) return `${v.getUTCMonth() + 1}월 ${v.getUTCDate()}일`;
  if (typeof v === 'object') {
    if ('result' in v) return text(v.result as Cell);
    if ('richText' in v) return v.richText.map(t => t.text).join('').trim();
    if ('text' in v) return String(v.text).trim();
  }
  return '';
}
function num(v: Cell): number | null {
  const t = text(v).replace(/,/g, '');
  if (!t) return null;
  const n = Number(t);
  // 엑셀 계산 찌꺼기(1.3000000000000003) 정리
  return Number.isFinite(n) ? Math.round(n * 1000) / 1000 : null;
}
const pad = (n: number) => String(n).padStart(2, '0');

export async function parseWasteWorkbook(file: File): Promise<WasteParsed> {
  const wb = new ExcelJS.Workbook();
  await wb.xlsx.load(await file.arrayBuffer());
  const byKey = new Map<string, WasteRow>();
  let sheets = 0;
  // 사본 시트("(2)")를 먼저 읽고 원본이 덮어쓰게
  const list = [...wb.worksheets].sort((a, b) => Number(/\(\d+\)\s*$/.test(b.name)) - Number(/\(\d+\)\s*$/.test(a.name)));
  for (const ws of list) {
    if (!ws.name.includes('폐액')) continue;
    const ym = /(\d{4})\s*년\s*(\d{1,2})\s*월/.exec(ws.name);
    if (!ym) continue;
    const sheetYear = Number(ym[1]); const sheetMonth = Number(ym[2]);
    // 머리글: '구분' 이 있는 줄
    let head = 0;
    const col: Record<string, number> = {};
    ws.eachRow((row, r) => {
      if (head || r > 10) return;
      const cells: string[] = [];
      row.eachCell({ includeEmpty: true }, (c, i) => { cells[i] = text(c.value).replace(/\s+/g, ''); });
      if (!cells.includes('구분')) return;
      head = r;
      cells.forEach((t, i) => {
        if (!t) return;
        if (t === '구분') col.shift = i;
        else if (/^前/.test(t) && !t.includes('폐액')) col.cb = i;
        else if (/^現/.test(t) && !t.includes('폐액')) col.ca = i;
        else if (t === '前폐액량') col.wb = i;
        else if (t === '現폐액량') col.wa = i;
        else if (t.startsWith('Dip')) col.dip = i;
        else if (t.startsWith('Spray')) col.spray = i;
      });
      // 일교체량·비고·일자 열은 위 줄(2행) 제목에만 있다
      const up = ws.getRow(r - 1);
      up.eachCell((c, i) => {
        const t = text(c.value).replace(/\s+/g, '');
        if (t === '일교체량') col.daily = i;
        else if (t === '비고') col.note = i;
        else if (t === '일자' && !col.date) col.date = i;
      });
      if (col.shift && !col.date) col.date = col.shift - 1;
      // 폐액량 前/現 제목이 빠진 시트 — 前 폐액 다음 열
      if (col.wb && !col.wa) col.wa = col.wb + 1;
    });
    if (!head || !col.shift) continue;
    sheets++;
    let day = 0; let month = sheetMonth;
    ws.eachRow((row, r) => {
      if (r <= head) return;
      const dt = text(row.getCell(col.date).value);
      const m = /(\d{1,2})\s*월\s*(\d{1,2})\s*일/.exec(dt) ?? /^(\d{1,2})\/(\d{1,2})/.exec(dt);
      if (m) { month = Number(m[1]); day = Number(m[2]); }
      const shift = text(row.getCell(col.shift).value);
      if (!day || (shift !== '주' && shift !== '야')) return;
      // 12월 시트의 1월 날짜 등 — 시트 달과 크게 차이나면 해를 넘긴 것
      const year = month - sheetMonth > 6 ? sheetYear - 1 : sheetMonth - month > 6 ? sheetYear + 1 : sheetYear;
      const get = (k: string) => (col[k] ? row.getCell(col[k]).value : null);
      const rec: WasteRow = {
        date: `${year}-${pad(month)}-${pad(day)}`, shift,
        causticBefore: num(get('cb')), causticAfter: num(get('ca')), wasteBefore: num(get('wb')), wasteAfter: num(get('wa')),
        dipEquipment: text(get('dip')), sprayEquipment: text(get('spray')),
        dailyChange: num(get('daily')), note: text(get('note')).replace(/\r/g, '').replace(/^\n+/, '').slice(0, 1000),
      };
      if (rec.dailyChange === 0 && !rec.dipEquipment && !rec.sprayEquipment) rec.dailyChange = null;   // 빈 양식의 0
      const empty = rec.causticBefore === null && rec.causticAfter === null && rec.wasteBefore === null && rec.wasteAfter === null
        && !rec.dipEquipment && !rec.sprayEquipment && rec.dailyChange === null && !rec.note;
      if (empty) return;
      if (Number.isNaN(new Date(rec.date + 'T00:00:00').getTime())) return;
      byKey.set(`${rec.date}|${shift}`, rec);
    });
  }
  const rows = [...byKey.values()].sort((a, b) => a.date.localeCompare(b.date) || (a.shift === '주' ? -1 : 1));
  return { rows, sheets, from: rows[0]?.date ?? '', to: rows[rows.length - 1]?.date ?? '' };
}
