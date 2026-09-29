// Daily 업무 보고 — 서버가 모아 준 하루치 기록을 "요약 숫자 + 섹션 → 블록" 모양으로 바꾼다.
// 화면과 메일 복사가 같은 모양을 쓰므로 둘의 내용이 어긋나지 않는다.
// 메일은 한 줄(단일 열)로 — 좁은 칸에서 글자가 어색하게 끊기지 않게 하고, 한글은 단어 단위로만 줄을 바꾼다.
import type { BakeLog, DailyBoardEq, DailyCrewTeam, DailyHandover, DailyMeeting, DailyReport, WasteLog } from '../../api/types';
import { sortByLine } from './common';
import type { BoardShift } from './boardImage';

export type Tone = '' | 'bad' | 'warn' | 'ok' | 'dim' | 'info';
export type Cell = string | { t: string; tone?: Tone; pill?: boolean; group?: boolean };   // group: 표 전체 폭 묶음 제목 줄
export interface TeamCard {
  name: string; shift: string; working: number; total: number;
  names: { n: string; t: string }[]; off: string[]; edu: string[];
}
export type Block =
  | { kind: 'table'; caption?: string; head: string[]; rows: Cell[][]; wide?: number[]; center?: number[]; stack?: boolean }
  | { kind: 'teams'; teams: TeamCard[]; tenure: { label: string; n: number }[]; tenureTitle: string; others: { k: string; v: string }[] }
  | { kind: 'cols'; cols: { label: string; tone: Tone; body: string }[] }
  | { kind: 'text'; label: string; body: string }
  | { kind: 'facts'; items: { k: string; v: Cell }[] }
  | { kind: 'board'; equipment: DailyBoardEq[]; date: string; shift: BoardShift }
  | { kind: 'note'; text: string };
export interface Section { key: string; title: string; link?: string; half?: boolean; badge?: { t: string; tone: Tone }; blocks: Block[] }
export interface Kpi { label: string; value: string; sub?: string; tone?: Tone }
export interface DailyModel { date: string; dateLabel: string; shiftLabel: string; kpis: Kpi[]; sections: Section[] }

const DOW = ['일', '월', '화', '수', '목', '금', '토'];
export const md = (s: string | null | undefined) => (s ? `${Number(s.slice(5, 7))}/${Number(s.slice(8, 10))}` : '');
export const dow = (s: string) => DOW[new Date(s + 'T00:00:00').getDay()];
export function addDays(s: string, n: number) {
  const d = new Date(s + 'T00:00:00'); d.setDate(d.getDate() + n);
  const p = (x: number) => String(x).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}
export function todayYmd() {
  const t = new Date(); const p = (n: number) => String(n).padStart(2, '0');
  return `${t.getFullYear()}-${p(t.getMonth() + 1)}-${p(t.getDate())}`;
}
const c = (t: string, tone: Tone = ''): Cell => ({ t, tone });
const pill = (t: string, tone: Tone): Cell => ({ t, tone, pill: true });
export const cellText = (x: Cell) => (typeof x === 'string' ? x : x.t);
export const cellTone = (x: Cell): Tone => (typeof x === 'string' ? '' : x.tone ?? '');
export const cellPill = (x: Cell) => typeof x !== 'string' && !!x.pill;
export const isGroupRow = (row: Cell[]) => row.length === 1 && typeof row[0] !== 'string' && !!row[0].group;
const fmt = (v: number | null | undefined) => (v === null || v === undefined ? '-' : String(Number(v.toFixed(3))));
const oneLine = (s: string) => s.split('\n').map(x => x.trim()).filter(Boolean).join(' / ');

/** 근속 표기 — 1년이 안 되면 개월, 넘으면 년. */
export const tenure = (m: number | null | undefined) => (m === null || m === undefined ? '' : m < 12 ? `${m}개월` : `${Math.floor(m / 12)}년`);
const TENURE_BUCKETS = ['1년 미만', '1년', '2년', '3년', '4년', '5년 이상'];
export const shiftTone = (s: string): Tone => (s === '주간' ? 'warn' : s === '야간' ? 'info' : 'dim');

// ── 섹션별 ──

