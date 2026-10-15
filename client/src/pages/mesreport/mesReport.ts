import ExcelJS from 'exceljs';
import { useEffect, useState } from 'react';
import { api } from '../../api/client';

// MES 조회(기존 MES DB 읽기 전용) — 세 화면이 같이 쓰는 형식·엑셀 내려받기.

export interface Status { configured: boolean; customers: string[]; customerNames: Record<string, string>; groups?: string[] }
export interface LotRow {
  lotId: string; outNo: string; matDesc: string; outSn: string; inSn: string; matId: string; customer: string;
  rework: string; paid: string; line: string; pmEq: string; circle: string; procName: string; matType: string; life: string; useCount: string;
  aetsInDay: string | null; status: string; aetsOutDay: string | null; secOutDay: string | null;
  /** 구분(MEMORY·FOUNDRY·기타세정) · 제품군(MAT GROUP) · 공정 코드 */
  group: string; matGroup: string; oper: string;
  /** COMMENT(마지막 작업 메모) · 세정 이력 칸(서버가 hist 로 보내고 화면이 펼친다) */
  comment: string; hist?: Record<string, string | null> | null;
  /** SEC 반입라인(반입 위치의 라인, 없으면 반출라인) · 반입 위치(SHIPTO 원문) */
  inLine: string; shipTo: string;
  tIn?: string | null; uIn?: string; tInsp?: string | null; uInsp?: string; tFinal?: string | null; uFinal?: string;
  cleanEq?: string; cleanIn?: string | null; cleanInUser?: string; cleanOut?: string | null; cleanOutUser?: string; cleanHours?: string | null;
  dryEq?: string; dryIn?: string | null; dryInUser?: string; dryOut?: string | null; dryOutUser?: string; dryHours?: string | null;
  bakeEq?: string; bakeIn?: string | null; bakeInUser?: string; bakeOut?: string | null; bakeOutUser?: string; bakeHours?: string | null;
  weightIn?: string | null; weightOut?: string | null; etch?: string | null;
  /** 세정상태 = 측정값의 합부판정(RESULT) */
  cleanState?: string | null;
}

export const won = (n: number | null | undefined) => (n == null ? '' : Math.round(n).toLocaleString());
export const qty = (n: number) => (n === 0 ? '' : Number.isInteger(n) ? n.toLocaleString() : n.toLocaleString(undefined, { maximumFractionDigits: 2 }));

let statusCache: Promise<Status | null> | null = null;
export function useLegacyStatus() {
  const [s, setS] = useState<Status | null | undefined>(undefined);
  useEffect(() => {
    statusCache ??= api.get<Status>('/api/mes-report/status').catch(() => null);
    void statusCache.then(setS);
  }, []);
  return s;   // undefined = 확인 중, null = 못 받음
}

/** LOT 목록 칸 — 화면 표와 엑셀이 같은 순서를 쓴다 */
export const LOT_COLS: LotCol[] = [
  ['lotId', 'LOT ID', 16], ['outNo', '반출번호', 14], ['outSn', '반출 S/N', 14], ['inSn', '반입 S/N', 14], ['group', '구분', 9], ['customer', '고객사', 14],
  ['matId', '세정코드', 11], ['matDesc', "PART'S 명", 30], ['matGroup', '제품군', 12], ['aetsInDay', 'AETS 입고일', 11], ['status', '현재 상태', 9],
  ['line', '반출라인', 10], ['pmEq', 'PM 설비명', 12], ['circle', '분임조', 10], ['procName', '공정명', 12], ['useCount', '사용횟수', 8],
  ['life', '누적', 7], ['rework', '재세정', 6], ['paid', '유무상', 6],
];

/** 표를 엑셀로 내려받기 */
/** 시트 하나 — 머리글은 비우지 않는다(빈 머리글이 있으면 엑셀 피벗이 '필드 이름이 잘못되었습니다'로 막힘) */
export interface XlsxSheet { name: string; head: string[]; rows: (string | number | null)[][]; widths?: number[] }

