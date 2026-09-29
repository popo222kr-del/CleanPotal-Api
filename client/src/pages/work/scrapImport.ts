import ExcelJS from 'exceljs';
import type { ScrapCircle, ScrapItemSave, ScrapTagSave } from '../../api/types';

// "폐기품 LIST(.xlsm)" 을 읽는다.
// - 폐기품_이력: "2026-01-15 폐기품 LIST" 제목 줄 → NO/LINE/MAT ID/MAT DESC/S/N/Check/OUT NO/상차여부/특이사항. 상차를 마친 LIST.
// - 폐기품_생산: 지금 적고 있는 LIST(B1 제목의 날짜). 이력에 이미 옮긴 것과 같으면 서버가 건너뛴다.
// - 눈관리 LIST: '작성자' 머리글 아래 줄들 + 오른쪽 분임조 → LINE·담당자 표.

type Val = ExcelJS.CellValue;
function text(v: Val): string {
  if (v === null || v === undefined) return '';
  if (typeof v === 'string') return v.trim();
  if (typeof v === 'number' || typeof v === 'boolean') return String(v);
  if (v instanceof Date) return ymd(v);
  if (typeof v === 'object') {
    if ('result' in v) return text(v.result as Val);
    if ('richText' in v) return v.richText.map(t => t.text).join('').trim();
    if ('text' in v) return String(v.text).trim();
  }
  return '';   // 오류 값(#N/A 등)
}
const pad = (n: number) => String(n).padStart(2, '0');
function ymd(d: Date) { return `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}`; }
const checked = (v: Val) => /^(O|V|Y|✓|✔|○|●|완료)$/i.test(text(v));
const clean = (v: Val) => text(v).replace(/\r/g, '').replace(/\s*\n\s*/g, ' ').replace(/^#N\/A$/, '');

export interface ScrapParsed {
  batches: { date: string; title: string; isClosed: boolean; items: ScrapItemSave[] }[];
  tags: ScrapTagSave[];
  circles: ScrapCircle[];
}

/** 한 시트에서 LIST 묶음 읽기 — 제목 줄마다 새 묶음. */
function readLists(ws: ExcelJS.Worksheet, isClosed: boolean) {
  const out: ScrapParsed['batches'] = [];
  let cur: ScrapParsed['batches'][number] | null = null;
  let col: Record<string, number> | null = null;
  ws.eachRow({ includeEmpty: false }, row => {
    const b = text(row.getCell(2).value);
    const m = /(\d{4})-(\d{1,2})-(\d{1,2}).*폐기품/.exec(b);
    if (m) {
      cur = { date: `${m[1]}-${pad(Number(m[2]))}-${pad(Number(m[3]))}`, title: b.replace(/\s+/g, ' ').trim(), isClosed, items: [] };
      out.push(cur);
      col = null;
      return;
    }
    if (b === 'NO') {
      // 머리글로 열 찾기(시트마다 같지만 혹시 몰라)
      col = {};
      row.eachCell((c, i) => {
        const t = text(c.value).replace(/\s+/g, '');
        if (t === 'LINE') col!.line = i;
        else if (t === 'MATID') col!.matId = i;
        else if (t === 'MATDESC') col!.matDesc = i;
        else if (t === 'S/N') col!.sn = i;
        else if (t.startsWith('Check') || t.startsWith('실물')) col!.matched = i;
        else if (t === 'OUTNO') col!.outNo = i;
        else if (t.startsWith('상차')) col!.loaded = i;
        else if (t.startsWith('특이')) col!.remark = i;
      });
      return;
    }
    if (!cur || !col) return;
    const c = col;
    const get = (k: string) => (c[k] ? row.getCell(c[k]).value : null);
    const it: ScrapItemSave = {
      line: clean(get('line')), matId: clean(get('matId')), matDesc: clean(get('matDesc')), serialNo: clean(get('sn')),
      outNo: clean(get('outNo')), matched: checked(get('matched')), loaded: checked(get('loaded')), remark: clean(get('remark')),
    };
    if (it.line || it.matId || it.matDesc || it.serialNo || it.outNo) cur.items.push(it);
  });
  return out.filter(x => x.items.length > 0);
}

export async function parseScrapWorkbook(file: File): Promise<ScrapParsed> {
  const wb = new ExcelJS.Workbook();
  await wb.xlsx.load(await file.arrayBuffer());
  const res: ScrapParsed = { batches: [], tags: [], circles: [] };

  const hist = wb.getWorksheet('폐기품_이력');
  if (hist) res.batches.push(...readLists(hist, true));
  const prod = wb.getWorksheet('폐기품_생산');
  // 이력에 이미 옮긴 LIST(초기화 뒤 남은 것)는 서버가 날짜·줄 수·첫 S/N 으로 알아보고 건너뛴다
  if (prod) res.batches.push(...readLists(prod, false));

  const tagWs = wb.worksheets.find(w => w.name.replace(/\s/g, '').startsWith('눈관리'));
  if (tagWs) {
    let head = 0;
    tagWs.eachRow({ includeEmpty: false }, (row, r) => {
      if (!head) { if (text(row.getCell(2).value) === '작성자') head = r; return; }
      const writer = clean(row.getCell(2).value);
      const item = clean(row.getCell(4).value);
      const sn = clean(row.getCell(5).value);
      const dv = row.getCell(3).value;
      if (item || sn) {
        res.tags.push({
          date: dv instanceof Date ? ymd(dv) : (/^\d{4}-\d{2}-\d{2}/.exec(text(dv))?.[0] ?? ''),
          writer, item, serialNo: sn, line: clean(row.getCell(6).value), circle: clean(row.getCell(7).value),
          owner: clean(row.getCell(8).value), note: clean(row.getCell(9).value), done: checked(row.getCell(10).value),
        });
      }
      const circle = clean(row.getCell(12).value);
      if (circle) res.circles.push({ id: 0, name: circle, line: clean(row.getCell(13).value), owner: clean(row.getCell(14).value) });
    });
    res.tags = res.tags.filter(t => t.date);
  }
  return res;
}
