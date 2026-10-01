// Daily 업무 보고 — 서버가 모아 준 하루치 기록을 "요약 숫자 + 섹션 → 블록" 모양으로 바꾼다.
// 화면과 메일 복사가 같은 모양을 쓰므로 둘의 내용이 어긋나지 않는다.
// 메일은 한 줄(단일 열, 폭 960)로 — 좁은 칸에서 글자가 어색하게 끊기지 않게 하고, 한글은 단어 단위로만 줄을 바꾼다.
// 근무 인원은 팀마다 한 줄씩, 이름은 같은 폭 칸에 나란히(쉼표로 이어 붙이지 않는다) — 줄이 바뀌어도 칸이 맞는다.
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
  | { kind: 'table'; caption?: string; head: string[]; rows: Cell[][]; wide?: number[]; center?: number[]; stack?: boolean; matrix?: boolean }   // matrix: 첫 머리 칸이 묶음 이름(MBO·METAL), 첫 열이 주간/야간
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
    kpis.push({ label: '체크시트(현장) 제출', value: `${submitted} / ${total}`, sub: ngs.length ? `NG ${ngs.length}건` : missing ? `미점검 ${missing}` : '이상 없음', tone: openNg ? 'bad' : missing ? 'warn' : 'ok' });
    // 라인(METAL·N-METAL)마다 표 하나 — 구역을 가로로, 주간·야간 두 줄(라인 이름을 줄마다 되풀이하지 않는다)
    const lines = [...new Set(z.map(x => x.line))];
    const blocks: Block[] = z.length === 0 ? [{ kind: 'note', text: '점검 구역이 없습니다.' }] : lines.map(line => {
      const zs = z.filter(x => x.line === line);
      return {
        kind: 'table', matrix: true, head: [line || '공통', ...zs.map(x => x.name)], center: zs.map((_, i) => i + 1),
        rows: [
          ['주간', ...zs.map(x => stateCell(x.dayState, x.dayNg, x.dayBy, dayPending))],
          ['야간', ...zs.map(x => stateCell(x.nightState, x.nightNg, x.nightBy, nightPending))],
        ],
      } as Block;
    });
    if (ngs.length) blocks.push({
      kind: 'table', caption: `NG ${ngs.length}건`, head: ['교대', '구역', '항목', '메모', '조치'], wide: [2, 3], stack: true,
      rows: ngs.map(n => [n.shift, n.zoneName, n.itemText, oneLine(n.memo) || '-',
        n.ngStatus === 'OPEN' ? pill('미조치', 'bad') : c(`조치 완료${n.ngCloseNote ? ` · ${oneLine(n.ngCloseNote)}` : ''}`, 'ok')]),
    });
    S.push({ key: 'check', title: '체크시트 (현장)', link: '/checklist', blocks,
      badge: { t: `제출 ${submitted}/${total}`, tone: openNg ? 'bad' : missing ? 'warn' : 'ok' } });
  }

  // 체크시트(설비) — 라인별로 매일·주간·월간 완료 설비 수와 매일 미점검 설비, 그날 나온 NG·고장
  if (r.eqCheck) {
    const s = r.eqCheck.status, ngs = r.eqCheck.ngs;
    type K = 'daily' | 'weekly' | 'monthly';
    const tally = (rows: typeof s.rows, k: K) => {
      const t = rows.filter(x => x[k].state !== 'none');
      return { done: t.filter(x => x[k].state === 'done').length, total: t.length, late: t.some(x => x[k].state === 'late') };
    };
    const cell = (rows: typeof s.rows, k: K): Cell => {
      const { done, total, late } = tally(rows, k);
      if (total === 0) return c('-', 'dim');
      if (done === total) return pill(`완료 ${done}/${total}`, 'ok');
      return c(`${done}/${total}${late ? ' · 지연' : ''}`, late ? 'bad' : future ? 'dim' : 'warn');
    };
    const d = tally(s.rows, 'daily');
    const missingOf = (rows: typeof s.rows) => rows.filter(x => x.daily.state !== 'none' && x.daily.state !== 'done').map(x => x.unitCode);
    const missing = missingOf(s.rows);
    const openNg = ngs.filter(n => n.ngStatus === 'OPEN').length;
    kpis.push({ label: '체크시트(설비) 매일', value: `${d.done} / ${d.total}`,
      sub: ngs.length ? `NG·고장 ${ngs.length}건` : missing.length && !future ? `미점검 ${missing.length}대` : '이상 없음',
      tone: openNg ? 'bad' : missing.length && !isToday && !future ? 'warn' : 'ok' });
    const lines = [...new Set(s.rows.map(x => x.line))];
    const month = Number(s.monthKey.slice(5));
    const blocks: Block[] = s.rows.length === 0 ? [{ kind: 'note', text: '점검할 설비가 없습니다.' }] : [{
      kind: 'table', head: ['라인', `매일 (${md(s.date)})`, '매일 미점검 설비', `주간 (~${md(s.weekDue)} 금)`, `월간 (${month}월)`],
      center: [1, 3, 4], wide: [2],
      rows: lines.map(l => {
        const rows = s.rows.filter(x => x.line === l);
        const miss = missingOf(rows);
        const dailyTotal = rows.filter(x => x.daily.state !== 'none').length;
        const missCell = miss.length === 0 ? c('-', 'dim')
          : miss.length === dailyTotal ? c(`전체 미점검 (${miss.length}대)`, future ? 'dim' : 'warn')
          : c(miss.join(', '), future ? 'dim' : '');
        return [l || '공통', cell(rows, 'daily'), missCell, cell(rows, 'weekly'), cell(rows, 'monthly')];
      }),
    }];
    const cycleName: Record<string, string> = { 일상: '매일', 주간: '주간', 월간: '월간', 고장: '고장' };
    if (ngs.length) blocks.push({
      kind: 'table', caption: `NG·고장 ${ngs.length}건`, head: ['주기', '설비', '항목', '값 · 메모', '조치'], wide: [2, 3], stack: true,
      rows: ngs.map(n => [cycleName[n.cycle] ?? n.cycle, n.unitCode, `${n.name}${n.point ? ` ${n.point}` : ''}`,
        [n.valueText, oneLine(n.memo)].filter(Boolean).join(' · ') || '-',
        n.ngStatus === 'OPEN' ? pill('미조치', 'bad') : c(`조치 완료${n.ngCloseNote ? ` · ${oneLine(n.ngCloseNote)}` : ''}`, 'ok')]),
    });
    S.push({ key: 'eqcheck', title: '체크시트 (설비)', link: '/eq-check', blocks,
      badge: { t: `매일 ${d.done}/${d.total}`, tone: openNg ? 'bad' : d.done === d.total ? 'ok' : 'warn' } });
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
      if (runs.length === 0) return '';
      const bad = runs.some(l => l.hasSoot || l.hasQuartz);
      return pill(runs.length > 1 ? `${runs.length}회` : '가동', bad ? 'bad' : 'ok');
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
    // 오븐은 앞 글자(MBO·NBO)로 묶고 칸 머리는 번호만(01-1) — 한 묶음이 표 하나, 가로로 오븐·세로로 주간/야간
    const split = (code: string) => { const m = /^([A-Za-z]+)[-_ ]?(.+)$/.exec(code); return m ? { g: m[1].toUpperCase(), n: m[2] } : { g: code, n: code }; };
    const groups: { g: string; ovens: string[] }[] = [];
    for (const o of ovens) {
      const { g } = split(o);
      const grp = groups.find(x => x.g === g);
      if (grp) grp.ovens.push(o); else groups.push({ g, ovens: [o] });
    }
    const remarks = ovens.map(o => ({ o, rm: remark(o) })).filter(x => x.rm);
    S.push({ key: 'bake', title: 'BAKE 진행 현황', link: '/work/bake',
      badge: { t: `가동 ${ranOvens}대 / ${ovens.length}대${issues.length ? ` · 이상 ${issues.length}` : ''}`, tone: issues.length ? 'bad' : '' },
      blocks: ovens.length === 0 ? [{ kind: 'note', text: '등록된 BAKE 오븐이 없습니다.' }]
        : logs.length === 0 ? [{ kind: 'note', text: '이날 BAKE 가동 기록이 없습니다.' }] : [
        ...groups.map(grp => ({
          kind: 'table', matrix: true, head: [grp.g, ...grp.ovens.map(o => split(o).n)], center: grp.ovens.map((_, i) => i + 1),
          rows: cols.map(x => [`${x}간`, ...grp.ovens.map(o => runCell(o, x))]),
        } as Block)),
        ...(remarks.length ? [{ kind: 'facts', items: remarks.map(x => ({ k: x.o, v: c(x.rm, 'bad') })) } as Block] : []),
      ] });
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
// 메일 편집기는 <style> 을 버리므로 스타일을 태그마다 붙이고, 레이아웃은 표로 잡는다(한 열, 폭 960).
const MAIL_W = 960;
/** 메일에서 한 줄에 놓을 이름 칸 수 — 팀 머리 칸(150)을 뺀 폭에 이름+근속이 한 줄로 들어가게. */
const NAMES_PER_ROW = 6;

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
  if (b.kind === 'note') return `<p style="margin:4px 0 0;color:#8A94A6;font-size:13px">${esc(b.text)}</p>`;
  if (b.kind === 'text') return `<p style="margin:10px 0 0;font-size:13px;line-height:1.6;color:#1F2937;${KEEP}"><b style="color:#4B5563">${esc(b.label)}</b><br>${br(b.body)}</p>`;
  if (b.kind === 'facts') return `<p style="margin:8px 0 0;font-size:13px;line-height:1.6;${KEEP}">${b.items.map(f => `<b style="color:#4B5563">${esc(f.k)}</b>&nbsp; <span style="color:${TONE_TEXT[cellTone(f.v)]}">${esc(cellText(f.v))}</span>`).join('<br>')}</p>`;
  if (b.kind === 'cols') return `<table cellpadding="0" cellspacing="0" style="border-collapse:separate;border-spacing:0;width:100%"><tr>${b.cols.map((x, i) =>
    `<td style="width:${100 / b.cols.length}%;vertical-align:top;padding:${i === 0 ? '0 6px 0 0' : '0 0 0 6px'}"><div style="background:#F7F8FA;border-top:3px solid ${TONE_TEXT[x.tone]};padding:10px 14px;font-size:13px;line-height:1.65;color:#1F2937;${KEEP}"><div style="font-weight:bold;color:${TONE_TEXT[x.tone]};margin-bottom:4px">${esc(x.label)}</div>${br(x.body)}</div></td>`).join('')}</tr></table>`;
  if (b.kind === 'board') {
    const src = images[b.shift];
    return src ? `<img src="${src}" width="${MAIL_W - 20}" style="display:block;width:100%;max-width:${MAIL_W - 20}px;height:auto;margin:6px 0 4px;border:1px solid #E5E8EE" alt="스케줄 보드 ${b.shift === 'day' ? '주간' : '야간'}">`
      : '<p style="margin:4px 0 0;color:#8A94A6;font-size:12.5px">스케줄 보드 그림을 만들지 못했습니다.</p>';
  }
  if (b.kind === 'teams') {
    // 팀마다 한 줄 — 왼쪽 팀 머리(이름·근무·인원), 오른쪽 이름 칸(같은 폭, 한 줄에 NAMES_PER_ROW 명)
    const NAME_W = `${(100 / NAMES_PER_ROW).toFixed(2)}%`;
    const nameCell = (x: { n: string; t: string } | null) => x
      ? `<td width="${NAME_W}" style="width:${NAME_W};padding:3px 12px 3px 0;white-space:nowrap;font-size:13.5px;color:#111827">${esc(x.n)}${x.t ? `<span style="color:#9CA3AF;font-size:11.5px">&nbsp;${esc(x.t)}</span>` : ''}</td>`
      : `<td width="${NAME_W}" style="width:${NAME_W}"></td>`;
    const names = (t: TeamCard) => {
      if (t.names.length === 0) return '<span style="color:#8A94A6">-</span>';
      const rows: string[] = [];
      for (let i = 0; i < t.names.length; i += NAMES_PER_ROW) {
        const chunk = t.names.slice(i, i + NAMES_PER_ROW);
        rows.push(`<tr>${chunk.map(nameCell).join('')}${Array.from({ length: NAMES_PER_ROW - chunk.length }, () => nameCell(null)).join('')}</tr>`);
      }
      return `<table width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;width:100%;table-layout:fixed">${rows.join('')}</table>`;
    };
    const sub = (label: string, list: string[], tone: Tone) => list.length
      ? `<div style="font-size:12.5px;margin-top:6px;color:${TONE_TEXT[tone]}"><b>${label}</b>&nbsp; ${esc(list.join(', '))}</div>` : '';
    const teamRows = b.teams.map(t => `<tr>
      <td width="150" style="width:150px;vertical-align:top;padding:10px 12px;background:#F7F8FA;border-bottom:2px solid #fff">
        <div style="font-size:15px;font-weight:bold;color:#111827;white-space:nowrap">${esc(t.name)}&nbsp; ${pillHtml(t.shift, shiftTone(t.shift))}</div>
        <div style="font-size:12.5px;color:#6B7280;margin-top:3px;white-space:nowrap">근무 <b style="color:#111827;font-size:14px">${t.working}</b> / ${t.total}명</div>
      </td>
      <td style="vertical-align:top;padding:8px 0 8px 14px;border-bottom:1px solid #EEF1F5">${names(t)}${sub('휴무', t.off, 'warn')}${sub('교육', t.edu, 'info')}</td>
    </tr>`).join('');
    // 메일 편집기(Outlook 등)는 CSS 폭을 무시하고 글자 길이대로 칸을 나눈다 — 칸마다 width 속성으로 같은 폭을 박는다
    const tw = `${(100 / Math.max(1, b.tenure.length)).toFixed(2)}%`;
    const tenure = `<table width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;width:100%;margin-top:6px;table-layout:fixed"><tr>${b.tenure.map(x =>
      `<td width="${tw}" style="width:${tw};text-align:center;padding:7px 4px;background:#F7F8FA;border-right:3px solid #fff"><div style="font-size:11.5px;color:#6B7280">${esc(x.label)}</div><div style="font-size:16px;font-weight:bold;color:#111827">${x.n}<span style="font-size:11.5px;color:#6B7280;font-weight:normal">명</span></div></td>`).join('')}</tr></table>`;
    return `<table width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse;width:100%">${teamRows}</table>`
      + `<div style="font-size:12.5px;font-weight:bold;color:#4B5563;margin-top:12px">${esc(b.tenureTitle)}</div>${tenure}`
      + (b.others.length ? `<p style="margin:8px 0 0;font-size:12.5px;color:#4B5563">${b.others.map(o => `<b>${esc(o.k)}</b> ${esc(o.v)}`).join(' &nbsp;·&nbsp; ')}</p>` : '');
  }
  const TH = `padding:7px 10px;background:#F6F8FB;color:#6B7280;font-size:12px;font-weight:bold;border-bottom:1px solid #E5E8EE;white-space:nowrap`;
  const TD = `padding:8px 10px;border-bottom:1px solid #EEF1F5;font-size:13px;line-height:1.55;vertical-align:top;color:#1F2937;${KEEP}`;
  const align = (i: number) => (b.center?.includes(i) ? 'center' : 'left');
  const thStyle = (i: number) => (b.matrix && i === 0 ? `${TH};color:#3B5BDB;font-size:12px;letter-spacing:0.5px` : TH);
  const tdStyle = (i: number) => (b.matrix ? (i === 0 ? `${TD};font-weight:bold;color:#4B5563` : `${TD};white-space:normal`) : TD);
  return (b.caption ? `<div style="font-size:12px;font-weight:bold;color:#4B5563;margin:12px 0 4px">${esc(b.caption)}</div>` : '')
    + `<table cellpadding="0" cellspacing="0" style="border-collapse:collapse;width:100%;${b.matrix ? 'table-layout:fixed;margin-bottom:12px;' : ''}${FONT}">`
    + (b.matrix ? `<colgroup><col style="width:64px">${b.head.slice(1).map(() => '<col>').join('')}</colgroup>` : '')
    + `<tr>${b.head.map((h, i) => `<th style="${thStyle(i)};text-align:${align(i)}">${esc(h)}</th>`).join('')}</tr>`
    + b.rows.map(row => isGroupRow(row)
      ? `<tr><td colspan="${b.head.length}" style="padding:8px 8px 4px;font-size:11.5px;font-weight:bold;color:#3B5BDB;border-bottom:1px solid #E5E8EE;letter-spacing:0.5px">${esc(cellText(row[0]))}</td></tr>`
      : `<tr>${row.map((x, i) => `<td style="${tdStyle(i)};text-align:${align(i)}${b.wide?.includes(i) || b.matrix ? '' : ';white-space:nowrap'}">${cellHtml(x)}</td>`).join('')}</tr>`).join('')
    + '</table>';
}

