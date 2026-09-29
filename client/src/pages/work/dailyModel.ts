// Daily 업무 보고 — 서버가 모아 준 하루치 기록을 "섹션 → 표/글" 모양으로 바꾼다.
// 화면과 메일 복사가 같은 모양을 쓰므로 둘의 내용이 어긋나지 않는다.
// 그래프 없이 표로 촘촘하게: 반쪽(half) 섹션 두 개는 나란히 놓는다(화면은 2단, 메일은 2칸 표).
import type { BakeLog, DailyCrewTeam, DailyHandover, DailyMeeting, DailyReport, WasteLog } from '../../api/types';
import { sortByLine } from './common';

export type Tone = '' | 'bad' | 'warn' | 'ok' | 'dim';
export type Cell = string | { t: string; tone?: Tone };
export type Block =
  | {
    kind: 'table'; caption?: string; head: string[]; rows: Cell[][];
    wide?: number[];                        // 글이 긴 칸(줄바꿈 허용)
    groups?: { t: string; span: number }[]; // 머리글 위 묶음 줄(체크시트 라인)
    fill?: boolean;                         // 칸 색을 배경으로(상태 표)
    rowHead?: boolean;                      // 첫 칸이 줄 제목(구분)
    stack?: boolean;                        // 폰에서 줄마다 카드로 푼다(칸 많은 목록)
  }
  | { kind: 'cols'; cols: { label: string; body: string }[] }
  | { kind: 'text'; label: string; body: string }
  | { kind: 'facts'; items: { k: string; v: Cell }[] }
  | { kind: 'chips'; items: { t: string; sub: string; tone: Tone }[] }
  | { kind: 'note'; text: string };
export interface Section { key: string; title: string; link?: string; half?: boolean; badge?: { t: string; tone: Tone }; blocks: Block[] }
export interface Alert { t: string; tone: 'bad' | 'warn'; key: string }
export interface DailyModel { head: { k: string; v: string }[]; alerts: Alert[]; sections: Section[] }

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
export const cellText = (x: Cell) => (typeof x === 'string' ? x : x.t);
export const cellTone = (x: Cell): Tone => (typeof x === 'string' ? '' : x.tone ?? '');
const fmt = (v: number | null | undefined) => (v === null || v === undefined ? '-' : String(Number(v.toFixed(3))));
const oneLine = (s: string) => s.split('\n').map(x => x.trim()).filter(Boolean).join(' / ');

/** 근속 표기 — 1년이 안 되면 개월, 넘으면 년(근무자 이름 옆에 붙인다). */
export const tenure = (m: number | null | undefined) => (m === null || m === undefined ? '' : m < 12 ? `${m}개월` : `${Math.floor(m / 12)}년`);
const TENURE_BUCKETS = ['1년 미만', '1년', '2년', '3년', '4년', '5년 이상'];

// ── 섹션별 ──

function crewSection(crew: DailyCrewTeam[], date: string): Section {
  const months = new Map<string, number | null>();
  for (const t of crew) for (const m of t.members) months.set(m.name, m.tenureMonths);
  const withT = (n: string) => { const y = tenure(months.get(n)); return y ? `${n}(${y})` : n; };
  // 교대 팀(1팀·2팀) 먼저, 그다음 주간팀 같은 교대 없는 생산팀
  const prod = crew.filter(t => t.production).sort((a, b) => Number(b.hasShift) - Number(a.hasShift));
  const others = crew.filter(t => !t.production && (t.off.length > 0 || t.edu.length > 0));
  const blocks: Block[] = [];
  if (prod.length === 0) blocks.push({ kind: 'note', text: '생산팀이 없습니다(조직 관리에서 생산팀을 지정하세요).' });
  else {
    blocks.push({
      kind: 'table', rowHead: true, head: ['구분', ...prod.map(t => `${t.team} · ${t.shift || '-'}`)], wide: prod.map((_, i) => i + 1),
      rows: [
        ['인원', ...prod.map(t => `근무 ${t.day.length + t.night.length} / 전체 ${t.members.length}`)],
        ['근무자', ...prod.map(t => [...t.day, ...t.night].map(withT).join(', ') || '-')],
        ['휴무', ...prod.map(t => c(t.off.join(', ') || '-', t.off.length ? 'warn' : 'dim'))],
        ['교육', ...prod.map(t => c(t.edu.join(', ') || '-', t.edu.length ? '' : 'dim'))],
      ],
    });
    // 근속별 인원(생산직 전체) — 입사일이 비어 있는 사람은 '미입력'
    const counts = TENURE_BUCKETS.map(() => 0);
    let unknown = 0;
    for (const t of prod) for (const m of t.members) {
      if (m.tenureMonths === null) unknown++;
      else counts[Math.min(5, Math.floor(m.tenureMonths / 12))]++;
    }
    blocks.push({
      kind: 'table', fill: true, caption: `근속별 인원 (생산직 ${prod.reduce((s, t) => s + t.members.length, 0)}명, ${md(date)} 기준)`,
      head: [...TENURE_BUCKETS, ...(unknown ? ['입사일 미입력'] : [])],
      rows: [[...counts.map(n => `${n}명`), ...(unknown ? [c(`${unknown}명`, 'dim')] : [])]],
    });
  }
  if (others.length) blocks.push({
    kind: 'facts', items: others.flatMap(t => [
      ...(t.off.length ? [{ k: `${t.team} 휴무`, v: t.off.join(', ') }] : []),
      ...(t.edu.length ? [{ k: `${t.team} 교육`, v: t.edu.join(', ') }] : []),
    ]),
  });
  return { key: 'crew', title: '근무 현황', link: '/calendar', blocks };
}

