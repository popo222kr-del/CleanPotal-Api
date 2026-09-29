// 데일리 업무보고 — 서버가 모아 준 하루치 기록을 "섹션 → 표/글" 모양으로 바꾼다.
// 화면과 메일 복사가 같은 모양을 쓰므로 둘의 내용이 어긋나지 않는다.
import type { BakeLog, DailyHandoverItem, DailyProdReqItem, DailyReport, WasteLog } from '../../api/types';
import { sortByLine } from './common';

export type Tone = '' | 'bad' | 'warn' | 'ok' | 'dim';
export type Cell = string | { t: string; tone?: Tone };
export type Block =
  | { kind: 'table'; caption?: string; head: string[]; rows: Cell[][]; wide?: number[] }
  | { kind: 'text'; label: string; body: string }
  | { kind: 'facts'; items: { k: string; v: Cell }[] }
  | { kind: 'note'; text: string };
export interface Section { key: string; title: string; link?: string; badge?: { t: string; tone: Tone }; blocks: Block[] }
export interface Alert { t: string; tone: 'bad' | 'warn'; key: string }

const DOW = ['일', '월', '화', '수', '목', '금', '토'];
export const md = (s: string | null | undefined) => (s ? `${Number(s.slice(5, 7))}/${Number(s.slice(8, 10))}` : '');
export const dow = (s: string) => DOW[new Date(s + 'T00:00:00').getDay()];
export function addDays(s: string, n: number) {
  const d = new Date(s + 'T00:00:00'); d.setDate(d.getDate() + n);
  const p = (x: number) => String(x).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}
