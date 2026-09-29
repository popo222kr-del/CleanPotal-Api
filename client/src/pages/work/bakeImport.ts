import ExcelJS from 'exceljs';
import type { BakeSave } from '../../api/types';

// "BAKE OVEN 그을음 현황" 엑셀(월별 시트 "2026년9월(그을음)", 2019년~)을 읽는다.
// - 블록 = 머리글 줄(A: "9/28\n(야간)", C~: 오븐 코드) + TRACK IN·TRACK OUT·품명·S/N·그을음·온도↑·온도↓(두 줄)·Q'TZ·비고.
//   2019~2020년 시트에는 S/N 줄이 없다 — 줄 순서가 아니라 B열 제목으로 읽는다.
// - 시트는 최근 날짜가 위. 같은 날짜·교대 블록이 여럿이면(한 교대에 두 번 돌림) 아래 것이 1회차.
// - 비가동·HOLD 는 TRACK IN 칸에 적고 블록 아래까지 병합해 두었다 — 병합의 나머지 칸은 비어 있는 것으로 본다.
// - 오븐 이름: "NBO04-1[신규]" → NBO04-1, "신규 BAKE 2-1" → NBO02-1. 이름이 깨진 칸은 바로 앞 블록의 같은 열 이름을 쓴다.

type Cell = ExcelJS.Cell;
type Val = ExcelJS.CellValue;

function text(v: Val): string {
  if (v === null || v === undefined) return '';
  if (typeof v === 'string') return v.trim();
  if (typeof v === 'number' || typeof v === 'boolean') return String(v);
  if (v instanceof Date) return v.toISOString();
  if (typeof v === 'object') {
    if ('result' in v) return text(v.result as Val);
    if ('richText' in v) return v.richText.map(t => t.text).join('').trim();
    if ('text' in v) return String(v.text).trim();
  }
  return '';
}

/** 병합 칸 — 같은 줄로 합친 칸은 첫 칸 값을, 아래로 합친 칸은 빈 값으로 본다. */
function val(c: Cell): Val {
  if (c.isMerged && c.master && c.master.address !== c.address) return c.master.row === c.row ? c.master.value : null;
  return c.value;
}

const pad = (n: number) => String(n).padStart(2, '0');
/** exceljs 날짜는 엑셀에 보이는 시각을 UTC 로 담아 준다. */
function isoFromUtc(d: Date) {
  return `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}T${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}:00`;
}

/** TRACK IN/OUT 칸 → 시각 또는 상태 글자. 손으로 친 "2023-05-014  8:00", "16:00:00 PM" 도 읽는다. */
function when(v: Val): { at: string | null; status: string; bad: string } {
  if (v instanceof Date) return { at: isoFromUtc(v), status: '', bad: '' };
  if (typeof v === 'number' && v > 30000 && v < 80000) return { at: isoFromUtc(new Date(Math.round((v - 25569) * 86400000))), status: '', bad: '' };
  const t = text(v).replace(/\s+/g, ' ').trim();
  if (!t) return { at: null, status: '', bad: '' };
  if (/^\d{3,5}\D/.test(t)) {
    const m = /^(\d{4})\D+(\d{1,2})\D+(\d{1,3})\D+(\d{1,2}):(\d{2})(?::\d{2})?\s*(AM|PM)?/i.exec(t);
    if (m) {
      let h = Number(m[4]);
      const ap = (m[6] ?? '').toUpperCase();
      if (ap === 'PM' && h < 12) h += 12;
      if (ap === 'AM' && h === 12) h = 0;
      const y = Number(m[1]), mo = Number(m[2]), d = Number(m[3]), mi = Number(m[5]);
      const dt = new Date(Date.UTC(y, mo - 1, d, h, mi));
      if (y >= 2015 && y <= 2040 && dt.getUTCMonth() === mo - 1 && dt.getUTCDate() === d && h < 24) return { at: isoFromUtc(dt), status: '', bad: '' };
    }
    return { at: null, status: '', bad: t };   // 날짜처럼 쳤지만 틀린 값(2월 29일 등) — 비고에 남긴다
  }
  if (t.length <= 1) return { at: null, status: '', bad: '' };   // "`" 같은 찌꺼기
  return { at: null, status: t.slice(0, 100), bad: '' };
}

