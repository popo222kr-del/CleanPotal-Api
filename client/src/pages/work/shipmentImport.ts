import ExcelJS from 'exceljs';

// 출하 실적 — 업무보고 엑셀(INFORM.xlsx)의 '3.출하 실적' 표를 읽는다.
// 표 위치는 고정이 아니라 '고객 구분' 머리 칸을 찾아서 읽는다(줄이 늘거나 줄어도 된다).
// - 병합 칸(출하금액 두 칸 묶음 등)은 첫 칸만 쓴다.
// - 숨긴 줄(엑셀에서 접어 둔 고객)은 엑셀 화면처럼 뺀다.
// - '합 계' 줄까지 읽는다. 날짜는 시트 위쪽의 '2026-09-29 (화요일)' 같은 칸에서 찾는다.

export interface ShipmentParsed {
  date: string | null;
  columns: string[];
  rows: { customer: string; values: (number | null)[] }[];
}

function text(v: ExcelJS.CellValue): string {
  if (v === null || v === undefined) return '';
  if (typeof v === 'object') {
    if ('result' in v && v.result !== undefined) return text(v.result as ExcelJS.CellValue);
    if ('richText' in v) return v.richText.map(x => x.text).join('');
    if (v instanceof Date) return v.toISOString().slice(0, 10);
    if ('text' in v) return String(v.text);
    return '';
  }
  return String(v);
}

function num(v: ExcelJS.CellValue): number | null {
  const t = text(v).replace(/[,\s]/g, '');
  if (!t || t === '-') return null;
  const n = Number(t);
  return Number.isFinite(n) ? Math.round(n * 100) / 100 : null;
}

export async function parseShipmentWorkbook(file: File): Promise<ShipmentParsed> {
  const wb = new ExcelJS.Workbook();
  await wb.xlsx.load(await file.arrayBuffer());
  for (const ws of wb.worksheets) {
    // 머리 줄: '고객 구분'(띄어쓰기 무관)이 있는 줄
    let headRow = 0, custCol = 0;
    ws.eachRow((row, r) => {
      if (headRow) return;
      row.eachCell((c, i) => {
        if (!headRow && text(c.value).replace(/\s+/g, '') === '고객구분') { headRow = r; custCol = i; }
      });
    });
    if (!headRow) continue;

    // 칸: 고객 구분 오른쪽의 머리 칸 — 병합된 칸은 첫 칸만
    const cols: { i: number; name: string }[] = [];
    const head = ws.getRow(headRow);
    for (let i = custCol + 1; i <= ws.columnCount; i++) {
      const c = head.getCell(i);
      if (c.isMerged && c.master.address !== c.address) continue;
      const name = text(c.value).replace(/\s+/g, ' ').trim();
      if (!name) continue;
      cols.push({ i, name });
    }

    const rows: ShipmentParsed['rows'] = [];
    for (let r = headRow + 1; r <= ws.rowCount && r <= headRow + 60; r++) {
      const row = ws.getRow(r);
      const customer = text(row.getCell(custCol).value).trim();
      if (!customer) { if (rows.length) break; continue; }
      if (row.hidden) continue;
      rows.push({ customer, values: cols.map(c => num(row.getCell(c.i).value)) });
      if (customer.replace(/\s+/g, '') === '합계') break;
    }

    // 날짜: 위쪽 몇 줄에서 yyyy-mm-dd
    let date: string | null = null;
    for (let r = 1; r <= Math.min(headRow, 10) && !date; r++) {
      ws.getRow(r).eachCell(c => {
        if (date) return;
        const m = /(\d{4})[-./](\d{1,2})[-./](\d{1,2})/.exec(text(c.value));
        if (m) date = `${m[1]}-${m[2].padStart(2, '0')}-${m[3].padStart(2, '0')}`;
      });
    }
    return { date, columns: cols.map(c => c.name), rows };
  }
  throw new Error("엑셀에서 '고객 구분' 표(3.출하 실적)를 찾지 못했습니다.");
}