/** images: 스케줄 보드 그림 data URL(day/night) — 복사할 때 만들어 넘긴다. */
export function mailHtml(m: DailyModel, images: Record<string, string> = {}): string {
  const out: string[] = [];
  out.push(`<div style="${FONT};color:#1F2937;max-width:${MAIL_W}px">`);
  out.push(`<div style="font-size:22px;font-weight:bold;color:#111827;margin:2px 0 2px">Daily 업무 보고</div>`);
  out.push(`<div style="font-size:13px;color:#4B5563;padding-bottom:10px;border-bottom:2px solid ${ACCENT}">${esc(m.dateLabel)} &nbsp;|&nbsp; ${esc(m.shiftLabel)}</div>`);
  if (m.kpis.length) {
    // 요약 숫자는 한 줄(6개까지), 넘으면 두 줄로 고르게
    const per = m.kpis.length <= 6 ? m.kpis.length : Math.ceil(m.kpis.length / 2);
    const rows: Kpi[][] = [];
    for (let i = 0; i < m.kpis.length; i += per) rows.push(m.kpis.slice(i, i + per));
    out.push(`<table width="100%" cellpadding="0" cellspacing="0" style="border-collapse:separate;border-spacing:6px;width:100%;table-layout:fixed;margin:8px -6px 0">${rows.map(r =>
      `<tr>${r.map(k => `<td width="${(100 / per).toFixed(2)}%" style="width:${(100 / per).toFixed(2)}%;background:#F6F8FB;border-radius:8px;padding:10px 12px;vertical-align:top;${KEEP}"><div style="font-size:12px;color:#6B7280">${esc(k.label)}</div><div style="font-size:20px;font-weight:bold;color:#111827;margin-top:2px;white-space:nowrap">${esc(k.value)}</div>${k.sub ? `<div style="font-size:12px;color:${TONE_TEXT[k.tone ?? '']};margin-top:1px">${esc(k.sub)}</div>` : ''}</td>`).join('')}${`<td width="${(100 / per).toFixed(2)}%"></td>`.repeat(per - r.length)}</tr>`).join('')}</table>`);
  }
  m.sections.forEach((s, i) => {
    out.push(`<table cellpadding="0" cellspacing="0" style="border-collapse:collapse;width:100%;margin:22px 0 8px"><tr>`
      + `<td style="font-size:16px;font-weight:bold;color:#111827;border-bottom:1px solid #E5E8EE;padding:0 0 6px"><span style="color:${ACCENT}">${String(i + 1).padStart(2, '0')}</span>&nbsp; ${esc(s.title)}</td>`
      + `<td style="text-align:right;font-size:12.5px;color:${TONE_TEXT[s.badge?.tone ?? 'dim']};border-bottom:1px solid #E5E8EE;padding:0 0 6px;white-space:nowrap">${s.badge ? esc(s.badge.t) : ''}</td></tr></table>`);
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