/** 오븐 머리글 → 코드. 모르면 ''. */
function ovenCode(raw: string): string {
  let t = raw.replace(/\+.*$/, '').replace(/\[.*?\]/g, '').replace(/\s+/g, ' ').trim().toUpperCase();
  const nb = /신규\s*BAKE\s*(\d+)\s*-\s*(\d)/.exec(t);
  if (nb) t = `NBO${pad(Number(nb[1]))}-${nb[2]}`;
  const m = /^([MN])BO\s*0*(\d{1,2})\s*-\s*(\d)$/.exec(t.replace(/\s/g, ''));
  return m ? `${m[1]}BO${pad(Number(m[2]))}-${m[3]}` : '';
}

type Field = 'trackIn' | 'trackOut' | 'item' | 'serialNo' | 'soot' | 'tempUp' | 'tempDown' | 'tempDown2' | 'quartz' | 'note';
function fieldOf(label: string, prev: Field | null): Field | null {
  const t = label.replace(/\s+/g, '').toUpperCase();
  if (!t) return prev === 'tempDown' ? 'tempDown2' : null;
  if (t.startsWith('TRACKIN')) return 'trackIn';
  if (t.startsWith('TRACKOUT')) return 'trackOut';
  if (t.startsWith('품명')) return 'item';
  if (t.startsWith('S/N')) return 'serialNo';
  if (t.startsWith('그을음')) return 'soot';
  if (t.includes('↑')) return 'tempUp';
  if (t.includes('↓')) return prev === 'tempDown' ? 'tempDown2' : 'tempDown';
  if (t.includes("TZ")) return 'quartz';
  if (t.startsWith('비고')) return 'note';
  return null;
}

interface Block { date: string; shift: string; order: number; rows: BakeSave[]; sig: string }
export interface BakeParsed { rows: BakeSave[]; sheets: number; blocks: number; from: string; to: string; ovens: string[] }