const stateCell = (state: string, ng: number, by: string, pending: boolean): Cell => {
  const ngT = ng > 0 ? ` NG${ng}` : '';
  if (state === 'submitted') return c(`✔ ${by || '제출'}${ngT}`, ng > 0 ? 'bad' : 'ok');
  if (state === 'progress') return c(`진행${ngT}`, ng > 0 ? 'bad' : 'warn');
  if (state === 'na') return c('-', 'dim');
  return c(pending ? '대기' : '미점검', pending ? 'dim' : 'bad');
};

function handoverSection(key: string, title: string, link: string, h: DailyHandover, alerts: Alert[]): Section {
  const rows: Cell[][] = [
    ...h.in.map(x => [c('입고', 'ok'), x.vendor, oneLine(x.content), md(x.inDate), md(x.outDate) || '-', x.status]),
    ...h.out.map(x => [c('출고', 'ok'), x.vendor, oneLine(x.content), md(x.inDate) || '-', md(x.outDate), x.status]),
    ...h.overdue.map(x => [c('지남', 'bad'), x.vendor, oneLine(x.content), md(x.inDate) || '-', c(md(x.outDate), 'bad'), x.status]),
  ];
  if (h.overdue.length) alerts.push({ t: `${title} 출고일 지남 ${h.overdue.length}`, tone: 'bad', key });
  return {
    key, title, link, half: true,
    badge: { t: `입고 ${h.in.length} · 출고 ${h.out.length} · 진행 ${h.open}`, tone: h.overdue.length ? 'bad' : '' },
    blocks: rows.length === 0 ? [{ kind: 'note', text: '이날 입고·출고가 없습니다.' }]
      : [{ kind: 'table', head: ['구분', '업체', '내용', '입고', '출고', '상태'], rows, wide: [2], stack: true }],
  };
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

export function buildDaily(r: DailyReport, today: string): DailyModel {
  const date = r.date.slice(0, 10);
  const isToday = date === today;
  const future = date > today;
  const alerts: Alert[] = [];
  const S: Section[] = [];

  // 맨 위 한 줄 — 생산일, 주간·야간 조(교대 팀만), 근무 인원
  const shiftTeams = (r.crew ?? []).filter(t => t.production && t.hasShift);
  const prodTeams = (r.crew ?? []).filter(t => t.production);
  const head = [
    { k: '생산일', v: `${date} (${dow(date)})` },
    { k: '주간', v: shiftTeams.filter(t => t.day.length > 0).map(t => t.team).join(', ') || '-' },
    { k: '야간', v: shiftTeams.filter(t => t.night.length > 0).map(t => t.team).join(', ') || '-' },
    ...(prodTeams.length ? [{ k: '근무 인원', v: `${prodTeams.reduce((s, t) => s + t.day.length + t.night.length, 0)} / ${prodTeams.reduce((s, t) => s + t.members.length, 0)}명` }] : []),
  ];

  // 근무 현황
  if (r.crew) S.push(crewSection(r.crew, date));

  // 생산팀 인수인계 — 주간·야간을 나란히(다른 팀 데일리의 '주간 생산 / 야간 생산' 칸처럼)
  if (r.meetings) {
    const join = (f: (m: DailyMeeting) => string) => r.meetings!.map(f).map(x => x.trim()).filter(Boolean).join('\n\n');
    const day = join(m => m.dayContent), night = join(m => m.nightContent), memo = join(m => m.officeMemo);
    const blocks: Block[] = r.meetings.length === 0 || (!day && !night && !memo)
      ? [{ kind: 'note', text: '이날 작성된 생산팀 인수인계가 없습니다.' }]
      : [{ kind: 'cols', cols: [{ label: '주간', body: day || '-' }, { label: '야간', body: night || '-' }] },
        ...(memo ? [{ kind: 'text', label: '사무실 메모', body: memo } as Block] : [])];
    S.push({ key: 'meeting', title: '인수인계 · 특이사항', link: '/meeting', blocks });
  }

  // 체크시트 — 구역을 가로로, 주간·야간 두 줄(라인은 위 묶음 줄)
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
    if (missing > 0) alerts.push({ t: `체크시트 미점검 ${missing}`, tone: 'warn', key: 'check' });
    if (openNg > 0) alerts.push({ t: `체크시트 미조치 NG ${openNg}`, tone: 'bad', key: 'check' });
    const groups: { t: string; span: number }[] = [{ t: '', span: 1 }];
    for (const x of z) {
      const g = groups[groups.length - 1];
      if (groups.length > 1 && g.t === x.line) g.span++; else groups.push({ t: x.line, span: 1 });
    }
    const blocks: Block[] = z.length === 0 ? [{ kind: 'note', text: '점검 구역이 없습니다.' }] : [{
      kind: 'table', fill: true, rowHead: true, groups: groups.length > 2 ? groups : undefined,
      head: ['', ...z.map(x => x.name)],
      rows: [
        ['주간', ...z.map(x => stateCell(x.dayState, x.dayNg, x.dayBy, dayPending))],
        ['야간', ...z.map(x => stateCell(x.nightState, x.nightNg, x.nightBy, nightPending))],
      ],
    }];
    if (ngs.length) blocks.push({
      kind: 'table', caption: `NG ${ngs.length}건`, head: ['교대', '구역', '항목', '메모', '조치'], wide: [2, 3, 4], stack: true,
      rows: ngs.map(n => [n.shift, n.zoneName, n.itemText, oneLine(n.memo) || '-',
        n.ngStatus === 'OPEN' ? c('미조치', 'bad') : c(`조치 완료${n.ngCloseNote ? ` · ${oneLine(n.ngCloseNote)}` : ''}`, 'ok')]),
    });
    S.push({ key: 'check', title: '체크시트', link: '/checklist', blocks,
      badge: { t: `제출 ${submitted}/${total}${ngs.length ? ` · NG ${ngs.length}` : ''}`, tone: openNg ? 'bad' : missing ? 'warn' : 'ok' } });
  }

  // 설비 진행 현황(스케줄 보드) — 모든 설비의 가동·대기·유휴를 한눈에, 가동 설비는 시간·레시피
  if (r.board) {
    const b = r.board;
    const running = b.equipment.filter(e => e.blocks.length > 0);
    const blocks: Block[] = b.equipment.length === 0 ? [{ kind: 'note', text: '스케줄 보드에 등록된 설비가 없습니다.' }] : [
      { kind: 'chips', items: b.equipment.map(e => ({
        t: e.name, sub: e.blocks.length ? `가동 ${e.blocks.length}` : e.isIdle ? '유휴' : '대기',
        tone: e.blocks.length ? 'ok' : e.isIdle ? 'dim' : '' as Tone,
      })) },
      ...(running.length ? [{
        kind: 'table', head: ['설비', '진행 (07:00 ~ 다음 날 07:00)'], wide: [1],
        rows: running.map(e => [e.name, e.blocks.map(x => `${x.start}~${x.end}${x.nextDay ? '(+1)' : ''} ${x.recipe}`).join('  ·  ')]),
      } as Block] : [{ kind: 'note', text: '이날 배치된 레시피가 없습니다.' } as Block]),
    ];
    S.push({ key: 'board', title: '설비 진행 현황', link: '/schedule-board', blocks,
      badge: { t: `가동 ${running.length} / ${b.totalEquipment}대${b.idleEquipment ? ` · 유휴 ${b.idleEquipment}` : ''}`, tone: '' } });
  }

  // 기타세정 · 주간세정 (나란히)
  if (r.handover) S.push(handoverSection('handover', '기타세정', '/handover', r.handover, alerts));
  if (r.weekly) S.push(handoverSection('weekly', '주간세정', '/weekly', r.weekly, alerts));

  // 생산팀 요청사항 — 신규·처리·마감 지남을 한 표로
  if (r.prodReq) {
    const p = r.prodReq;
    if (p.overdue.length) alerts.push({ t: `요청 마감 지남 ${p.overdue.length}`, tone: 'warn', key: 'prodreq' });
    const rows: Cell[][] = [
      ...p.new.map(x => [c('신규', 'warn'), x.category || '-', oneLine(x.requestDetail), `마감 ${md(x.dueDate) || '-'}`, x.requester || '-']),
      ...p.done.map(x => [c('처리', 'ok'), x.category || '-', oneLine(x.requestDetail), oneLine(x.actionDetail) || '-', x.assignee || '-']),
      ...p.overdue.map(x => [c('마감 지남', 'bad'), x.category || '-', oneLine(x.requestDetail), c(`마감 ${md(x.dueDate)}`, 'bad'), x.assignee || '-']),
    ];
    S.push({ key: 'prodreq', title: '생산팀 요청사항', link: '/prodreq',
      badge: { t: `신규 ${p.new.length} · 처리 ${p.done.length}`, tone: p.overdue.length ? 'warn' : '' },
      blocks: rows.length === 0 ? [{ kind: 'note', text: '이날 들어오거나 처리한 요청이 없습니다.' }]
        : [{ kind: 'table', head: ['구분', '분류', '요청 내용', '조치 / 마감', '담당'], rows, wide: [2, 3], stack: true }] });
  }

  // 약액 교체 · KOH·폐액 (나란히)
  if (r.chemical) {
    const rows = sortByLine(r.chemical.rows.filter(x => x.kind !== 'BAKE' && x.content));
    S.push({ key: 'chemical', title: '약액 교체', link: '/work/chemical', half: true,
      badge: { t: `${rows.length}대`, tone: '' },
      blocks: rows.length === 0 ? [{ kind: 'note', text: '이날 약액 교체가 없습니다.' }] : [{
        kind: 'table', head: ['설비', '공정', '교체 내용', '메모'], wide: [3],
        rows: rows.map(x => [x.code, x.process || '-', c(x.content, 'ok'), oneLine(x.note) || '-']),
      }] });
  }
  if (r.waste) {
    const cur = wasteDay(r.waste.rows, date);
    const prev = wasteDay(r.waste.rows, addDays(date, -1));
    const diff = (a: number, b: number): Cell => {
      if (!cur.has || !prev.has) return c('-', 'dim');
      const d = Number((a - b).toFixed(3));
      return d === 0 ? c('0', 'dim') : c(`${d > 0 ? '▲' : '▼'} ${fmt(Math.abs(d))}`, d > 0 ? 'warn' : 'ok');
    };
    const blocks: Block[] = !cur.has ? [{ kind: 'note', text: '이날 KOH·폐액 기록이 없습니다.' }] : [
      {
        kind: 'table', rowHead: true, head: ['', `전날 ${md(addDays(date, -1))}`, `당일 ${md(date)}`, '증감'],
        rows: [
          ['KOH 사용', prev.has ? fmt(prev.used) : '-', fmt(cur.used), diff(cur.used, prev.used)],
          ['폐액 증가', prev.has ? fmt(prev.inc) : '-', fmt(cur.inc), diff(cur.inc, prev.inc)],
          ...(cur.refill || prev.refill ? [['KOH 보충', fmt(prev.refill), fmt(cur.refill), '']] as Cell[][] : []),
          ...(cur.removed || prev.removed ? [['폐액 수거', fmt(prev.removed), fmt(cur.removed), '']] as Cell[][] : []),
          ['잔량 KOH / 폐액', `${fmt(prev.koh)} / ${fmt(prev.waste)}`, `${fmt(cur.koh)} / ${fmt(cur.waste)}`, ''],
        ],
      },
      ...cur.rows.filter(w => w.note.trim()).map(w => ({ kind: 'text', label: `${w.shift} 비고`, body: w.note.trim() } as Block)),
    ];
    S.push({ key: 'waste', title: 'KOH·폐액', link: '/work/waste', half: true, blocks });
  }

  // BAKE 그을음 — 가동한 회차만 표로, HOLD·비가동은 교대별 오븐 이름만 한 줄로
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
      kind: 'table', head: ['교대', '오븐', '가동', '품명 (S/N)', '그을음', "Q'TZ", '비고'], wide: [3], stack: true,
      rows: run.map(l => [`${l.shift}${roundMark(l.round)}`, l.eqCode, `${hm(l.trackIn)}→${hm(l.trackOut)}`,
        `${l.item}${l.serialNo ? ` (${l.serialNo})` : ''}`,
        c(l.soot || '-', l.hasSoot ? 'bad' : ''), c(l.quartz || '-', l.hasQuartz ? 'bad' : ''), oneLine(l.note) || '']),
    });
    if (idle.size) blocks.push({ kind: 'facts', items: [...idle].map(([k, eqs]) => ({ k, v: c(eqs.join(', '), 'dim') })) });
    S.push({ key: 'bake', title: 'BAKE 그을음', link: '/work/bake',
      badge: { t: `가동 ${run.length}회${issues.length ? ` · 이상 ${issues.length}` : ''}`, tone: issues.length ? 'bad' : '' },
      blocks });
  }

  // 반쪽 섹션이 짝 없이 혼자 남으면 한 줄 전체를 쓴다
  for (let i = 0; i < S.length; i++) {
    if (!S[i].half) continue;
    if (S[i + 1]?.half) { i++; continue; }
    S[i] = { ...S[i], half: false };
  }
  return { head, alerts, sections: S };
}