export function todayYmd() { return localYmd(new Date()); }
function localYmd(t: Date) {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${t.getFullYear()}-${p(t.getMonth() + 1)}-${p(t.getDate())}`;
}
const c = (t: string, tone: Tone = ''): Cell => ({ t, tone });
export const cellText = (x: Cell) => (typeof x === 'string' ? x : x.t);
export const cellTone = (x: Cell): Tone => (typeof x === 'string' ? '' : x.tone ?? '');
const fmt = (v: number | null | undefined) => (v === null || v === undefined ? '-' : String(Number(v.toFixed(3))));
const oneLine = (s: string) => s.split('\n').map(x => x.trim()).filter(Boolean).join(' / ');

// ── 섹션별 ──

function hoTable(caption: string, list: DailyHandoverItem[], when: 'in' | 'out'): Block {
  return {
    kind: 'table', caption: `${caption} ${list.length}건`, head: ['업체', '내용', '담당', when === 'in' ? '출고 예정' : '입고일', '상태'],
    rows: list.map(h => [h.vendor, oneLine(h.content), h.owner, md(when === 'in' ? h.outDate : h.inDate) || '-',
      c(h.status, h.status === '완료' ? 'ok' : '')]), wide: [1],
  };
}

const stateCell = (state: string, ng: number, by: string, pending: boolean): Cell => {
  const ngT = ng > 0 ? ` · NG ${ng}` : '';
  if (state === 'submitted') return c(`제출${by ? ` (${by})` : ''}${ngT}`, ng > 0 ? 'bad' : 'ok');
  if (state === 'progress') return c(`진행 중${ngT}`, ng > 0 ? 'bad' : 'warn');
  if (state === 'na') return c('-', 'dim');
  return c('미점검', pending ? 'dim' : 'bad');
};

function reqRows(list: DailyProdReqItem[], kind: 'new' | 'done' | 'overdue'): Cell[][] {
  return list.map(p => kind === 'new'
    ? [p.category || '-', p.location || '-', oneLine(p.requestDetail), p.requester || '-', md(p.dueDate) || '-']
    : kind === 'done'
      ? [p.category || '-', oneLine(p.requestDetail), oneLine(p.actionDetail) || '-', p.assignee || '-']
      : [p.category || '-', oneLine(p.requestDetail), c(md(p.dueDate), 'bad'), p.assignee || '-']);
}

const hm = (at: string | null) => (at ? at.slice(11, 16) : '?');
const roundMark = (n: number) => '①②③④⑤⑥⑦⑧⑨'[n - 1] ?? String(n);

/** KOH·폐액 하루 합계(주+야) — KOH 는 줄면 사용, 늘면 보충 / 폐액은 늘면 증가, 줄면 수거. */
export function wasteDay(rows: WasteLog[], date: string) {
  const rs = rows.filter(r => r.date.slice(0, 10) === date);
  const pos = (v: number | null) => (v !== null && v > 0 ? v : 0);
  const neg = (v: number | null) => (v !== null && v < 0 ? -v : 0);
  return {
    has: rs.length > 0, rows: rs,
    used: rs.reduce((s, r) => s + pos(r.causticUsed), 0), refill: rs.reduce((s, r) => s + neg(r.causticUsed), 0),
    inc: rs.reduce((s, r) => s + pos(r.wasteIncrease), 0), removed: rs.reduce((s, r) => s + neg(r.wasteIncrease), 0),
    koh: [...rs].reverse().find(r => r.causticAfter !== null)?.causticAfter ?? null,
    waste: [...rs].reverse().find(r => r.wasteAfter !== null)?.wasteAfter ?? null,
  };
}

export function buildDaily(r: DailyReport, today: string): { alerts: Alert[]; sections: Section[] } {
  const date = r.date.slice(0, 10);
  const isToday = date === today;
  const future = date > today;
  const alerts: Alert[] = [];
  const S: Section[] = [];

  // 1. 근무
  if (r.crew || r.dayTeams) {
    const teams = (r.crew ?? []).filter(t => t.production || t.off.length > 0 || t.edu.length > 0);
    const blocks: Block[] = [{ kind: 'facts', items: [
      { k: '주간', v: (r.dayTeams ?? []).join(', ') || '-' },
      { k: '야간', v: (r.nightTeams ?? []).join(', ') || '-' },
    ] }];
    if (teams.length > 0) blocks.push({
      kind: 'table', head: ['팀', '근무', '휴무', '교육'], wide: [2],
      rows: teams.map(t => [t.team,
        [t.day.length ? `주간 ${t.day.length}` : '', t.night.length ? `야간 ${t.night.length}` : ''].filter(Boolean).join(' · ') || '-',
        t.off.join(', ') || '-', t.edu.join(', ') || '-']),
    });
    S.push({ key: 'crew', title: '근무', link: '/calendar', blocks });
  }

  // 2. 체크시트
  if (r.checklist) {
    const z = r.checklist.zones;
    const hour = new Date().getHours();
    // 오늘이면 아직 안 온 교대는 흐리게(주간 08시·야간 20시 전)
    const dayPending = future || (isToday && hour < 8);
    const nightPending = future || isToday;
    const missing = z.filter(x => (x.dayState === 'none' && !dayPending) || (x.nightState === 'none' && !nightPending)).length;
    const ngCount = r.checklist.ngs.length;
    const openNg = r.checklist.ngs.filter(n => n.ngStatus === 'OPEN').length;
    const submitted = z.reduce((s, x) => s + (x.dayState === 'submitted' ? 1 : 0) + (x.nightState === 'submitted' ? 1 : 0), 0);
    const total = z.reduce((s, x) => s + (x.dayState !== 'na' ? 1 : 0) + (x.nightState !== 'na' ? 1 : 0), 0);
    if (missing > 0) alerts.push({ t: `체크시트 미점검 ${missing}`, tone: 'warn', key: 'check' });
    if (openNg > 0) alerts.push({ t: `체크시트 미조치 NG ${openNg}`, tone: 'bad', key: 'check' });
    const blocks: Block[] = [{
      kind: 'table', head: ['라인', '구역', '주간', '야간'],
      rows: z.map(x => [x.line, x.name, stateCell(x.dayState, x.dayNg, x.dayBy, dayPending), stateCell(x.nightState, x.nightNg, x.nightBy, nightPending)]),
    }];
    if (ngCount > 0) blocks.push({
      kind: 'table', caption: `NG ${ngCount}건`, head: ['교대', '구역', '항목', '메모', '조치'], wide: [2, 3],
      rows: r.checklist.ngs.map(n => [n.shift, n.zoneName, n.itemText, oneLine(n.memo) || '-',
        n.ngStatus === 'OPEN' ? c('미조치', 'bad') : c(`조치 완료${n.ngCloseNote ? ` · ${oneLine(n.ngCloseNote)}` : ''}`, 'ok')]),
    });
    if (z.length === 0) blocks.splice(0, 1, { kind: 'note', text: '점검 구역이 없습니다.' });
    S.push({ key: 'check', title: '체크시트', link: '/checklist', blocks,
      badge: { t: `제출 ${submitted}/${total}${ngCount ? ` · NG ${ngCount}` : ''}`, tone: openNg ? 'bad' : missing ? 'warn' : 'ok' } });
  }

  // 3. 생산팀 인수인계
  if (r.meetings) {
    const blocks: Block[] = [];
    for (const m of r.meetings) {
      if (m.dayContent.trim()) blocks.push({ kind: 'text', label: '주간', body: m.dayContent.trim() });
      if (m.nightContent.trim()) blocks.push({ kind: 'text', label: '야간', body: m.nightContent.trim() });
      if (m.officeMemo.trim()) blocks.push({ kind: 'text', label: '사무실 메모', body: m.officeMemo.trim() });
    }
    if (blocks.length === 0) blocks.push({ kind: 'note', text: '이날 작성된 생산팀 인수인계가 없습니다.' });
    S.push({ key: 'meeting', title: '생산팀 인수인계', link: '/meeting', blocks });
  }

  // 4. 기타세정 · 주간세정
  for (const [key, title, link, h] of [['handover', '기타세정', '/handover', r.handover], ['weekly', '주간세정', '/weekly', r.weekly]] as const) {
    if (!h) continue;
    const blocks: Block[] = [];
    if (h.in.length) blocks.push(hoTable('입고', h.in, 'in'));
    if (h.out.length) blocks.push(hoTable('출고', h.out, 'out'));
    if (h.overdue.length) {
      alerts.push({ t: `${title} 출고일 지남 ${h.overdue.length}`, tone: 'bad', key });
      blocks.push({
        kind: 'table', caption: `출고일 지남 ${h.overdue.length}건 (현재 기준)`, head: ['업체', '내용', '담당', '출고 예정', '상태'], wide: [1],
        rows: h.overdue.map(x => [x.vendor, oneLine(x.content), x.owner, c(md(x.outDate), 'bad'), x.status]),
      });
    }
    if (blocks.length === 0) blocks.push({ kind: 'note', text: '이날 입고·출고가 없습니다.' });
    S.push({ key, title, link, blocks, badge: { t: `입고 ${h.in.length} · 출고 ${h.out.length} · 진행 ${h.open}`, tone: h.overdue.length ? 'bad' : '' } });
  }

  // 5. 생산팀 요청사항
  if (r.prodReq) {
    const p = r.prodReq;
    const blocks: Block[] = [];
    if (p.new.length) blocks.push({ kind: 'table', caption: `신규 ${p.new.length}건`, head: ['구분', '위치', '요청 내용', '요청자', '마감'], rows: reqRows(p.new, 'new'), wide: [2] });
    if (p.done.length) blocks.push({ kind: 'table', caption: `처리 완료 ${p.done.length}건`, head: ['구분', '요청 내용', '조치 내용', '담당'], rows: reqRows(p.done, 'done'), wide: [1, 2] });
    if (p.overdue.length) {
      alerts.push({ t: `요청 마감 지남 ${p.overdue.length}`, tone: 'warn', key: 'prodreq' });
      blocks.push({ kind: 'table', caption: `마감 지남 ${p.overdue.length}건 (현재 기준)`, head: ['구분', '요청 내용', '마감', '담당'], rows: reqRows(p.overdue, 'overdue'), wide: [1] });
    }
    if (blocks.length === 0) blocks.push({ kind: 'note', text: '이날 들어오거나 처리한 요청이 없습니다.' });
    S.push({ key: 'prodreq', title: '생산팀 요청사항', link: '/prodreq', blocks });
  }

  // 6. 설비 진행 현황(스케줄 보드)
  if (r.board) {
    const b = r.board;
    const blocks: Block[] = b.equipment.length === 0
      ? [{ kind: 'note', text: '이날 스케줄 보드에 배치된 레시피가 없습니다.' }]
      : [{
        kind: 'table', head: ['설비', '공정', '진행 (07:00 ~ 다음 날 07:00)'], wide: [2],
        rows: b.equipment.map(e => [e.name, e.process || '-',
          e.blocks.map(x => `${x.start}~${x.end}${x.nextDay ? '(+1)' : ''} ${x.recipe}`).join('\n')]),
      }];
    S.push({ key: 'board', title: '설비 진행 현황', link: '/schedule-board', blocks,
      badge: { t: `가동 ${b.equipment.length}대 / ${b.totalEquipment}대${b.idleEquipment ? ` · 유휴 ${b.idleEquipment}` : ''}`, tone: '' } });
  }

  // 7. 약액 교체
  if (r.chemical) {
    const rows = sortByLine(r.chemical.rows.filter(x => x.kind !== 'BAKE' && x.content));
    S.push({ key: 'chemical', title: '약액 교체', link: '/work/chemical',
      badge: { t: `${rows.length}대`, tone: '' },
      blocks: rows.length === 0 ? [{ kind: 'note', text: '이날 약액 교체가 없습니다.' }] : [{
        kind: 'table', head: ['설비', '공정', '교체 내용', '메모'], wide: [3],
        rows: rows.map(x => [x.code, x.process || '-', c(x.content, 'ok'), oneLine(x.note) || '-']),
      }] });
  }

  // 8. KOH·폐액
  if (r.waste) {
    const cur = wasteDay(r.waste.rows, date);
    const prev = wasteDay(r.waste.rows, addDays(date, -1));
    const diff = (a: number, b: number): Cell => {
      if (!cur.has || !prev.has) return c('-', 'dim');
      const d = Number((a - b).toFixed(3));
      return d === 0 ? c('0', 'dim') : c(`${d > 0 ? '▲' : '▼'} ${fmt(Math.abs(d))}`, d > 0 ? 'warn' : 'ok');
    };
    const blocks: Block[] = [];
    if (!cur.has) blocks.push({ kind: 'note', text: '이날 KOH·폐액 기록이 없습니다.' });
    else {
      blocks.push({
        kind: 'table', head: ['구분', `전날 ${md(addDays(date, -1))}`, `오늘 ${md(date)}`, '증감'],
        rows: [
          ['KOH 사용', prev.has ? fmt(prev.used) : '-', fmt(cur.used), diff(cur.used, prev.used)],
          ['폐액 증가', prev.has ? fmt(prev.inc) : '-', fmt(cur.inc), diff(cur.inc, prev.inc)],
          ...(cur.refill || prev.refill ? [['KOH 보충', fmt(prev.refill), fmt(cur.refill), '']] as Cell[][] : []),
          ...(cur.removed || prev.removed ? [['폐액 수거', fmt(prev.removed), fmt(cur.removed), '']] as Cell[][] : []),
          ['잔량 KOH', fmt(prev.koh), fmt(cur.koh), ''],
          ['잔량 폐액', fmt(prev.waste), fmt(cur.waste), ''],
        ],
      });
      blocks.push({
        kind: 'table', caption: '교대별', head: ['교대', 'KOH 前→現', '사용', '폐액 前→現', '증가', '교체 설비', '비고'], wide: [6],
        rows: cur.rows.map(w => [w.shift, `${fmt(w.causticBefore)}→${fmt(w.causticAfter)}`, fmt(w.causticUsed),
          `${fmt(w.wasteBefore)}→${fmt(w.wasteAfter)}`, fmt(w.wasteIncrease),
          [w.dipEquipment, w.sprayEquipment].filter(Boolean).join(', ') || '-', oneLine(w.note) || '-']),
      });
    }
    S.push({ key: 'waste', title: 'KOH·폐액', link: '/work/waste', blocks });
  }

  // 9. BAKE 그을음 — 가동한 회차만 표로, HOLD·비가동은 교대별 오븐 이름만 한 줄로(표가 수십 줄이 되지 않게)
  if (r.bake) {
    const logs: BakeLog[] = r.bake;
    const issues = logs.filter(l => l.hasSoot || l.hasQuartz);
    if (issues.length) alerts.push({ t: `BAKE 그을음·Q'TZ ${issues.length}`, tone: 'bad', key: 'bake' });
    const run = logs.filter(l => !l.status);
    const idle = new Map<string, string[]>();
    for (const l of logs.filter(x => x.status)) {
      const k = `${l.shift} ${l.status}`;
      const list = idle.get(k) ?? [];
      if (!list.includes(l.eqCode)) list.push(l.eqCode);
      idle.set(k, list);
    }
    const blocks: Block[] = [];
    if (logs.length === 0) blocks.push({ kind: 'note', text: '이날 그을음 기록이 없습니다.' });
    if (run.length) blocks.push({
      kind: 'table', head: ['교대', '오븐', '가동', '품명 (S/N)', '그을음', "Q'TZ", '비고'], wide: [3],
      rows: run.map(l => [`${l.shift}${roundMark(l.round)}`, l.eqCode, `${hm(l.trackIn)}→${hm(l.trackOut)}`,
        `${l.item}${l.serialNo ? ` (${l.serialNo})` : ''}`,
        c(l.soot || '-', l.hasSoot ? 'bad' : ''), c(l.quartz || '-', l.hasQuartz ? 'bad' : ''), oneLine(l.note) || '']),
    });
    if (idle.size) blocks.push({ kind: 'facts', items: [...idle].map(([k, eqs]) => ({ k, v: c(eqs.join(', '), 'dim') })) });
    S.push({ key: 'bake', title: 'BAKE 그을음', link: '/work/bake',
      badge: { t: `가동 ${run.length}회${issues.length ? ` · 이상 ${issues.length}` : ''}`, tone: issues.length ? 'bad' : '' },
      blocks });
  }

  // 10. BROKEN
  if (r.broken) {
    if (r.broken.length) alerts.push({ t: `BROKEN ${r.broken.length}건`, tone: 'bad', key: 'broken' });
    S.push({ key: 'broken', title: 'BROKEN', link: '/broken',
      blocks: r.broken.length === 0 ? [{ kind: 'note', text: '이날 발생한 BROKEN 이 없습니다.' }] : [{
        kind: 'table', head: ['라인', '제품', 'S/N', '공정', '내용', '상태'], wide: [4],
        rows: r.broken.map(b => [b.line, b.productName, b.sn || '-', b.occurStage || '-', oneLine(b.description) || '-',
          c(`${b.status}${b.isOfficial ? ' · 공식' : ''}`, b.status === '완료' ? 'ok' : 'warn')]),
      }] });
  }

  // 11. 폐기품
  if (r.scrap) {
    S.push({ key: 'scrap', title: '폐기품', link: '/work/scrap',
      blocks: r.scrap.length === 0 ? [{ kind: 'note', text: '이날 등록한 폐기 LIST 가 없습니다.' }] : [{
        kind: 'table', head: ['LIST', '수량', '상차', '라인'],
        rows: r.scrap.map(s => [s.title || '-', `${s.items}`, s.isClosed ? c('상차 완료', 'ok') : `${s.loaded}/${s.items}`, s.lines.join(', ') || '-']),
      }] });
  }

  // 12. ICP-MS — 설비가 스무 대가 넘어 다 싣지 않는다. 값이 높은 5대 + 특이사항을 적은 설비만.
  if (r.icpms) {
    const measured = r.icpms.filter(x => x.measured);
    const top = [...measured].sort((a, b) => b.topValue - a.topValue).slice(0, 5);
    const shown = [...top, ...r.icpms.filter(x => x.note.trim() && !top.includes(x))];
    const max = top[0];
    S.push({ key: 'icpms', title: 'ICP-MS', link: '/icpms',
      badge: measured.length ? { t: `측정 ${measured.length}대`, tone: '' } : undefined,
      blocks: r.icpms.length === 0 ? [{ kind: 'note', text: '이날 측정 기록이 없습니다.' }] : [
        ...(max ? [{ kind: 'facts', items: [{ k: '가장 높은 값', v: `${max.eqId} ${max.topElement} ${fmt(max.topValue)}` }] } as Block] : []),
        {
          kind: 'table', caption: '값이 높은 설비 · 특이사항', head: ['설비', '공정', '가장 높은 원소', '특이사항'], wide: [3],
          rows: shown.map(x => [x.eqId, x.process || '-', x.measured && x.topElement ? `${x.topElement} ${fmt(x.topValue)}` : '-', oneLine(x.note) || '-']),
        }] });
  }

  // 13. 배차
  if (r.dispatch) {
    S.push({ key: 'dispatch', title: '배차', link: '/dispatch',
      blocks: r.dispatch.length === 0 ? [{ kind: 'note', text: '이날 배차가 없습니다.' }] : [{
        kind: 'table', head: ['업체', '출고', '입고'], wide: [1, 2],
        rows: r.dispatch.map(d => [d.vendorName, oneLine(d.outgoing) || '-', oneLine(d.incoming) || '-']),
      }] });
  }

  // 14. 다음 날 예정
  {
    const next = addDays(date, 1);
    const outs = [...(r.handover?.tomorrow ?? []).map(h => ({ ...h, kind: '기타세정' })), ...(r.weekly?.tomorrow ?? []).map(h => ({ ...h, kind: '주간세정' }))];
    const blocks: Block[] = [];
    if (outs.length) blocks.push({
      kind: 'table', caption: `출고 예정 ${outs.length}건`, head: ['구분', '업체', '내용', '담당', '상태'], wide: [2],
      rows: outs.map(h => [h.kind, h.vendor, oneLine(h.content), h.owner, h.status]),
    });
    const facts: { k: string; v: Cell }[] = [];
    if (r.tomorrow?.edu.length) facts.push({ k: '교육', v: r.tomorrow.edu.join(', ') });
    if (r.tomorrow?.events.length) facts.push({ k: '팀 일정', v: r.tomorrow.events.join(', ') });
    if (facts.length) blocks.push({ kind: 'facts', items: facts });
    if (blocks.length === 0) blocks.push({ kind: 'note', text: '예정된 출고·교육·일정이 없습니다.' });
    S.push({ key: 'tomorrow', title: `다음 날 예정 (${md(next)} ${dow(next)})`, blocks });
  }

  return { alerts, sections: S };
}