export async function parseBakeWorkbook(file: File): Promise<BakeParsed> {
  const wb = new ExcelJS.Workbook();
  await wb.xlsx.load(await file.arrayBuffer());
  // 시트를 연·월 순으로
  const sheets = wb.worksheets
    .map(ws => ({ ws, ym: /(\d{4})\s*년\s*(\d{1,2})\s*월/.exec(ws.name) }))
    .filter(x => x.ws.name.includes('그을음') && x.ym)
    .sort((a, b) => Number(a.ym![1]) * 12 + Number(a.ym![2]) - (Number(b.ym![1]) * 12 + Number(b.ym![2])));
  const byKey = new Map<string, Block[]>();
  const ovenSet = new Set<string>();
  let order = 0;

  for (const { ws, ym } of sheets) {
    const sheetYear = Number(ym![1]); const sheetMonth = Number(ym![2]);
    const maxCol = Math.max(ws.columnCount, 3);
    const inSheet: Block[] = [];
    let cols: string[] = [];          // 열 번호 → 오븐 코드(앞 블록 이름 이어 쓰기용)
    let cur: { block: Block; recs: Map<number, BakeSave & { _bad: string[] }>; prev: Field | null } | null = null;

    const close = () => {
      if (!cur) return;
      const rows = [...cur.recs.values()].map(({ _bad, ...r }) => {
        if (_bad.length) r.note = [r.note, ..._bad.map(b => `(${b})`)].filter(Boolean).join(' ').slice(0, 500);
        return r;
      }).filter(r => r.status || r.trackIn || r.trackOut || r.item || r.serialNo || r.soot || r.tempUp || r.tempDown || r.tempDown2 || r.quartz || r.note);
      if (rows.length) {
        cur.block.rows = rows;
        cur.block.sig = JSON.stringify(rows.map(r => [r.eqCode, r.status, r.trackIn, r.trackOut, r.serialNo, r.item]));
        inSheet.push(cur.block);
      }
      cur = null;
    };

    for (let r = 1; r <= ws.rowCount; r++) {
      const row = ws.getRow(r);
      const heads: string[] = [];
      let codes = 0;
      for (let c = 3; c <= maxCol; c++) {
        const t = text(row.getCell(c).value);
        heads[c] = t;
        if (/^[MN]BO|신규\s*BAKE/i.test(t)) codes++;
      }
      if (codes >= 3) {
        // 새 블록 머리글
        close();
        const label = text(row.getCell(1).value);
        const dm = /(\d{1,2})\s*\/\s*(\d{1,2})/.exec(label);
        const rest = label.slice((dm?.index ?? 0) + (dm?.[0].length ?? 0));
        const shift = /야/.test(rest) ? '야' : /[주줘]/.test(rest) ? '주' : '';
        const next: string[] = [];
        for (let c = 3; c <= maxCol; c++) next[c] = ovenCode(heads[c] ?? '') || (heads[c] ? cols[c] ?? '' : '');
        cols = next;
        if (!dm || !shift) continue;
        const month = Number(dm[1]); const day = Number(dm[2]);
        const year = month - sheetMonth > 6 ? sheetYear - 1 : sheetMonth - month > 6 ? sheetYear + 1 : sheetYear;
        const dt = new Date(Date.UTC(year, month - 1, day));
        if (dt.getUTCMonth() !== month - 1) continue;
        cur = { block: { date: `${year}-${pad(month)}-${pad(day)}`, shift, order: order++, rows: [], sig: '' }, recs: new Map(), prev: null };
        continue;
      }
      if (!cur) continue;
      const blk = cur;
      const f = fieldOf(text(val(row.getCell(2))), blk.prev);
      blk.prev = f;
      if (!f) continue;
      for (let c = 3; c <= maxCol; c++) {
        const code = cols[c];
        if (!code) continue;
        const v = val(row.getCell(c));
        if (v === null || v === undefined || text(v) === '') continue;
        let rec = blk.recs.get(c);
        if (!rec) {
          rec = { date: blk.block.date, shift: blk.block.shift, round: 1, eqCode: code, status: '', trackIn: null, trackOut: null,
            item: '', serialNo: '', soot: '', tempUp: '', tempDown: '', tempDown2: '', quartz: '', note: '', _bad: [] };
          blk.recs.set(c, rec);
        }
        if (f === 'trackIn' || f === 'trackOut') {
          const w = when(v);
          rec[f] = w.at;
          if (w.status && f === 'trackIn') rec.status = w.status;
          if (w.bad) rec._bad.push(`${f === 'trackIn' ? 'IN' : 'OUT'} ${w.bad}`);
        } else {
          rec[f] = text(v).replace(/\r/g, '').replace(/\s*\n\s*/g, ' ').slice(0, f === 'note' ? 500 : f === 'soot' ? 200 : 100);
        }
      }
    }
    close();
    // 시트 안에서는 아래 블록이 먼저 — 거꾸로 넣어 회차가 시간 순이 되게
    for (const b of inSheet.reverse()) {
      const k = `${b.date}|${b.shift}`;
      const list = byKey.get(k) ?? [];
      if (list.some(x => x.sig === b.sig)) continue;   // 다음 달 시트에 옮겨 적은 같은 블록
      list.push(b);
      byKey.set(k, list);
    }
  }

  const rows: BakeSave[] = [];
  let blocks = 0;
  for (const list of byKey.values()) {
    list.slice(0, 9).forEach((b, i) => {
      blocks++;
      for (const r of b.rows) { rows.push({ ...r, round: i + 1 }); ovenSet.add(r.eqCode); }
    });
  }
  rows.sort((a, b) => a.date.localeCompare(b.date) || (a.shift === b.shift ? 0 : a.shift === '주' ? -1 : 1) || a.round - b.round);
  return { rows, sheets: sheets.length, blocks, from: rows[0]?.date ?? '', to: rows[rows.length - 1]?.date ?? '', ovens: [...ovenSet].sort() };
}