/** 화면·메일 공통 배치 — 반쪽 섹션은 둘씩 묶는다. */
export function rowsOf(sections: Section[]): Section[][] {
  const out: Section[][] = [];
  for (let i = 0; i < sections.length; i++) {
    if (sections[i].half && sections[i + 1]?.half) { out.push([sections[i], sections[i + 1]]); i++; }
    else out.push([sections[i]]);
  }
  return out;
}

// ── 메일 복사 — 메일 본문에 붙여도 모양이 유지되게 스타일을 태그마다 직접 붙인다 ──

const esc = (s: string) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
const br = (s: string) => esc(s).replace(/\n/g, '<br>');
const TONE_COLOR: Record<Tone, string> = { '': '#222', bad: '#c92a2a', warn: '#b35c00', ok: '#2b8a3e', dim: '#999' };
const TONE_FILL: Record<Tone, string> = { '': '#fff', bad: '#ffe3e3', warn: '#fff4e6', ok: '#ebfbee', dim: '#f8f9fa' };
const FONT = "font-family:'Malgun Gothic','맑은 고딕',Arial,sans-serif";
const TD = 'border:1px solid #c9d0da;padding:3px 6px;vertical-align:top;font-size:12px';
const TH = `${TD};background:#e7ecf4;font-weight:bold;text-align:center;white-space:nowrap`;