function crewSection(crew: DailyCrewTeam[]): Section {
  const months = new Map<string, number | null>();
  for (const t of crew) for (const m of t.members) months.set(m.name, m.tenureMonths);
  // 교대 팀(1팀·2팀) 먼저, 그다음 주간팀 같은 교대 없는 생산팀
  const prod = crew.filter(t => t.production).sort((a, b) => Number(b.hasShift) - Number(a.hasShift));
  if (prod.length === 0) return { key: 'crew', title: '근무 현황', link: '/calendar', blocks: [{ kind: 'note', text: '생산팀이 없습니다(조직 관리에서 생산팀을 지정하세요).' }] };
  const counts = TENURE_BUCKETS.map(() => 0);
  let unknown = 0;
  for (const t of prod) for (const m of t.members) {
    if (m.tenureMonths === null) unknown++;
    else counts[Math.min(5, Math.floor(m.tenureMonths / 12))]++;
  }
  const others = crew.filter(t => !t.production).flatMap(t => [
    ...(t.off.length ? [{ k: `${t.team} 휴무`, v: t.off.join(', ') }] : []),
    ...(t.edu.length ? [{ k: `${t.team} 교육`, v: t.edu.join(', ') }] : []),
  ]);
  const total = prod.reduce((s, t) => s + t.members.length, 0);
  return {
    key: 'crew', title: '근무 현황', link: '/calendar',
    blocks: [{
      kind: 'teams', others,
      teams: prod.map(t => ({
        name: t.team, shift: t.shift || '-', working: t.day.length + t.night.length, total: t.members.length,
        names: [...t.day, ...t.night].map(n => ({ n, t: tenure(months.get(n)) })), off: t.off, edu: t.edu,
      })),
      tenureTitle: `근속별 인원 · 생산직 ${total}명`,
      tenure: [...TENURE_BUCKETS.map((label, i) => ({ label, n: counts[i] })), ...(unknown ? [{ label: '입사일 미입력', n: unknown }] : [])],
    }],
  };
}

const stateCell = (state: string, ng: number, by: string, pending: boolean): Cell => {
  const ngT = ng > 0 ? ` · NG ${ng}` : '';
  if (state === 'submitted') return pill(`제출${by ? ` ${by}` : ''}${ngT}`, ng > 0 ? 'bad' : 'ok');
  if (state === 'progress') return pill(`진행 중${ngT}`, ng > 0 ? 'bad' : 'warn');
  if (state === 'na') return c('-', 'dim');
  return pending ? c('대기', 'dim') : pill('미점검', 'bad');
};

function handoverSection(key: string, title: string, link: string, h: DailyHandover): Section {
  const rows: Cell[][] = [
    ...h.in.map(x => [pill('입고', 'info'), x.vendor, oneLine(x.content), `${md(x.inDate)} → ${md(x.outDate) || '미정'}`, x.status]),
    ...h.out.map(x => [pill('출고', 'ok'), x.vendor, oneLine(x.content), `${md(x.inDate) || '-'} → ${md(x.outDate)}`, x.status]),
    ...h.overdue.map(x => [pill('지연', 'bad'), x.vendor, oneLine(x.content), c(`${md(x.inDate) || '-'} → ${md(x.outDate)}`, 'bad'), x.status]),
  ];
  return {
    key, title, link, half: true,
    badge: { t: `입고 ${h.in.length} · 출고 ${h.out.length} · 진행 ${h.open}`, tone: '' },
    blocks: rows.length === 0 ? [{ kind: 'note', text: '이날 입고·출고가 없습니다.' }]
      : [{ kind: 'table', head: ['', '업체', '내용', '입고 → 출고', '상태'], rows, wide: [2], center: [0, 3, 4], stack: true }],
  };
}

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

