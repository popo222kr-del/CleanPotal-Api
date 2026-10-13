import type { LotRow } from './mesReport';

// 입·출고 현황(재공) 공통 — 표 화면과 [새 창] LOT 목록이 같은 조건으로 LOT 를 거른다(숫자와 목록이 늘 같다).

export const NO_LINE = '(라인 없음)';
/** 삼성(MEMORY·FOUNDRY 등 구분에 든 고객사) / 기타(나머지 = 기타세정) */
export type Scope = 'samsung' | 'other';
export const OTHER = '기타세정';
/** 줄을 거르는 조건 — 이 조건으로 LOT 를 걸러 세고, 숫자를 누르면 같은 조건으로 LOT 목록을 보인다. */
export interface Key { group?: string; line?: string; proc?: string; matGroup?: string; matId?: string; oper?: string }
/** 화면 위 조회 조건(칸마다 따로) */
export interface Filters { group: string; line: string; proc: string; matGroup: string; matId: string; desc: string }
export const NO_FILTERS: Filters = { group: '', line: '', proc: '', matGroup: '', matId: '', desc: '' };

export const inScope = (l: LotRow, scope: Scope) => (scope === 'other' ? l.group === OTHER : l.group !== OTHER);
/** 묶음 이름 — 삼성 화면은 구분(MEMORY·FOUNDRY), 기타 화면은 고객사 */
export const secOf = (l: LotRow, scope: Scope) => (scope === 'other' ? (l.customer || '(고객사 없음)') : l.group);
export const matches = (l: LotRow, k: Key, scope: Scope) =>
  (k.group === undefined || secOf(l, scope) === k.group) && (k.line === undefined || (l.line || NO_LINE) === k.line)
  && (k.proc === undefined || l.procName === k.proc) && (k.matGroup === undefined || l.matGroup === k.matGroup)
  && (k.matId === undefined || l.matId === k.matId) && (k.oper === undefined || l.oper === k.oper);
export const passes = (l: LotRow, f: Filters, scope: Scope) => {
  const id = f.matId.trim().toLowerCase(), desc = f.desc.trim().toLowerCase();
  return (!f.group || secOf(l, scope) === f.group) && (!f.line || (l.line || NO_LINE) === f.line) && (!f.proc || l.procName === f.proc)
    && (!f.matGroup || l.matGroup === f.matGroup) && (!id || l.matId.toLowerCase().includes(id)) && (!desc || l.matDesc.toLowerCase().includes(desc));
};
export const cmp = (a: string, b: string) => a.localeCompare(b, 'ko', { numeric: true });

const KEYS = ['group', 'line', 'proc', 'matGroup', 'matId', 'oper'] as const;
const FKEYS = ['group', 'line', 'proc', 'matGroup', 'matId', 'desc'] as const;

/** 숫자 하나의 LOT 목록을 새 창으로 — 주소에 조건만 싣고 새 창이 다시 불러와 같은 조건으로 거른다 */
export function openLotsWindow(scope: Scope, key: Key, f: Filters, label: string) {
  const p = new URLSearchParams({ scope, label });
  KEYS.forEach(k => { if (key[k] !== undefined) p.set(`k.${k}`, key[k]!); });
  FKEYS.forEach(k => { if (f[k].trim()) p.set(`f.${k}`, f[k]); });
  const w = Math.min(1500, screen.availWidth - 40), h = Math.min(860, screen.availHeight - 60);
  window.open(`/mes-report/lots-view?${p}`, '_blank', `width=${w},height=${h},left=${(screen.availWidth - w) / 2},top=30,resizable=yes,scrollbars=yes`);
}

export function readLotsQuery(q: URLSearchParams) {
  const scope: Scope = q.get('scope') === 'other' ? 'other' : 'samsung';
  const key: Key = {};
  KEYS.forEach(k => { const v = q.get(`k.${k}`); if (v !== null) key[k] = v; });
  const f: Filters = { ...NO_FILTERS };
  FKEYS.forEach(k => { f[k] = q.get(`f.${k}`) ?? ''; });
  return { scope, key, f, label: q.get('label') ?? 'LOT 목록' };
}

/** LOT 상세(이력 전체·검사값) 새 창 */
export function openLotDetail(lotId: string) {
  const w = Math.min(1300, screen.availWidth - 40), h = Math.min(860, screen.availHeight - 60);
  window.open(`/mes-report/lot/${encodeURIComponent(lotId)}`, `lot-${lotId}`, `width=${w},height=${h},left=${(screen.availWidth - w) / 2},top=30,resizable=yes,scrollbars=yes`);
}

/** 세정 이력 조회 새 창 — 지금 조건 그대로 사이드바 없이 넓게 */
export function openHistoryWindow(p: URLSearchParams) {
  const w = screen.availWidth - 20, h = screen.availHeight - 40;
  window.open(`/mes-report/history?${p}`, '_blank', `width=${w},height=${h},left=10,top=10,resizable=yes,scrollbars=yes`);
}