function blockHtml(b: Block): string {
  if (b.kind === 'note') return `<div style="color:#999;font-size:12px;margin:2px 0 4px">${esc(b.text)}</div>`;
  if (b.kind === 'text') return `<div style="font-size:12px;margin:4px 0"><b style="color:#555">${esc(b.label)}</b><br>${br(b.body)}</div>`;
  if (b.kind === 'facts') return `<div style="font-size:12px;margin:4px 0">${b.items.map(f => `<b style="color:#555">${esc(f.k)}</b> <span style="color:${TONE_COLOR[cellTone(f.v)]}">${esc(cellText(f.v))}</span>`).join(' &nbsp;|&nbsp; ')}</div>`;
  if (b.kind === 'cols') return `<table style="border-collapse:collapse;width:100%;${FONT}"><tr>${b.cols.map(x => `<th style="${TH};width:${100 / b.cols.length}%">${esc(x.label)}</th>`).join('')}</tr><tr>${b.cols.map(x => `<td style="${TD}">${br(x.body)}</td>`).join('')}</tr></table>`;
  if (b.kind === 'chips') return `<div style="margin:0 0 6px">${b.items.map(x => `<span style="display:inline-block;margin:0 3px 3px 0;padding:1px 6px;border:1px solid #c9d0da;border-radius:4px;font-size:11px;background:${TONE_FILL[x.tone]};color:${TONE_COLOR[x.tone]}"><b>${esc(x.t)}</b> ${esc(x.sub)}</span>`).join('')}</div>`;
  const out: string[] = [];
  if (b.caption) out.push(`<div style="font-size:12px;font-weight:bold;color:#555;margin:6px 0 2px">${esc(b.caption)}</div>`);
  out.push(`<table style="border-collapse:collapse;width:100%;margin:0 0 4px;${FONT}">`);
  if (b.groups) out.push(`<tr>${b.groups.map(g => `<th colspan="${g.span}" style="${TH}">${esc(g.t)}</th>`).join('')}</tr>`);
  out.push(`<tr>${b.head.map(h => `<th style="${TH}">${esc(h)}</th>`).join('')}</tr>`);
  for (const row of b.rows)
    out.push(`<tr>${row.map((x, i) => {
      const tone = cellTone(x);
      const bg = b.rowHead && i === 0 ? ';background:#f5f7fa;font-weight:bold;white-space:nowrap'
        : b.fill ? `;background:${TONE_FILL[tone]};text-align:center` : '';
      return `<td style="${TD};color:${TONE_COLOR[tone]}${tone === 'bad' ? ';font-weight:bold' : ''}${bg}">${br(cellText(x)) || '&nbsp;'}</td>`;
    }).join('')}</tr>`);
  out.push('</table>');
  return out.join('');
}

