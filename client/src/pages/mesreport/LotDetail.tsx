import { Fragment, useEffect, useMemo, useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { api } from '../../api/client';
import { downloadXlsx, type LotRow } from './mesReport';
import './MesReport.css';

// LOT 상세 새 창 — 세정 이력 조회에서 LOT ID 를 누르면 연다.
// 위에서 아래로: LOT(현재 상태 표 + 진행 단계 바) → 반출 정보 → 반입 정보(반출과 다르면 '변경됨') · 제품·세정 정보
// → 공정 진행·이력(공정마다 실제 소요 vs 제품 셋업 READ TIME, 그 아래 그 공정의 이력 줄) → 입고/출하 검사
// (단가·금액은 넣지 않는다 — LOT 조회마다 단가가 보이면 보안상 곤란, 2026-10-15)

interface Hist { seq: string; time: string | null; code: string; oper: string; operName: string; oldOper: string; oldOperName: string; res: string; user: string; userName: string; comment: string; recipe?: string; chemCount?: string }
interface Edc { seq: string; param: string; oper: string; operName: string; values: string[]; user: string; userName: string; time: string | null }
interface Spec { param: string; oper: string; desc: string; min: string; max: string }
interface RecipeTime { oper: string; code: string; description: string; minutes: number | null; min: number | null; max: number | null; count: number }
interface RecipeDef { code: string; description: string; oper: string; minutes: number | null }
interface Detail { lot: LotRow; history: Hist[]; edc: Edc[]; spec?: Spec[]; recipes?: RecipeTime[]; recipeDefs?: RecipeDef[] }

/** 측정 항목 순서·이름 — 기존 MES 측정 입력 화면 기준. 스펙 표에 설명이 있으면 그걸 쓴다. */
const PARAMS: [string, string][] = [
  ['NTHK', '누적막두께(Å)'], ['CHIP', '깨짐'], ['CRACK', 'CRACK'], ['SCR', '긁힘'], ['STAIN', '얼룩'], ['PIT', '패임'], ['STRIP', '벗겨짐'],
  ['UNETCH', 'UNETCH'], ['WEIGHT', '무게(g)'], ['THK', '두께'], ['PARTICLE', '표면먼지(ea/sq.in)'], ['ROUGH', '표면거칠기(㎛)'], ['RESULT', '합부판정'],
];
const ORDER = new Map(PARAMS.map(([p], i) => [p, i]));
const DESC = new Map(PARAMS);
/** 외관 항목 — Y 면 이상 */
const VISUAL = new Set(['CHIP', 'CRACK', 'SCR', 'STAIN', 'PIT', 'STRIP', 'UNETCH']);
/** 입고검사 쪽 공정 — 나머지(최종검사 7000 등)는 출하검사 쪽 */
const IN_OPERS = new Set(['RCV', '2000', '2100']);
/** 진행 단계 — 늘 보이는 단계와, 이력에 있을 때만 보이는 단계(LASER·BAKE·REPAIR) */
const FLOW: { oper: string; name: string; optional?: boolean }[] = [
  { oper: '2000', name: '입고' }, { oper: '2100', name: '입고검사' }, { oper: '3000', name: '세정' }, { oper: '4000', name: '건조' },
  { oper: '4100', name: 'LASER', optional: true }, { oper: '5000', name: 'BAKE', optional: true }, { oper: '6000', name: 'REPAIR', optional: true },
  { oper: '7000', name: '출하검사' }, { oper: '7100', name: '포장' }, { oper: 'WH', name: '창고' }, { oper: 'SHIP', name: '출하' },
];
const toDate = (t: string | null | undefined) => (t ? new Date(t.replace(' ', 'T')) : null);
/** 걸린 시간 — 시간 단위(소수 1자리). 1시간 안 되면 분. */
function dur(ms: number) {
  if (!Number.isFinite(ms) || ms < 0) return '';
  const m = Math.round(ms / 60000);
  return m < 60 ? `${m}분` : `${(ms / 3600000).toFixed(1)}시간`;
}
/** 레시피 기준과 실제 — ±10% 안이면 기준 내, 넘으면 초과(빨강), 모자라면 미달(주황) */
function vsRecipe(actualMs: number, recipeMin: number, running: boolean) {
  const diffH = actualMs / 3600000 - recipeMin / 60;
  const ratio = actualMs / 60000 / recipeMin;
  const sign = diffH >= 0 ? '+' : '−';
  const text = `${sign}${Math.abs(diffH).toFixed(1)}시간 (${Math.round(ratio * 100)}%)`;
  if (running) return ratio > 1.1 ? { cls: 'over', label: `초과 중 ${text}` } : { cls: 'run', label: `진행 ${Math.round(ratio * 100)}%` };
  if (ratio > 1.1) return { cls: 'over', label: `초과 ${text}` };
  if (ratio < 0.9) return { cls: 'short', label: `미달 ${text}` };
  return { cls: 'ok', label: `기준 내 ${text}` };
}
/** running = 지금 단계에서 작업을 시작함(START) — false 면 도착만 하고 대기 중 */
interface Step { oper: string; name: string; state: 'done' | 'now' | 'skip' | 'todo'; running?: boolean; start?: Date | null; end?: Date | null; eq?: string; user?: string }

/** 이력으로 단계마다 상태·시작·끝·설비를 만든다 */
function buildSteps(h: Hist[], l: LotRow): Step[] {
  const shipped = l.status === '출하완료' || h.some(x => x.code === 'SHIP');
  const wh = h.some(x => x.oper === '8000' || x.oldOper === '8000') ? '8000' : '8100';
  const whName = wh === '8000' ? '천안창고' : '동탄창고';
  const seen = new Set(h.flatMap(x => [x.oper, x.oldOper]).filter(Boolean));
  const skipped = new Set<string>();
  // SKIP 2100→7000 이면 그 사이(3000·4000…)는 건너뜀
  h.filter(x => x.code === 'SKIP' && x.oldOper && x.oper).forEach(x => FLOW.forEach(f => { if (f.oper > x.oldOper && f.oper < x.oper && /^\d+$/.test(f.oper)) skipped.add(f.oper); }));
  const steps = FLOW.filter(f => !f.optional || seen.has(f.oper)).map(f => {
    const oper = f.oper === 'WH' ? wh : f.oper;
    const name = f.oper === 'WH' ? whName : f.name;
    if (f.oper === 'SHIP') {
      const sh = h.filter(x => x.code === 'SHIP').pop();
      return { oper, name, state: shipped ? 'done' : 'todo', end: toDate(sh?.time), user: sh?.userName } as Step;
    }
    if (f.oper === '2000') {
      const c = h.find(x => x.code === 'CREATE');
      const e = h.filter(x => x.code === 'END' && x.oldOper === '2000').pop();
      return { oper, name, state: e || shipped ? 'done' : l.oper === '2000' ? 'now' : c ? 'done' : 'todo', start: toDate(c?.time), end: toDate(e?.time ?? c?.time), user: c?.userName } as Step;
    }
    const st = h.filter(x => x.code === 'START' && x.oper === oper).pop();
    const en = h.filter(x => (x.code === 'END' || x.code === 'SKIP') && x.oldOper === oper).pop();
    const state: Step['state'] = skipped.has(oper) ? 'skip' : en ? 'done' : !shipped && l.oper === oper ? 'now' : shipped ? 'done' : 'todo';
    if (state === 'now') {
      // 이 공정으로 들어온 시각(앞 공정 완료) 뒤에 START 가 있으면 작업 중, 없으면 대기
      const arr = h.filter(x => (x.code === 'END' || x.code === 'SKIP') && x.oper === oper).pop();
      const running = !!st && (!arr || (st.time ?? '') >= (arr.time ?? ''));
      return { oper, name, state, running, start: toDate(running ? st?.time : arr?.time), eq: running ? st?.res ?? '' : '', user: (running ? st : arr)?.userName } as Step;
    }
    return { oper, name, state, start: toDate(st?.time), end: toDate(en?.time), eq: st?.res || en?.res || '', user: (en ?? st)?.userName } as Step;
  });
  // 시작 시각이 없으면 앞에서 마지막으로 끝난 시각(건너뛴 단계는 넘어서)
  let lastEnd: Date | null = null;
  steps.forEach(s => { if (!s.start && (s.state === 'done' || s.state === 'now') && s.oper !== 'SHIP') s.start = lastEnd; if (s.end) lastEnd = s.end; });   // 출하는 한 시점(시각만)
  // 끝난 단계인데 끝 시각이 없으면(창고 → 출하처럼 다음 단계가 나간 시각) 다음 단계의 시작·끝
  steps.forEach((s, i) => {
    if (s.state !== 'done' || s.end) return;
    const next = steps.slice(i + 1).find(n => n.state !== 'skip' && (n.start || n.end));
    s.end = next ? (next.start && next.start > (s.start ?? new Date(0)) ? next.start : next.end) ?? null : null;
  });
  return steps;
}

/** 지금 상태 말 — 세정 대기 / 세정 중 / 출하 대기(창고) / 출하완료 */
function stateText(steps: Step[], l: LotRow) {
  if (steps.find(s => s.oper === 'SHIP')?.state === 'done') return '출하완료';
  const c = steps.find(s => s.state === 'now');
  if (!c) return l.status;
  if (c.oper === '8000' || c.oper === '8100') return `출하 대기 · ${c.name}`;
  return `${c.name} ${c.running ? '중' : '대기'}`;
}

/** 여러 줄 메모의 첫 줄 */
const firstLine = (t: string) => (t ?? '').replace(/\r\n?/g, '\n').split('\n').find(x => x.trim()) ?? '';

const num = (v: string) => { const n = Number((v ?? '').replace(/,/g, '')); return v !== '' && Number.isFinite(n) ? n : null; };

/** 이력 한 줄을 사람 말로 — CREATE 입고 / START 시작 / END 완료(끝낸 공정 → 다음 공정) / SHIP 출하 */
function what(h: Hist) {
  switch (h.code) {
    case 'CREATE': return { kind: '입고', step: h.operName };
    case 'START': return { kind: '시작', step: h.operName };
    case 'END': return { kind: '완료', step: h.oldOperName ? `${h.oldOperName} → ${h.operName}` : h.operName };
    case 'SHIP': return { kind: '출하', step: h.oldOperName || h.operName };
    default: return { kind: h.code, step: h.oldOper && h.oldOper !== h.oper ? `${h.oldOperName} → ${h.operName}` : h.operName };
  }
}

export default function LotDetail() {
  const { lotId = '' } = useParams();
  const nav = useNavigate();
  // LOT 현황 조회 목록에서 들어왔으면 목록으로 돌아가는 단추
  const fromList = (useLocation().state as { from?: string } | null)?.from === 'list';
  const [d, setD] = useState<Detail | null>(null);
  const [err, setErr] = useState('');

  useEffect(() => {
    document.title = `${lotId} - LOT 상세`;
    api.get<Detail>(`/api/mes-report/lot/${encodeURIComponent(lotId)}`).then(setD)
      .catch(e => setErr(e instanceof Error ? e.message : '불러오지 못했습니다.'));
  }, [lotId]);

  function exportXlsx() {
    if (!d) return;
    void downloadXlsx(`LOT_${lotId}.xlsx`, '이력', ['순번', '일시', '구분', '공정', '설비', '작업자', '사번', 'COMMENT'],
      d.history.map(h => { const w = what(h); return [h.seq, h.time, w.kind, w.step, h.res, h.userName, h.user, h.comment]; }), [6, 20, 8, 22, 10, 10, 10, 50]);
  }

  const l = d?.lot;
  const steps = useMemo(() => (d ? buildSteps(d.history, d.lot) : []), [d]);
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => { const t = setInterval(() => setNow(Date.now()), 60000); return () => clearInterval(t); }, []);
  const cur = steps.find(s => s.state === 'now');
  const last = d?.history.length ? toDate(d.history[d.history.length - 1].time) : null;
  const shipped = steps.find(s => s.oper === 'SHIP')?.state === 'done';
  const first = steps[0]?.start ?? steps[0]?.end ?? null;
  const leadEnd = shipped ? steps[steps.length - 1].end : null;
  const judge = (l?.hist?.cleanState ?? '').toUpperCase();
  // 입고검사 요약 — 누적막두께, 외관 이상(Y) 항목
  const inEdc = d?.edc.filter(e => IN_OPERS.has(e.oper)) ?? [];
  const lastOf = (p: string) => inEdc.filter(e => e.param === p).sort((a, b) => Number(a.seq) - Number(b.seq)).pop()?.values[0] ?? '';
  const visualBad = [...VISUAL].filter(p => lastOf(p).toUpperCase() === 'Y').map(p => DESC.get(p) ?? p);
  const snChanged = !!l && !!l.inSn && !!l.outSn && l.inSn !== l.outSn;
  const lineChanged = !!l && !!l.inLine && !!l.line && l.inLine !== l.line;

  const fmt = (t?: Date | null) => (t ? `${t.getMonth() + 1}/${t.getDate()} ${String(t.getHours()).padStart(2, '0')}:${String(t.getMinutes()).padStart(2, '0')}` : '');
  // 공정별 레시피 — ① 이 LOT 이 실제로 건 레시피(그 공정 START 줄의 LOT_CMF_3, 시간 = 레시피 정의 READ TIME, 없으면 DRYnn → nn 시간)
  //                ② 없으면 제품 셋업의 MAIN 레시피
  const recipeOf = (oper: string): { code: string; description: string; minutes: number | null; count: number; source: 'lot' | 'setup' } | undefined => {
    const used = (d?.history ?? []).filter(h => h.code === 'START' && h.oper === oper && h.recipe).pop()?.recipe;
    if (used) {
      const def = d?.recipeDefs?.find(r => r.code.toUpperCase() === used.toUpperCase());
      const dry = /^DRY\s*(\d+(?:\.\d+)?)$/i.exec(used);
      return { code: used, description: def?.description ?? '', minutes: def?.minutes ?? (dry ? Number(dry[1]) * 60 : null), count: 1, source: 'lot' };
    }
    const rc = d?.recipes?.find(r => r.oper === oper);
    return rc ? { code: rc.code, description: rc.description, minutes: rc.minutes, count: rc.count, source: 'setup' } : undefined;
  };
  const hasRecipes = steps.some(st => recipeOf(st.oper));
  // COMMENT — 이력에 남은 메모 전부(같은 글은 한 번), 이력에 없으면 LOT 의 마지막 메모. 줄바꿈 그대로.
  const comments = (() => {
    const seen = new Set<string>();
    const list = (d?.history ?? []).filter(h => (h.comment ?? '').trim().length > 0 && h.comment.trim() !== '-')
      .filter(h => { const k = h.comment.trim(); if (seen.has(k)) return false; seen.add(k); return true; })
      .map(h => ({ when: h.time ?? '', who: h.userName, step: what(h).step, text: h.comment.replace(/\r\n?/g, '\n').trim() }));
    if (l?.comment && !list.some(c => c.text.includes(l.comment.replace(/\r\n?/g, '\n').trim()))) list.push({ when: '', who: '', step: '', text: l.comment.replace(/\r\n?/g, '\n').trim() });
    return list;
  })();
  // 케미칼 횟수(설비 사용횟수) — 기존 MES 의 어느 칸인지 확인되면 서버가 chemCount 로 보낸다
  // 케미칼 횟수 = 세정 START 줄의 LOT_CMF_18(그 때 세정 설비의 약액 사용 횟수) — 여러 번 세정했으면 마지막
  const chemCount = (d?.history ?? []).filter(h => h.code === 'START' && h.oper === '3000' && h.chemCount).pop()?.chemCount
    ?? (d?.history ?? []).filter(h => h.chemCount).pop()?.chemCount ?? '';
  const stateName = (st: Step) => ({ done: '완료', now: st.running ? '진행 중' : '대기 중', skip: '건너뜀', todo: '대기' })[st.state];
  const nowText = l ? stateText(steps, l) : '';
  // 성적서 칸 — Insp Date = 출하검사(7000) 날짜(측정값 입력 시각, 없으면 출하검사 완료 시각)
  const fiEdc = d?.edc.filter(e => e.oper === '7000').map(e => e.time ?? '').sort().pop();
  const fiEnd = steps.find(s => s.oper === '7000')?.end;
  const inspDate = fiEdc ? fiEdc.slice(0, 10) : fiEnd ? `${fiEnd.getFullYear()}-${String(fiEnd.getMonth() + 1).padStart(2, '0')}-${String(fiEnd.getDate()).padStart(2, '0')}` : '';
  const [showHist, setShowHist] = useState(true);
  // 이력 한 줄이 어느 공정 것인지 — 시작은 그 공정, 완료·SKIP 은 끝낸 공정, 입고는 2000, 출하는 SHIP
  // 입고 → 입고검사로 넘긴 줄(END 2000)은 입고검사의 '시작'으로 — 입고 칸은 입고(CREATE) 한 줄만
  const stepOf = (h: Hist) => h.code === 'CREATE' ? '2000' : h.code === 'SHIP' ? 'SHIP' : h.code === 'START' ? h.oper
    : h.code === 'END' && h.oldOper === '2000' ? (h.oper || '2100') : h.oldOper || h.oper;
  /** 이력 줄 구분 — 입고·시작·완료(→ 다음 공정)·출하, 그 밖은 코드 그대로(HOLD·RELEASE…) */
  const evKind = (h: Hist): { kind: string; to?: string } => {
    if (h.code === 'END' && h.oldOper === '2000') return { kind: '시작' };
    if (h.code === 'END' || h.code === 'SKIP') return { kind: h.code === 'SKIP' ? 'SKIP' : '완료', to: h.oper && h.oper !== h.oldOper ? h.operName : undefined };
    return { kind: what(h).kind };
  };
  const stepOpers = new Set(steps.map(s => s.oper));
  const histOf = (oper: string) => (d?.history ?? []).filter(h => stepOf(h) === oper);
  const otherHist = (d?.history ?? []).filter(h => !stepOpers.has(stepOf(h)));

  return (
    <div className="mr-window mr-detail">
      <div className="mr-card">
        <div className="mr-card-h">
          {fromList && <button type="button" className="btn btn-ghost" onClick={() => nav(-1)}>← 목록</button>}
          <b>LOT {lotId}</b>
          {l && <span className={`ld-state${shipped ? ' ship' : cur && !cur.running ? ' wait' : ''}`}>{nowText}</span>}
          {d && <button type="button" className="btn btn-ghost" onClick={exportXlsx}>엑셀(이력)</button>}
          <button type="button" className="btn btn-ghost mr-x" onClick={() => window.close()}>닫기</button>
        </div>
        {err && <div className="mr-err">{err}</div>}
        {!d && !err && <div className="mr-empty">불러오는 중…</div>}
        {l && (
          // 성적서(INSPECT REPORT) 머리표와 같은 칸 — Line·S/N 은 반입 기준. 누적(Å) 은 누적막두께, 케미칼 횟수 = 세정 START 이력의 LOT_CMF_18.
          <table className="ld-cert">
            <tbody>
              <tr><th>Customer</th><td>{l.customer}</td><th>Line</th><td>{l.inLine || l.line}</td><th className="res-h">INSPECT RESULT</th></tr>
              <tr><th>Equipment</th><td>{l.pmEq}</td><th>분임조</th><td>{l.circle}</td>
                <td rowSpan={6} className={`res ${judge === 'NG' ? 'ng' : judge === 'OK' ? 'ok' : 'none'}`}>{judge || '판정 전'}</td></tr>
              <tr><th>Part Name</th><td>{l.matDesc}</td><th>Process</th><td>{l.procName}</td></tr>
              <tr><th>세정 코드</th><td>{l.matId}</td><th>S/N</th><td>{l.inSn || l.outSn}</td></tr>
              <tr><th>TYPE</th><td>{l.matType}</td><th>누적(Å)</th><td>{l.life}</td></tr>
              <tr><th>사용 횟수</th><td>{l.useCount}</td><th>Insp Date</th><td>{inspDate}</td></tr>
              <tr><th>케미칼 횟수</th><td className="b" title="세정 시작 때 세정 설비의 약액 사용 횟수(이력 LOT_CMF_18)">{chemCount || <span className="mr-dim">—</span>}</td><th>Remark</th><td className="remark" title={l.comment}>{firstLine(l.comment)}</td></tr>
            </tbody>
          </table>
        )}
        {l && (
          <ol className="ld-steps">
            {steps.map((st, i) => (
              <li key={st.oper + i} className={`${st.state}${st.state === 'now' && !st.running ? ' wait' : ''}`}>
                <i>{st.state === 'done' ? '✓' : st.state === 'skip' ? '–' : st.state === 'now' ? '●' : ''}</i>
                <b>{st.name}</b>
                <small>{st.state === 'skip' ? '건너뜀' : st.state === 'now' ? `${st.running ? '' : '대기 '}${st.start ? `${dur(now - st.start.getTime())}째` : ''}`.trim() || '진행 중' : st.state === 'done' ? fmt(st.oper === '2000' ? st.start : st.end) : ''}</small>
                {st.state === 'done' && st.oper !== '2000' && st.start && st.end && st.end > st.start && <small className="d">{dur(st.end.getTime() - st.start.getTime())}</small>}
                {st.eq && <small className="eq">{st.eq}</small>}
              </li>
            ))}
          </ol>
        )}
        {l && comments.length > 0 && (
          <div className="ld-cmts">
            <div className="ld-cmts-h">COMMENT <span className="mr-dim">{comments.length}건 · 오래된 것부터</span></div>
            {comments.map((c, i) => (
              <div key={i} className="ld-cmt">
                {c.when && <span className="who">{c.when}{c.who ? ` · ${c.who}` : ''}{c.step ? ` · ${c.step}` : ''}</span>}
                <div className="txt">{c.text}</div>
              </div>
            ))}
          </div>
        )}
      </div>

      {l && (
        <div className="mr-card">
          <div className="mr-card-h"><b>반출 정보</b><span className="mr-dim">삼성이 보낼 때의 기록 — 반입 때 S/N·라인이 바뀔 수 있음(바뀌면 아래 반입 정보에 표시)</span></div>
          <table className="mr-kv">
            <tbody>
              <tr><th>반출번호</th><td>{l.outNo || '—'}</td><th>반출 S/N</th><td>{l.outSn || '—'}</td><th>SEC 반출라인</th><td>{l.line || '—'}</td></tr>
              <tr><th>PM 설비명</th><td>{l.pmEq || '—'}</td><th>분임조</th><td>{l.circle || '—'}</td><th>고객사</th><td>{l.customer || '—'}</td></tr>
            </tbody>
          </table>
        </div>
      )}
      {l && (
        <div className="ld-grid">
          <div className="mr-card">
            <div className="mr-card-h"><b>반입 정보</b><span className="mr-dim">돌려줄 곳 — 실제로 쓰는 값</span></div>
            <table className="mr-kv ld-kv">
              <tbody>
                <tr><th>반입 S/N</th><td>{l.inSn || <span className="mr-dim">—</span>}{snChanged && <Changed was={l.outSn} />}</td></tr>
                <tr><th>SEC 반입라인</th><td>{l.inLine || <span className="mr-dim">—</span>}{lineChanged && <Changed was={l.line} />}</td></tr>
                <tr><th>반입 위치</th><td>{l.shipTo || <span className="mr-dim">—</span>}</td></tr>
              </tbody>
            </table>
          </div>
          <div className="mr-card">
            <div className="mr-card-h"><b>제품 · 세정 정보</b></div>
            <table className="mr-kv ld-kv two">
              <tbody>
                <tr><th>공정명</th><td>{l.procName || '—'}</td><th>TYPE</th><td>{l.matType || '—'}</td></tr>
                <tr><th>누적(A)</th><td>{l.life || '—'}</td><th>사용횟수</th><td>{l.useCount || '—'}</td></tr>
                <tr><th>세정 설비</th><td>{steps.find(s => s.oper === '3000')?.eq || '—'}</td><th>건조 설비</th><td>{steps.find(s => s.oper === '4000')?.eq || '—'}</td></tr>
                <tr><th>입고검사</th><td colSpan={3}>
                  {inEdc.length === 0 ? <span className="mr-dim">측정값 없음</span> : <>
                    {lastOf('NTHK') && <>누적막두께 <b>{lastOf('NTHK')}</b> · </>}
                    {visualBad.length ? <span className="ld-bad">외관 이상: {visualBad.join(', ')}</span> : <span className="ld-good">외관 이상 없음</span>}
                    {lastOf('WEIGHT') && <> · 무게 {lastOf('WEIGHT')}g</>}
                  </>}
                </td></tr>
              </tbody>
            </table>
          </div>
        </div>
      )}

      {l && d && (
        <div className="mr-card">
          <div className="mr-card-h">
            <b>공정 진행 · 이력</b>
            <span className="mr-dim">{hasRecipes ? '굵은 줄 = 공정(실제 소요 vs 레시피 시간 — 이 LOT 이 건 레시피, 없으면 제품 셋업 MAIN · ±10% 안이면 기준 내) · 아래 줄 = 그 공정의 이력' : '레시피 기록이 없어 실제 시간만 보여 줍니다 · 아래 줄 = 그 공정의 이력'}</span>
            <button type="button" className="btn btn-ghost" onClick={() => setShowHist(v => !v)}>{showHist ? '이력 접기' : `이력 펼치기 (${d.history.length}건)`}</button>
          </div>
          <div className="mr-scroll ld-flow-wrap">
            <table className="mr-table ld-flow">
              <thead><tr><th>공정</th><th>상태</th><th>구분</th><th>일시</th><th>설비</th><th>작업자</th><th>소요</th><th>레시피</th><th>기준 시간</th><th>기준 대비</th><th>COMMENT</th></tr></thead>
              <tbody>
                {steps.map((st, i) => {
                  const rc = recipeOf(st.oper);
                  const running = st.state === 'now' && !!st.running;   // 대기 중이면 소요·레시피 비교 없음
                  const ms = st.start ? ((running ? now : st.end?.getTime() ?? NaN) - st.start.getTime()) : NaN;
                  // 세정은 레시피 표시만(레시피 시간 = 약액 공정 시간이라 START~END 와 비교하지 않음), 입고는 입고 시각만
                  const timed = st.oper !== '3000' && st.oper !== '2000';
                  const cmp = timed && rc?.minutes && Number.isFinite(ms) && (st.state === 'done' || running) ? vsRecipe(ms, rc.minutes, running) : null;
                  const showDur = st.oper !== '2000' && st.state !== 'skip' && Number.isFinite(ms) && (st.state === 'done' || running);
                  const evs = showHist ? histOf(st.oper) : [];
                  const n = Math.max(1, evs.length);
                  const lead = (
                    <>
                      <td rowSpan={n} className="nm">{st.name}</td>
                      <td rowSpan={n}><span className={`ld-st ${st.state}${st.state === 'now' && !st.running ? ' wait' : ''}`}>{stateName(st)}</span></td>
                    </>
                  );
                  const tail = (
                    <>
                      <td rowSpan={n} className="h">{showDur ? `${dur(ms)}${running ? '째' : ''}` : ''}</td>
                      <td rowSpan={n} title={rc ? `${rc.code}${rc.source === 'setup' ? ' (제품 셋업 MAIN — 이 LOT 이력에 레시피 없음)' : ' (이 LOT 이 건 레시피)'}` : ''}>
                        {rc ? <><b className="ld-rcp">{rc.description || rc.code}</b>{rc.description && <span className="mr-dim"> {rc.code}</span>}{rc.source === 'setup' && <span className="mr-dim"> · 셋업</span>}{rc.count > 1 && <span className="mr-dim"> 외 {rc.count - 1}</span>}{timed && !rc.minutes && <span className="mr-dim"> · 시간 미등록</span>}</> : ''}
                      </td>
                      <td rowSpan={n} className="h">{timed && rc?.minutes ? dur(rc.minutes * 60000) : ''}</td>
                      <td rowSpan={n}>{cmp && <span className={`ld-cmp ${cmp.cls}`}>{cmp.label}</span>}</td>
                    </>
                  );
                  if (evs.length === 0) {
                    // 이력을 접었거나 아직 오지 않은 단계 — 한 줄 요약
                    const when = st.state === 'skip' ? '' : st.oper === '2000' ? fmt(st.start) : [fmt(st.start), st.state === 'done' ? fmt(st.end) : ''].filter(Boolean).join(' ~ ');
                    return (
                      <tr key={st.oper + i} className={`step first s-${st.state}`}>
                        {lead}<td />{<td className="t">{when}</td>}<td>{st.eq}</td><td>{st.user}</td>{tail}<td />
                      </tr>
                    );
                  }
                  return (
                    <Fragment key={st.oper + i}>
                      {evs.map((h, k) => { const w = evKind(h); return (
                        <tr key={h.seq} className={`step s-${st.state}${k === 0 ? ' first' : ''}`}>
                          {k === 0 && lead}
                          <td className="ev-k"><span className={`mr-kind k-${h.code}`}>{w.kind}</span></td>
                          <td className="t">{h.time}{w.to && <span className="mr-dim"> → {w.to}</span>}</td>
                          <td>{h.res}</td>
                          <td title={h.user}>{h.userName}</td>
                          {k === 0 && tail}
                          <td className="w">{h.comment}</td>
                        </tr>
                      ); })}
                    </Fragment>
                  );
                })}
                {showHist && otherHist.map(h => { const w = evKind(h); return (
                  <tr key={h.seq} className="step first">
                    <td className="nm">{h.operName || h.oper}</td><td />
                    <td className="ev-k"><span className={`mr-kind k-${h.code}`}>{w.kind}</span></td>
                    <td className="t">{h.time}{w.to && <span className="mr-dim"> → {w.to}</span>}</td><td>{h.res}</td><td title={h.user}>{h.userName}</td><td colSpan={4} /><td className="w">{h.comment}</td>
                  </tr>
                ); })}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {d && (
        <div className="mr-edc-grid">
          <EdcPanel title="입고검사" sub="IN INSP" rows={d.edc.filter(e => IN_OPERS.has(e.oper))} spec={d.spec ?? []} />
          <EdcPanel title="출하검사" sub="FI INSP" rows={d.edc.filter(e => !IN_OPERS.has(e.oper))} spec={d.spec ?? []} />
        </div>
      )}

      {l && (
        <div className="mr-card">
          <div className="mr-card-h"><b>그 밖의 정보</b><span className="mr-dim">성적서 머리표에 없는 값 — 자리 검토 중</span></div>
          <table className="mr-kv ld-now">
            <tbody>
              <tr>
                <th>현재 상태</th><td className={`strong ${shipped ? 'good' : 'accent'}`}>{nowText}</td>
                <th>{shipped ? '입고 → 출하' : '현재 단계 머문 시간'}</th>
                <td className="strong">{shipped ? (first && leadEnd ? dur(leadEnd.getTime() - first.getTime()) : '—') : cur?.start ? dur(now - cur.start.getTime()) : last ? dur(now - last.getTime()) : '—'}</td>
                <th>입고 후 경과</th><td>{first ? dur((leadEnd ?? new Date(now)).getTime() - first.getTime()) : '—'}</td>
              </tr>
              <tr>
                <th>유/무상</th><td className={l.paid.includes('무상') ? 'warn' : ''}>{l.paid || '—'}</td>
                <th>입고 외관</th><td className={visualBad.length ? 'bad strong' : inEdc.length ? 'good' : ''}>{inEdc.length === 0 ? <span className="mr-dim">측정값 없음</span> : visualBad.length ? `이상 · ${visualBad.join(', ')}` : '이상 없음'}</td>
                <th>TYPE</th><td>{l.matType || '—'}</td>
              </tr>
              <tr>
                <th>AETS 입고일</th><td>{l.aetsInDay ?? '—'}</td>
                <th>AETS 출고일</th><td>{l.aetsOutDay ?? <span className="mr-dim">—</span>}</td>
                <th>반출 S/N · 라인</th><td>{[l.outSn, l.line].filter(Boolean).join(' · ') || '—'}</td>
              </tr>
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

/** 반출 때 값과 다를 때 — 원래 값을 작게 */
function Changed({ was }: { was: string }) {
  return <span className="ld-changed" title="반출 정보와 다릅니다">변경됨 · 반출 {was}</span>;
}

/** 측정값 반쪽 — 항목 · 설명 · MIN · MAX · 값(여러 개면 줄마다). 스펙을 벗어나거나 외관 Y · 판정 NG 면 빨간색. 입력자는 머리에 한 번. */
function EdcPanel({ title, sub, rows, spec }: { title: string; sub: string; rows: Edc[]; spec: Spec[] }) {
  // 같은 항목을 여러 번 쟀으면 마지막(HIST_SEQ 가 큰 것)
  const latest = new Map<string, Edc>();
  rows.forEach(e => { const p = latest.get(e.param); if (!p || Number(e.seq) >= Number(p.seq)) latest.set(e.param, e); });
  const list = [...latest.values()].sort((a, b) => (ORDER.get(a.param) ?? 99) - (ORDER.get(b.param) ?? 99) || a.param.localeCompare(b.param));
  const who = [...new Set(list.map(e => e.userName).filter(Boolean))].join(', ');
  const when = list.map(e => e.time ?? '').sort().pop()?.slice(0, 16);
  const specOf = (e: Edc) => spec.find(s => s.param === e.param && s.oper === e.oper) ?? spec.find(s => s.param === e.param && !s.oper)
    ?? spec.find(s => s.param === e.param);
  const bad = (e: Edc, v: string, sp?: Spec) => {
    if (VISUAL.has(e.param)) return v.toUpperCase() === 'Y';
    if (e.param === 'RESULT') return v.toUpperCase() === 'NG';
    const n = num(v), lo = num(sp?.min ?? ''), hi = num(sp?.max ?? '');
    return n !== null && ((lo !== null && n < lo) || (hi !== null && n > hi));
  };
  return (
    <div className="mr-card">
      <div className="mr-card-h">
        <b>{title}</b><span className="mr-dim">{sub}</span>
        {who && <span className="mr-edc-who">입력 {who}{when ? ` · ${when}` : ''}</span>}
      </div>
      {list.length === 0 ? <div className="mr-empty">측정값이 없습니다.</div> : (
        <table className="mr-table mr-edc">
          <thead><tr><th>PARAMETER</th><th>항목</th><th>MIN</th><th>MAX</th><th>값</th></tr></thead>
          <tbody>
            {list.map(e => {
              const sp = specOf(e);
              const vals = e.values.length ? e.values : [''];
              return (
                <tr key={e.param}>
                  <td className="p">{e.param}</td>
                  <td>{sp?.desc || DESC.get(e.param) || ''}</td>
                  <td className="n">{sp?.min}</td>
                  <td className="n">{sp?.max}</td>
                  <td className="n v">{vals.map((v, i) => <div key={i} className={bad(e, v, sp) ? 'bad' : ''}>{v}</div>)}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
    </div>
  );
}