/** boardShift: 스케줄 보드 그림을 주간/야간 중 어느 쪽으로 넣을지(보내는 시간대). */
export function buildDaily(r: DailyReport, today: string, boardShift: BoardShift): DailyModel {
  const date = r.date.slice(0, 10);
  const isToday = date === today;
  const future = date > today;
  const kpis: Kpi[] = [];
  const S: Section[] = [];

  const prodTeams = (r.crew ?? []).filter(t => t.production);
  const shiftTeams = prodTeams.filter(t => t.hasShift);
  const dayT = shiftTeams.filter(t => t.day.length > 0).map(t => t.team).join(', ') || '-';
  const nightT = shiftTeams.filter(t => t.night.length > 0).map(t => t.team).join(', ') || '-';
  const d = new Date(date + 'T00:00:00');
  const dateLabel = `${d.getFullYear()}년 ${d.getMonth() + 1}월 ${d.getDate()}일 (${dow(date)})`;
  const shiftLabel = `주간 ${dayT} · 야간 ${nightT}`;

  // 근무 현황
  if (r.crew) {
    S.push(crewSection(r.crew));
    if (prodTeams.length) kpis.push({
      label: '근무 인원', value: `${prodTeams.reduce((s, t) => s + t.day.length + t.night.length, 0)} / ${prodTeams.reduce((s, t) => s + t.members.length, 0)}`,
      sub: `휴무 ${prodTeams.reduce((s, t) => s + t.off.length, 0)} · 교육 ${prodTeams.reduce((s, t) => s + t.edu.length, 0)}`,
    });
  }

  // 인수인계 · 특이사항 — 주간·야간을 나란히
  if (r.meetings) {
    const join = (f: (m: DailyMeeting) => string) => r.meetings!.map(f).map(x => x.trim()).filter(Boolean).join('\n\n');
    const day = join(m => m.dayContent), night = join(m => m.nightContent), memo = join(m => m.officeMemo);
    const blocks: Block[] = !day && !night && !memo
      ? [{ kind: 'note', text: '이날 작성된 생산팀 인수인계가 없습니다.' }]
      : [{ kind: 'cols', cols: [{ label: `주간 · ${dayT}`, tone: 'warn', body: day || '-' }, { label: `야간 · ${nightT}`, tone: 'info', body: night || '-' }] },
        ...(memo ? [{ kind: 'text', label: '사무실 메모', body: memo } as Block] : [])];
    S.push({ key: 'meeting', title: '인수인계 · 특이사항', link: '/meeting', blocks });
  }

  // 체크시트 — 구역별 주간·야간
  if (r.checklist) {
    const z = r.checklist.zones;
    const hour = new Date().getHours();
    const dayPending = future || (isToday && hour < 8);       // 아직 안 온 교대는 흐리게
    const nightPending = future || isToday;
    const missing = z.filter(x => (x.dayState === 'none' && !dayPending) || (x.nightState === 'none' && !nightPending)).length;
    const ngs = r.checklist.ngs;
    const openNg = ngs.filter(n => n.ngStatus === 'OPEN').length;
    const submitted = z.reduce((s, x) => s + (x.dayState === 'submitted' ? 1 : 0) + (x.nightState === 'submitted' ? 1 : 0), 0);
    const total = z.reduce((s, x) => s + (x.dayState !== 'na' ? 1 : 0) + (x.nightState !== 'na' ? 1 : 0), 0);
    kpis.push({ label: '체크시트 제출', value: `${submitted} / ${total}`, sub: ngs.length ? `NG ${ngs.length}건` : missing ? `미점검 ${missing}` : '이상 없음', tone: openNg ? 'bad' : missing ? 'warn' : 'ok' });
    const blocks: Block[] = z.length === 0 ? [{ kind: 'note', text: '점검 구역이 없습니다.' }] : [{
      // 라인(METAL·N-METAL)은 묶음 제목 줄 한 번만 — 줄마다 라인 이름을 되풀이하지 않는다
      kind: 'table', head: ['구역', '주간', '야간'], center: [1, 2],
      rows: z.flatMap((x, i) => [
        ...(i === 0 || z[i - 1].line !== x.line ? [[{ t: x.line || '공통', group: true }] as Cell[]] : []),
        [x.name, stateCell(x.dayState, x.dayNg, x.dayBy, dayPending), stateCell(x.nightState, x.nightNg, x.nightBy, nightPending)],
      ]),
    }];
    if (ngs.length) blocks.push({
      kind: 'table', caption: `NG ${ngs.length}건`, head: ['교대', '구역', '항목', '메모', '조치'], wide: [2, 3], stack: true,
      rows: ngs.map(n => [n.shift, n.zoneName, n.itemText, oneLine(n.memo) || '-',
        n.ngStatus === 'OPEN' ? pill('미조치', 'bad') : c(`조치 완료${n.ngCloseNote ? ` · ${oneLine(n.ngCloseNote)}` : ''}`, 'ok')]),
    });
    S.push({ key: 'check', title: '체크시트', link: '/checklist', blocks,
      badge: { t: `제출 ${submitted}/${total}`, tone: openNg ? 'bad' : missing ? 'warn' : 'ok' } });
  }

  // 설비 진행 현황 — 스케줄 보드 그림(주간·야간)
  if (r.board) {
    const b = r.board;
    const running = b.equipment.filter(e => e.blocks.length > 0).length;
    kpis.push({ label: '설비 가동', value: `${running} / ${b.totalEquipment}`, sub: b.idleEquipment ? `유휴 ${b.idleEquipment}대` : '스케줄 보드' });
    S.push({ key: 'board', title: '설비 진행 현황', link: '/schedule-board',
      badge: { t: `가동 ${running}대 / ${b.totalEquipment}대`, tone: '' },
      blocks: b.equipment.length === 0 ? [{ kind: 'note', text: '스케줄 보드에 등록된 설비가 없습니다.' }]
        : [{ kind: 'board', equipment: b.equipment, date, shift: boardShift }] });
  }

  // 기타세정 · 주간세정
  if (r.handover) S.push(handoverSection('handover', '기타세정', '/handover', r.handover));
  if (r.weekly) S.push(handoverSection('weekly', '주간세정', '/weekly', r.weekly));

  // 생산팀 요청사항
  if (r.prodReq) {
    const p = r.prodReq;
    const rows: Cell[][] = [
      ...p.new.map(x => [pill('신규', 'info'), x.category || '-', oneLine(x.requestDetail), `마감 ${md(x.dueDate) || '-'}`, x.requester || '-']),
      ...p.done.map(x => [pill('처리', 'ok'), x.category || '-', oneLine(x.requestDetail), oneLine(x.actionDetail) || '-', x.assignee || '-']),
      ...p.overdue.map(x => [pill('마감 지남', 'bad'), x.category || '-', oneLine(x.requestDetail), c(`마감 ${md(x.dueDate)}`, 'bad'), x.assignee || '-']),
    ];
    S.push({ key: 'prodreq', title: '생산팀 요청사항', link: '/prodreq',
      badge: { t: `신규 ${p.new.length} · 처리 ${p.done.length}`, tone: '' },
      blocks: rows.length === 0 ? [{ kind: 'note', text: '이날 들어오거나 처리한 요청이 없습니다.' }]
        : [{ kind: 'table', head: ['', '분류', '요청 내용', '조치 / 마감', '담당'], rows, wide: [2, 3], center: [0, 1, 4], stack: true }] });
  }

  // 약액 교체 · KOH·폐액
  if (r.chemical) {
    const rows = sortByLine(r.chemical.rows.filter(x => x.kind !== 'BAKE' && x.content));
    kpis.push({ label: '약액 교체', value: `${rows.length}대`, sub: rows.map(x => x.code).slice(0, 3).join(', ') + (rows.length > 3 ? ' 외' : '') || '없음' });
    S.push({ key: 'chemical', title: '약액 교체', link: '/work/chemical', half: true,
      badge: { t: `${rows.length}대`, tone: '' },
      blocks: rows.length === 0 ? [{ kind: 'note', text: '이날 약액 교체가 없습니다.' }] : [{
        kind: 'table', head: ['설비', '공정', '교체 내용', '메모'], wide: [3],
        rows: rows.map(x => [x.code, c(x.process || '-', 'dim'), c(x.content, 'ok'), oneLine(x.note) || '-']),
      }] });
  }
  if (r.waste) {
    const cur = wasteDay(r.waste.rows, date);
    const prev = wasteDay(r.waste.rows, addDays(date, -1));
    if (cur.has) kpis.push({ label: 'KOH 사용 · 폐액 증가', value: `${fmt(cur.used)} · ${fmt(cur.inc)}`, sub: `전날 ${prev.has ? `${fmt(prev.used)} · ${fmt(prev.inc)}` : '기록 없음'}` });
    const v = (t: typeof cur, n: number | null) => (t.has ? fmt(n) : '-');
    const blocks: Block[] = !cur.has && !prev.has ? [{ kind: 'note', text: '전날·당일 KOH·폐액 기록이 없습니다.' }] : [{
      kind: 'table', head: ['', `전날 ${md(addDays(date, -1))}`, `당일 ${md(date)}`], center: [1, 2],
      rows: [
        ['KOH 사용', v(prev, prev.used), v(cur, cur.used)],
        ['폐액 증가', v(prev, prev.inc), v(cur, cur.inc)],
        ...(cur.refill || prev.refill ? [['KOH 보충', v(prev, prev.refill), v(cur, cur.refill)]] : []),
        ...(cur.removed || prev.removed ? [['폐액 수거', v(prev, prev.removed), v(cur, cur.removed)]] : []),
        ['잔량 KOH', v(prev, prev.koh), v(cur, cur.koh)],
        ['잔량 폐액', v(prev, prev.waste), v(cur, cur.waste)],
      ],
    }];
    S.push({ key: 'waste', title: 'KOH · 폐액', link: '/work/waste', half: true, blocks });
  }

  // BAKE 그을음 — 오븐을 모두 나열하고 교대별로 가동한 것만 표시, 특이사항은 이상(그을음·Q'TZ·비고)이 있을 때만
  if (r.bake) {
    const logs: BakeLog[] = r.bake;
    const ovens = [...new Set([
      ...(r.chemical?.rows ?? []).filter(x => x.kind === 'BAKE').map(x => x.code),
      ...logs.map(l => l.eqCode),
    ])];
    const issues = logs.filter(l => l.hasSoot || l.hasQuartz);
    const ranOvens = ovens.filter(o => logs.some(l => l.eqCode === o && !l.status)).length;
    kpis.push({ label: 'BAKE 가동', value: `${ranOvens} / ${ovens.length}대`, sub: issues.length ? `그을음·Q'TZ ${issues.length}건` : '이상 없음', tone: issues.length ? 'bad' : 'ok' });
    const runCell = (o: string, shift: string): Cell => {
      const runs = logs.filter(l => l.eqCode === o && l.shift === shift && !l.status);
      if (runs.length === 0) return c('', 'dim');
      const bad = runs.some(l => l.hasSoot || l.hasQuartz);
      return pill(runs.length > 1 ? `가동 ${runs.length}회` : '가동', bad ? 'bad' : 'ok');
    };
    const remark = (o: string) => logs.filter(l => l.eqCode === o && !l.status).flatMap(l => {
      const p: string[] = [];
      if (l.hasSoot) p.push(`그을음 ${l.soot}`);
      if (l.hasQuartz) p.push(`Q'TZ ${l.quartz}`);
      const note = oneLine(l.note);
      if (note && note !== '정상') p.push(note);
      return p.length ? [`${l.shift}${roundMark(l.round)} ${p.join(', ')}`] : [];
    }).join(' / ');
    const shifts = [...new Set(logs.map(l => l.shift))].sort((a, b) => (a === '야' ? 1 : 0) - (b === '야' ? 1 : 0));
    const cols = shifts.length ? shifts : ['주', '야'];
    S.push({ key: 'bake', title: 'BAKE 그을음', link: '/work/bake',
      badge: { t: `가동 ${ranOvens}대 / ${ovens.length}대${issues.length ? ` · 이상 ${issues.length}` : ''}`, tone: issues.length ? 'bad' : '' },
      blocks: ovens.length === 0 ? [{ kind: 'note', text: '등록된 BAKE 오븐이 없습니다.' }] : [{
        kind: 'table', head: ['오븐', ...cols.map(x => `${x}간`), '특이사항'], center: cols.map((_, i) => i + 1), wide: [cols.length + 1],
        rows: ovens.map(o => { const rm = remark(o); return [o, ...cols.map(x => runCell(o, x)), rm ? c(rm, 'bad') : '']; }),
      }] });
  }

  // 반쪽 섹션이 짝 없이 혼자 남으면 한 줄 전체를 쓴다
  for (let i = 0; i < S.length; i++) {
    if (!S[i].half) continue;
    if (S[i + 1]?.half) { i++; continue; }
    S[i] = { ...S[i], half: false };
  }
  return { date, dateLabel, shiftLabel, kpis, sections: S };
}