function sectionHtml(s: Section): string {
  return `<div style="margin:0 0 4px"><span style="display:inline-block;background:#dbe4f3;color:#1c3d7a;font-weight:bold;font-size:13px;padding:2px 10px;border:1px solid #b8c6e0">${esc(s.title)}</span>${s.badge ? ` <span style="font-size:12px;color:${TONE_COLOR[s.badge.tone]}">${esc(s.badge.t)}</span>` : ''}</div>`
    + s.blocks.map(blockHtml).join('');
}

export function mailHtml(model: DailyModel): string {
  const out: string[] = [];
  out.push(`<div style="${FONT};font-size:13px;color:#222;line-height:1.45;max-width:1100px">`);
  out.push(`<div style="font-size:17px;font-weight:bold;margin:0 0 6px">Daily 업무 보고</div>`);
  out.push(`<table style="border-collapse:collapse;margin:0 0 6px;${FONT}"><tr>${model.head.map(h => `<th style="${TH}">${esc(h.k)}</th><td style="${TD};font-weight:bold;padding:3px 10px">${esc(h.v)}</td>`).join('')}</tr></table>`);
  out.push(model.alerts.length
    ? `<div style="margin:0 0 8px">${model.alerts.map(a => `<span style="display:inline-block;margin:0 4px 3px 0;padding:1px 8px;border-radius:9px;font-size:12px;font-weight:bold;color:#fff;background:${a.tone === 'bad' ? '#e03131' : '#e8590c'}">${esc(a.t)}</span>`).join('')}</div>`
    : `<div style="margin:0 0 8px;color:#2b8a3e;font-weight:bold;font-size:12px">특이 이상 없음</div>`);
  for (const row of rowsOf(model.sections)) {
    if (row.length === 1) out.push(`<div style="margin:0 0 12px">${sectionHtml(row[0])}</div>`);
    else out.push(`<table style="border-collapse:collapse;width:100%;margin:0 0 12px"><tr>${row.map((s, i) => `<td style="width:50%;vertical-align:top;padding:0 ${i === 0 ? '8px' : '0'} 0 ${i === 1 ? '8px' : '0'}">${sectionHtml(s)}</td>`).join('')}</tr></table>`);
  }
  out.push('</div>');
  return out.join('');
}

