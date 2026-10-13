import ExcelJS from 'exceljs';
import { useEffect, useState } from 'react';
import { api } from '../../api/client';

// MES 조회(기존 MES DB 읽기 전용) — 세 화면이 같이 쓰는 형식·엑셀 내려받기.

export interface Status { configured: boolean; customers: string[]; customerNames: Record<string, string>; groups?: string[] }
export interface LotRow {
  lotId: string; outNo: string; matDesc: string; outSn: string; inSn: string; matId: string; customer: string; price: number | null;
  rework: string; paid: string; line: string; pmEq: string; circle: string; procName: string; matType: string; life: string; useCount: string;
  aetsInDay: string | null; status: string; aetsOutDay: string | null; secOutDay: string | null;
  /** 구분(MEMORY·FOUNDRY·기타세정) · 제품군(MAT GROUP) · 공정 코드 */
  group: string; matGroup: string; oper: string;
  /** 출하예정일(LOT_CMF_6) · COMMENT(마지막 작업 메모) */
  dueTime: string | null; comment: string;
  /** 공정에 들어간 시각 — 세정 이력 조회에서만(입고·입고검사·세정작업·DRY·BAKE OVEN·출하검사) */
  tIn?: string | null; tInsp?: string | null; tClean?: string | null; tDry?: string | null; tBake?: string | null; tFinal?: string | null;
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
  ['life', '누적', 7], ['rework', '재세정', 6], ['paid', '유무상', 6], ['price', '단가', 10],
];

/** 표를 엑셀로 내려받기 */
export async function downloadXlsx(fileName: string, sheet: string, head: string[], rows: (string | number | null)[][], widths?: number[]) {
  const wb = new ExcelJS.Workbook();
  const ws = wb.addWorksheet(sheet, { views: [{ state: 'frozen', ySplit: 1 }] });
  ws.addRow(head);
  rows.forEach(r => ws.addRow(r.map(v => v ?? '')));
  ws.getRow(1).eachCell(c => { c.font = { bold: true }; c.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FFEEF2F8' } }; });
  (widths ?? head.map(() => 12)).forEach((w, i) => { ws.getColumn(i + 1).width = w; });
  ws.autoFilter = { from: { row: 1, column: 1 }, to: { row: 1, column: head.length } };
  const buf = await wb.xlsx.writeBuffer();
  const url = URL.createObjectURL(new Blob([buf], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }));
  const a = document.createElement('a');
  a.href = url; a.download = fileName;
  document.body.appendChild(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}

export type LotCol = [keyof LotRow, string, number];

/** 세정 이력 조회 칸 — 기존 MES '세정 이력 조회' 화면 순서. 공정 칸은 그 공정에 들어간 시각. 아직 못 찾은 것(무게·세정상태·혼입체크·SEC 반입라인·TAT)은 확인 후 추가. */
export const HIST_COLS: LotCol[] = [
  ['secOutDay', 'SEC 출고일', 11], ['outNo', '반출번호', 14], ['lotId', 'LOT ID', 16], ['matDesc', "PART'S 명", 30], ['outSn', '반출 S/N', 14],
  ['inSn', '반입 S/N', 14], ['matId', '세정코드', 11], ['rework', '재세정여부', 7], ['paid', '유/무상', 7], ['line', 'SEC 반출라인', 10],
  ['pmEq', 'PM 설비명', 12], ['circle', '분임조', 10], ['procName', '공정명', 12], ['matType', 'TYPE', 7], ['life', '누적(A)', 8],
  ['useCount', '사용횟수', 8], ['aetsInDay', 'AETS 입고일', 11], ['status', '현재 상태', 9], ['aetsOutDay', 'AETS 출고일', 11],
  ['tIn', '입고', 16], ['tInsp', '입고검사', 16], ['tClean', '세정작업', 16], ['tDry', 'DRY', 16], ['tBake', 'BAKE OVEN', 16], ['tFinal', '출하검사', 16],
  ['comment', 'COMMENT', 24],
];

export const downloadLots = (name: string, rows: LotRow[], cols: LotCol[] = LOT_COLS) => downloadXlsx(name, 'LOT', cols.map(c => c[1]),
  rows.map(r => cols.map(([k]) => r[k] as string | number | null)), cols.map(c => c[2]));