// ── 메일 복사 ──
// 메일 편집기는 <style> 을 버리므로 스타일을 태그마다 붙이고, 레이아웃은 표로 잡는다(한 열, 폭 760).

const esc = (s: string) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
const br = (s: string) => esc(s).replace(/\n/g, '<br>');
export const TONE_TEXT: Record<Tone, string> = { '': '#1F2937', bad: '#C92A2A', warn: '#B45309', ok: '#2F7D4A', dim: '#8A94A6', info: '#3B5BDB' };
export const TONE_BG: Record<Tone, string> = { '': '#F3F4F6', bad: '#FDECEC', warn: '#FFF3E0', ok: '#E9F7EF', dim: '#F3F4F6', info: '#EDF2FF' };
const FONT = "font-family:'Malgun Gothic','맑은 고딕','Apple SD Gothic Neo',sans-serif";
const KEEP = 'word-break:keep-all;overflow-wrap:break-word';
const ACCENT = '#335BFF';

const pillHtml = (t: string, tone: Tone) =>
  `<span style="display:inline-block;padding:1px 8px;border-radius:10px;background:${TONE_BG[tone]};color:${TONE_TEXT[tone]};font-size:11.5px;font-weight:bold;white-space:nowrap">${esc(t)}</span>`;

function cellHtml(x: Cell) {
  if (cellPill(x)) return pillHtml(cellText(x), cellTone(x));
  const tone = cellTone(x);
  return `<span style="color:${TONE_TEXT[tone]}${tone === 'bad' ? ';font-weight:bold' : ''}">${br(cellText(x)) || '&nbsp;'}</span>`;
}