/** 서식 없는 메일(텍스트)로 붙을 때 쓰는 글 모양. */
export function mailText(model: DailyModel): string {
  const lines: string[] = ['Daily 업무 보고', model.head.map(h => `${h.k} ${h.v}`).join(' | ')];
  lines.push(model.alerts.length ? `이상: ${model.alerts.map(a => a.t).join(', ')}` : '특이 이상 없음');
  for (const s of model.sections) {
    lines.push('', `■ ${s.title}${s.badge ? ` (${s.badge.t})` : ''}`);
    for (const b of s.blocks) {
      if (b.kind === 'note') lines.push(`  ${b.text}`);
      else if (b.kind === 'text') lines.push(`  [${b.label}]`, ...b.body.split('\n').map(x => `    ${x}`));
      else if (b.kind === 'cols') for (const x of b.cols) lines.push(`  [${x.label}]`, ...x.body.split('\n').map(y => `    ${y}`));
      else if (b.kind === 'facts') lines.push(`  ${b.items.map(f => `${f.k}: ${cellText(f.v)}`).join(' / ')}`);
      else if (b.kind === 'chips') lines.push(`  ${b.items.map(x => `${x.t} ${x.sub}`).join(', ')}`);
      else {
        if (b.caption) lines.push(`  - ${b.caption}`);
        lines.push(`    ${b.head.join(' | ')}`);
        for (const row of b.rows) lines.push(`    ${row.map(x => cellText(x).replace(/\n/g, ', ')).join(' | ')}`);
      }
    }
  }
  return lines.join('\n');
}