export async function downloadXlsx(fileName: string, sheet: string, head: string[], rows: (string | number | null)[][], widths?: number[], extra: XlsxSheet[] = []) {
  const wb = new ExcelJS.Workbook();
  for (const sh of [{ name: sheet, head, rows, widths }, ...extra]) {
    const ws = wb.addWorksheet(sh.name, { views: [{ state: 'frozen', ySplit: 1 }] });
    ws.addRow(sh.head.map((h, i) => h || `칸${i + 1}`));
    sh.rows.forEach(r => ws.addRow(r.map(v => v ?? '')));
    ws.getRow(1).eachCell(c => { c.font = { bold: true }; c.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FFEEF2F8' } }; });
    (sh.widths ?? sh.head.map(() => 12)).forEach((w, i) => { ws.getColumn(i + 1).width = w; });
    ws.autoFilter = { from: { row: 1, column: 1 }, to: { row: 1, column: sh.head.length } };
  }
  const buf = await wb.xlsx.writeBuffer();
  const url = URL.createObjectURL(new Blob([buf], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }));
  const a = document.createElement('a');
  a.href = url; a.download = fileName;
  document.body.appendChild(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}

/** [칸, 이름, 엑셀 너비, 묶음 머리(있으면 두 줄 머리)] */
export type LotCol = [keyof LotRow, string, number, string?];

/** 세정 이력 조회 칸 — 기존 MES '세정 이력 조회' 화면 순서·묶음. 공정 칸: 입고 = 입고 시각, 입고검사·출하검사 = 끝낸 시각, 세정·DRY·BAKE = 시작(IN)·끝(OUT). */
const stage = (k: 'clean' | 'dry' | 'bake', group: string, eq: string): LotCol[] => [
  [`${k}Eq`, eq, 10, group], [`${k}In`, 'IN', 16, group], [`${k}InUser`, 'IN 작업자', 9, group],
  [`${k}Out`, 'OUT', 16, group], [`${k}OutUser`, 'OUT 작업자', 9, group], [`${k}Hours`, '경과(h)', 8, group],
];
export const HIST_COLS: LotCol[] = [
  ['outNo', '반출번호', 14], ['lotId', 'LOT ID', 16], ['matDesc', "PART'S 명", 30], ['outSn', '반출 S/N', 14],
  ['inSn', '반입 S/N', 14], ['matId', '세정코드', 11], ['rework', '재세정여부', 7], ['paid', '유/무상', 7], ['line', 'SEC 반출라인', 10], ['inLine', 'SEC 반입라인', 10],
  ['pmEq', 'PM 설비명', 12], ['circle', '분임조', 10], ['procName', '공정명', 12], ['matType', 'TYPE', 7], ['life', '누적(A)', 8],
  ['useCount', '사용횟수', 8], ['aetsInDay', 'AETS 입고일', 11], ['status', '현재 상태', 9], ['cleanState', '세정상태', 8], ['aetsOutDay', 'AETS 출고일', 11],
  ['tIn', '일시', 16, '입고'], ['uIn', '작업자', 9, '입고'],
  ['tInsp', '일시', 16, '입고검사'], ['uInsp', '작업자', 9, '입고검사'],
  ...stage('clean', '세정작업', '세정설비'), ...stage('dry', 'DRY', 'DRY STOCKER'), ...stage('bake', 'BAKE OVEN', 'BAKE OVEN'),
  ['tFinal', '일시', 16, '출하검사'], ['uFinal', '작업자', 9, '출하검사'],
  ['weightIn', 'IN', 8, 'Weight'], ['weightOut', 'OUT', 8, 'Weight'], ['etch', '식각량', 8, 'Weight'],
  ['comment', 'COMMENT', 30],
];

export const downloadLots = (name: string, rows: LotRow[], cols: LotCol[] = LOT_COLS) => downloadXlsx(name, 'LOT', cols.map(c => (c[3] ? `${c[3]} ${c[1]}` : c[1])),
  rows.map(r => cols.map(([k]) => r[k] as string | number | null)), cols.map(c => c[2]));