function blockHtml(b: Block, images: Record<string, string>): string {
  if (b.kind === 'note') return `<p style="margin:4px 0 0;color:#8A94A6;font-size:12.5px">${esc(b.text)}</p>`;
  if (b.kind === 'text') return `<p style="margin:10px 0 0;font-size:12.5px;color:#1F2937;${KEEP}"><b style="color:#4B5563">${esc(b.label)}</b><br>${br(b.body)}</p>`;
  if (b.kind === 'facts') return `<p style="margin:8px 0 0;font-size:12.5px;${KEEP}">${b.items.map(f => `<b style="color:#4B5563">${esc(f.k)}</b>&nbsp; <span style="color:${TONE_TEXT[cellTone(f.v)]}">${esc(cellText(f.v))}</span>`).join('<br>')}</p>`;
  if (b.kind === 'cols') return `<table cellpadding="0" cellspacing="0" style="border-collapse:separate;border-spacing:0;width:100%"><tr>${b.cols.map((x, i) =>
    `<td style="width:${100 / b.cols.length}%;vertical-align:top;padding:${i === 0 ? '0 6px 0 0' : '0 0 0 6px'}"><div style="background:#F7F8FA;border-top:3px solid ${TONE_TEXT[x.tone]};padding:10px 12px;font-size:12.5px;line-height:1.6;color:#1F2937;${KEEP}"><div style="font-weight:bold;color:${TONE_TEXT[x.tone]};margin-bottom:4px">${esc(x.label)}</div>${br(x.body)}</div></td>`).join('')}</tr></table>`;
  if (b.kind === 'board') {
    const src = images[b.shift];
    return src ? `<img src="${src}" width="740" style="display:block;width:100%;max-width:740px;height:auto;margin:6px 0 4px;border:1px solid #E5E8EE" alt="스케줄 보드 ${b.shift === 'day' ? '주간' : '야간'}">`
      : '<p style="margin:4px 0 0;color:#8A94A6;font-size:12.5px">스케줄 보드 그림을 만들지 못했습니다.</p>';
  }
  if (b.kind === 'teams') {
    const cards = b.teams.map(t => `<td style="vertical-align:top;width:${100 / b.teams.length}%;padding:0 4px">
      <div style="border:1px solid #E5E8EE;border-radius:8px;padding:10px 12px;${KEEP}">
        <div style="font-size:14px;font-weight:bold;color:#111827">${esc(t.name)} &nbsp;${pillHtml(t.shift, shiftTone(t.shift))}</div>
        <div style="font-size:12px;color:#6B7280;margin:2px 0 6px">근무 <b style="color:#111827">${t.working}</b> / ${t.total}명</div>
        <div style="font-size:12.5px;line-height:1.7;color:#1F2937">${t.names.map(x => `${esc(x.n)}${x.t ? `<span style="color:#9CA3AF;font-size:11px"> ${esc(x.t)}</span>` : ''}`).join(', ') || '-'}</div>
        ${t.off.length ? `<div style="font-size:12px;margin-top:6px;color:${TONE_TEXT.warn}"><b>휴무</b> ${esc(t.off.join(', '))}</div>` : ''}
        ${t.edu.length ? `<div style="font-size:12px;margin-top:3px;color:${TONE_TEXT.info}"><b>교육</b> ${esc(t.edu.join(', '))}</div>` : ''}
      </div></td>`).join('');
    const tenure = `<table cellpadding="0" cellspacing="0" style="border-collapse:collapse;width:100%;margin-top:10px"><tr>${b.tenure.map(x =>
      `<td style="text-align:center;padding:6px 4px;background:#F7F8FA;border-right:2px solid #fff"><div style="font-size:11px;color:#6B7280">${esc(x.label)}</div><div style="font-size:15px;font-weight:bold;color:#111827">${x.n}<span style="font-size:11px;color:#6B7280">명</span></div></td>`).join('')}</tr></table>`;
    return `<table cellpadding="0" cellspacing="0" style="border-collapse:collapse;width:100%;margin:0 -4px"><tr>${cards}</tr></table>`
      + `<div style="font-size:12px;color:#6B7280;margin-top:10px">${esc(b.tenureTitle)}</div>${tenure}`
      + (b.others.length ? `<p style="margin:8px 0 0;font-size:12px;color:#4B5563">${b.others.map(o => `<b>${esc(o.k)}</b> ${esc(o.v)}`).join(' &nbsp;·&nbsp; ')}</p>` : '');
  }
  const TH = `padding:6px 8px;background:#F6F8FB;color:#6B7280;font-size:11.5px;font-weight:bold;border-bottom:1px solid #E5E8EE;white-space:nowrap`;
  const TD = `padding:7px 8px;border-bottom:1px solid #EEF1F5;font-size:12.5px;vertical-align:top;color:#1F2937;${KEEP}`;
  const align = (i: number) => (b.center?.includes(i) ? 'center' : 'left');
  return (b.caption ? `<div style="font-size:12px;font-weight:bold;color:#4B5563;margin:12px 0 4px">${esc(b.caption)}</div>` : '')
    + `<table cellpadding="0" cellspacing="0" style="border-collapse:collapse;width:100%;${FONT}">`
    + `<tr>${b.head.map((h, i) => `<th style="${TH};text-align:${align(i)}">${esc(h)}</th>`).join('')}</tr>`
    + b.rows.map(row => isGroupRow(row)
      ? `<tr><td colspan="${b.head.length}" style="padding:8px 8px 4px;font-size:11.5px;font-weight:bold;color:#3B5BDB;border-bottom:1px solid #E5E8EE;letter-spacing:0.5px">${esc(cellText(row[0]))}</td></tr>`
      : `<tr>${row.map((x, i) => `<td style="${TD};text-align:${align(i)}${b.wide?.includes(i) ? '' : ';white-space:nowrap'}">${cellHtml(x)}</td>`).join('')}</tr>`).join('')
    + '</table>';
}