// ── 메일 복사 — 메일 본문에 붙여도 모양이 유지되게 스타일을 태그마다 직접 붙인다 ──

const esc = (s: string) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
const br = (s: string) => esc(s).replace(/\n/g, '<br>');
const TONE_COLOR: Record<Tone, string> = { '': '#222', bad: '#c92a2a', warn: '#b35c00', ok: '#2b8a3e', dim: '#999' };
const FONT = "font-family:'Malgun Gothic','맑은 고딕',Arial,sans-serif";

export function mailHtml(r: DailyReport, model: { alerts: Alert[]; sections: Section[] }): string {
  const date = r.date.slice(0, 10);
  const td = `border:1px solid #cfd4dc;padding:4px 8px;vertical-align:top;font-size:12px`;
  const th = `${td};background:#eef1f6;font-weight:bold;text-align:left;white-space:nowrap`;
  const out: string[] = [];
  out.push(`<div style="${FONT};font-size:13px;color:#222;line-height:1.5">`);
  out.push(`<div style="font-size:18px;font-weight:bold;margin:0 0 2px">업무보고 ${md(date)}(${dow(date)})</div>`);
  out.push(`<div style="color:#666;font-size:12px;margin:0 0 10px">${md(date)} 주간 + 야간(${md(addDays(date, 1))} 아침까지)</div>`);
  out.push(model.alerts.length
    ? `<div style="margin:0 0 12px">${model.alerts.map(a => `<span style="display:inline-block;margin:0 6px 4px 0;padding:2px 8px;border-radius:10px;font-size:12px;font-weight:bold;color:#fff;background:${a.tone === 'bad' ? '#e03131' : '#e8590c'}">${esc(a.t)}</span>`).join('')}</div>`
    : `<div style="margin:0 0 12px;color:#2b8a3e;font-weight:bold">특이 이상 없음</div>`);
  model.sections.forEach((s, i) => {
    out.push(`<div style="font-size:14px;font-weight:bold;margin:16px 0 6px;padding-left:6px;border-left:4px solid #3b5bdb">${i + 1}. ${esc(s.title)}${s.badge ? ` <span style="font-weight:normal;font-size:12px;color:${TONE_COLOR[s.badge.tone]}">${esc(s.badge.t)}</span>` : ''}</div>`);
    for (const b of s.blocks) {
      if (b.kind === 'note') out.push(`<div style="color:#999;font-size:12px;margin:0 0 6px">${esc(b.text)}</div>`);
      else if (b.kind === 'text') out.push(`<div style="margin:0 0 8px"><div style="font-weight:bold;font-size:12px;color:#555">${esc(b.label)}</div><div style="font-size:12px;padding:4px 8px;border-left:2px solid #dee2e6">${br(b.body)}</div></div>`);
      else if (b.kind === 'facts') out.push(`<div style="margin:0 0 6px;font-size:12px">${b.items.map(f => `<b>${esc(f.k)}</b> <span style="color:${TONE_COLOR[cellTone(f.v)]}">${esc(cellText(f.v))}</span>`).join(' &nbsp;·&nbsp; ')}</div>`);
      else {
        if (b.caption) out.push(`<div style="font-size:12px;font-weight:bold;color:#555;margin:6px 0 3px">${esc(b.caption)}</div>`);
        out.push(`<table style="border-collapse:collapse;margin:0 0 8px;${FONT}"><tr>${b.head.map(h => `<th style="${th}">${esc(h)}</th>`).join('')}</tr>`);
        for (const row of b.rows)
          out.push(`<tr>${row.map(x => `<td style="${td};color:${TONE_COLOR[cellTone(x)]}${cellTone(x) === 'bad' ? ';font-weight:bold' : ''}">${br(cellText(x)) || '&nbsp;'}</td>`).join('')}</tr>`);
        out.push('</table>');
      }
    }
  });
  out.push('</div>');
  return out.join('');
}

