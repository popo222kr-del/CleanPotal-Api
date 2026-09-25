import ExcelJS from 'exceljs';
import type { CheckItemDef, CheckZoneDef } from '../../api/types';

// "체크시트_QR_양식입력.xlsx" 를 읽어 구역·항목으로 바꾼다. 서버에는 코드 기준으로 넣거나 고친다.
// 시트 이름과 열 순서는 그 양식 파일을 따른다(2_구역: A~I, 3_점검항목: A~Y, 표는 5행부터).

type Cell = ExcelJS.CellValue;

function text(v: Cell): string {
  if (v === null || v === undefined) return '';
  if (typeof v === 'string') return v.trim();
  if (typeof v === 'number' || typeof v === 'boolean') return String(v);
  if (v instanceof Date) return ymd(v);
  if (typeof v === 'object') {
    if ('result' in v) return text(v.result as Cell);          // 수식 — 저장된 결과값
    if ('richText' in v) return v.richText.map(t => t.text).join('').trim();
    if ('text' in v) return String(v.text).trim();
  }
  return '';
}
function ymd(d: Date) {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}
function num(v: Cell): number | null {
  const t = text(v).replace(/,/g, '');
  if (!t) return null;
  const n = Number(t);
  return Number.isFinite(n) ? n : null;
}
function dateOrNull(v: Cell): string | null {
  if (v instanceof Date) return ymd(v);
  const t = text(v);
  return /^\d{4}-\d{2}-\d{2}$/.test(t) ? t : null;
}
const yes = (v: Cell, dflt: boolean) => { const t = text(v); return t ? t === '예' : dflt; };

const WEEKDAY: Record<string, number> = { 월: 1, 화: 2, 수: 3, 목: 4, 금: 5, 토: 6, 일: 7 };

/** 입력 방식 → 사진 정책(사진 정책 칸이 비었을 때). 결과 형식은 수치 입력만 수치, 나머지는 OK/NG. */
function photoFromInput(input: string): string {
  if (input.includes('전·후')) return '작업 전·후';
  if (input === '사진 1장') return '항상';
  if (input === '체크') return '없음';
  return 'NG 시';
}

export interface ParsedCheckWorkbook { zones: CheckZoneDef[]; items: CheckItemDef[]; notes: string[]; }

export async function parseCheckWorkbook(file: File): Promise<ParsedCheckWorkbook> {
  const wb = new ExcelJS.Workbook();
  await wb.xlsx.load(await file.arrayBuffer());
  const notes: string[] = [];
  const zs = wb.getWorksheet('2_구역');
  const is = wb.getWorksheet('3_점검항목');
  if (!zs || !is) throw new Error("'2_구역', '3_점검항목' 시트가 있는 양식 입력 파일이 아닙니다.");

  const zones: CheckZoneDef[] = [];
  for (let r = 5; r <= zs.rowCount; r++) {
    const row = zs.getRow(r);
    const code = text(row.getCell(1).value).toUpperCase();
    if (!/^[A-Z0-9][A-Z0-9-]{0,19}$/.test(code)) continue;      // 안내 문장·빈 줄은 건너뛴다
    const name = text(row.getCell(2).value);
    zones.push({
      id: 0, code, name, line: text(row.getCell(3).value), sortOrder: num(row.getCell(4).value) ?? zones.length + 1,
      isCommon: code.endsWith('-ALL') || name === '전 구역', hasQr: yes(row.getCell(5).value, true),
      qrLocation: text(row.getCell(6).value), qrCount: num(row.getCell(7).value) ?? 1,
      isActive: yes(row.getCell(8).value, true), note: text(row.getCell(9).value),
    });
  }

  const items: CheckItemDef[] = [];
  for (let r = 5; r <= is.rowCount; r++) {
    const row = is.getRow(r);
    const c = (n: number) => row.getCell(n).value;
    const zoneCode = text(c(3)).toUpperCase();
    const body = text(c(5));
    if (!zoneCode || !body) continue;
    const input = text(c(10));
    const isNum = input === '수치 입력';
    const judge = text(c(14));
    const photo = text(c(20)) || photoFromInput(input);
    const weekday = WEEKDAY[text(c(9))] ?? null;
    const ngDept = text(c(22));
    items.push({
      id: 0, code: text(c(1)).toUpperCase(), zoneCode, sortOrder: num(c(2)) ?? items.length + 1,
      text: body, detail: text(c(6)), cycle: text(c(7)), timing: text(c(8)), weekday,
      resultType: isNum ? 'NUM' : 'OKNG', unit: text(c(11)), minValue: num(c(12)), maxValue: num(c(13)),
      judgeMode: !isNum ? 'NONE' : judge === '절댓값 이하' ? 'ABS' : judge.startsWith('범위') ? 'RANGE' : 'NONE',
      photoPolicy: photo, required: yes(c(18), true), allowNa: yes(c(19), false), paperForm: text(c(15)),
      // 기본설정의 권장 문구("항목별 확인 필요")가 그대로 딸려 온 것은 부서가 아니다.
      ngDept: ngDept.includes('확인 필요') ? '' : ngDept,
      validFrom: dateOrNull(c(23)), validTo: dateOrNull(c(24)), revisionNote: text(c(25)),
      isActive: yes(c(16), true), note: text(c(17)), updatedAt: '', updatedBy: '',
    });
    if (text(c(8)) === '주 1회' && weekday === null) notes.push(`${text(c(1)) || body} — 주 1회 요일이 비어 있습니다(그 주 아무 날이나로 처리)`);
  }
  if (wb.getWorksheet('4_설비_2단계') || wb.getWorksheet('5_설비점검_2단계'))
    notes.push('설비 시트(4·5번)는 2단계에서 가져옵니다 — 이번에는 구역·항목만 읽었습니다.');
  return { zones, items, notes };
}