/** images: 스케줄 보드 그림 data URL(day/night) — 복사할 때 만들어 넘긴다. */
export function mailHtml(m: DailyModel, images: Record<string, string> = {}): string {
  const out: string[] = [];
  out.push(`<div style="${FONT};color:#1F2937;max-width:760px">`);
  out.push(`<div style="font-size:22px;font-weight:bold;color:#111827;margin:2px 0 2px">Daily 업무 보고</div>`);
  out.push(`<div style="font-size:13px;color:#4B5563;padding-bottom:10px;border-bottom:2px solid ${ACCENT}">${esc(m.dateLabel)} &nbsp;|&nbsp; ${esc(m.shiftLabel)}</div>`);
  if (m.kpis.length) {
    const rows: Kpi[][] = [];
    for (let i = 0; i < m.kpis.length; i += 3) rows.push(m.kpis.slice(i, i + 3));
    out.push(`<table cellpadding="0" cellspacing="0" style="border-collapse:separate;border-spacing:6px;width:100%;margin:8px -6px 0">${rows.map(r =>
      `<tr>${r.map(k => `<td style="width:33%;background:#F6F8FB;border-radius:8px;padding:10px 12px;vertical-align:top"><div style="font-size:11.5px;color:#6B7280">${esc(k.label)}</div><div style="font-size:19px;font-weight:bold;color:#111827;margin-top:2px">${esc(k.value)}</div>${k.sub ? `<div style="font-size:11.5px;color:${TONE_TEXT[k.tone ?? '']};margin-top:1px">${esc(k.sub)}</div>` : ''}</td>`).join('')}${'<td></td>'.repeat(3 - r.length)}</tr>`).join('')}</table>`);
  }
  m.sections.forEach((s, i) => {
    out.push(`<table cellpadding="0" cellspacing="0" style="border-collapse:collapse;width:100%;margin:22px 0 8px"><tr>`
      + `<td style="font-size:15px;font-weight:bold;color:#111827;border-bottom:1px solid #E5E8EE;padding:0 0 6px"><span style="color:${ACCENT}">${String(i + 1).padStart(2, '0')}</span>&nbsp; ${esc(s.title)}</td>`
      + `<td style="text-align:right;font-size:12px;color:${TONE_TEXT[s.badge?.tone ?? 'dim']};border-bottom:1px solid #E5E8EE;padding:0 0 6px;white-space:nowrap">${s.badge ? esc(s.badge.t) : ''}</td></tr></table>`);
    out.push(s.blocks.map(b => blockHtml(b, images)).join(''));
  });
  out.push(`<p style="margin:24px 0 0;font-size:11px;color:#9CA3AF">세정 업무 통합 관리 · Daily 업무 보고에서 자동으로 만든 내용입니다.</p>`);
  out.push('</div>');
  return out.join('');
}