/** 서식 없는 메일(텍스트)로 붙을 때 쓰는 글 모양. */
export function mailText(r: DailyReport, model: { alerts: Alert[]; sections: Section[] }): string {
  const date = r.date.slice(0, 10);
  const lines: string[] = [`업무보고 ${md(date)}(${dow(date)}) — ${md(date)} 주간 + 야간(${md(addDays(date, 1))} 아침까지)`];
  lines.push(model.alerts.length ? `이상: ${model.alerts.map(a => a.t).join(', ')}` : '특이 이상 없음');
  model.sections.forEach((s, i) => {
    lines.push('', `■ ${i + 1}. ${s.title}${s.badge ? ` (${s.badge.t})` : ''}`);
    for (const b of s.blocks) {
      if (b.kind === 'note') lines.push(`  ${b.text}`);
      else if (b.kind === 'text') lines.push(`  [${b.label}]`, ...b.body.split('\n').map(x => `    ${x}`));
      else if (b.kind === 'facts') lines.push(`  ${b.items.map(f => `${f.k}: ${cellText(f.v)}`).join(' / ')}`);
      else {
        if (b.caption) lines.push(`  - ${b.caption}`);
        for (const row of b.rows) lines.push(`    ${row.map(x => cellText(x).replace(/\n/g, ', ')).join(' | ')}`);
      }
    }
  });
  return lines.join('\n');
}