/** 서식 없는 메일(텍스트)로 붙을 때 쓰는 글 모양. */
export function mailText(m: DailyModel): string {
  const lines: string[] = ['[Daily 업무 보고]', `${m.dateLabel} | ${m.shiftLabel}`];
  if (m.kpis.length) lines.push(m.kpis.map(k => `${k.label} ${k.value}`).join(' · '));
  m.sections.forEach((s, i) => {
    lines.push('', `${String(i + 1).padStart(2, '0')}. ${s.title}${s.badge ? ` (${s.badge.t})` : ''}`);
    for (const b of s.blocks) {
      if (b.kind === 'note') lines.push(`  ${b.text}`);
      else if (b.kind === 'text') lines.push(`  [${b.label}]`, ...b.body.split('\n').map(x => `    ${x}`));
      else if (b.kind === 'cols') for (const x of b.cols) lines.push(`  [${x.label}]`, ...x.body.split('\n').map(y => `    ${y}`));
      else if (b.kind === 'facts') for (const f of b.items) lines.push(`  ${f.k}: ${cellText(f.v)}`);
      else if (b.kind === 'board') lines.push('  (스케줄 보드 그림은 서식 있는 메일에서 보입니다)');
      else if (b.kind === 'teams') {
        for (const t of b.teams) {
          lines.push(`  ${t.name} (${t.shift}) 근무 ${t.working}/${t.total}명: ${t.names.map(x => x.t ? `${x.n}(${x.t})` : x.n).join(', ') || '-'}`);
          if (t.off.length) lines.push(`    휴무: ${t.off.join(', ')}`);
          if (t.edu.length) lines.push(`    교육: ${t.edu.join(', ')}`);
        }
        lines.push(`  ${b.tenureTitle}: ${b.tenure.map(x => `${x.label} ${x.n}`).join(' / ')}`);
        for (const o of b.others) lines.push(`  ${o.k}: ${o.v}`);
      } else {
        if (b.caption) lines.push(`  - ${b.caption}`);
        for (const row of b.rows) lines.push(isGroupRow(row) ? `  [${cellText(row[0])}]` : `  · ${row.map(x => cellText(x).replace(/\n/g, ', ')).filter(Boolean).join(' | ')}`);
      }
    }
  });
  return lines.join('\n');
}
