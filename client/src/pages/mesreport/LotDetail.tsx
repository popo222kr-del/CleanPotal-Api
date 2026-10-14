import { useEffect, useMemo, useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { api } from '../../api/client';
import { downloadXlsx, won, type LotRow } from './mesReport';
import './MesReport.css';

// LOT 상세 새 창 — 세정 이력 조회에서 LOT ID 를 누르면 연다.
// 위에서 아래로: 요약(지금 상태·경과·판정·금액) → 진행 단계 → 반입 정보(돌려줄 곳 — 반출과 다르면 '변경됨') · 제품·세정 정보
// → 공정 이력 → 입고/출하 검사 측정값 → 반출 정보(삼성이 보낼 때의 원본 기록, 추적용).

interface Hist { seq: string; time: string | null; code: string; oper: string; operName: string; oldOper: string; oldOperName: string; res: string; user: string; userName: string; comment: string }
interface Edc { seq: string; param: string; oper: string; operName: string; values: string[]; user: string; userName: string; time: string | null }
interface Spec { param: string; oper: string; desc: string; min: string; max: string }
interface Detail { lot: LotRow; history: Hist[]; edc: Edc[]; spec?: Spec[]; price?: { unit: number; qty: number; amount: number } }

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
/** 걸린 시간 — 분·시간·일 */
function dur(ms: number) {
  if (!Number.isFinite(ms) || ms < 0) return '';
  const m = Math.round(ms / 60000);
  if (m < 60) return `${m}분`;
  const h = Math.floor(m / 60), r = m % 60;
  if (h < 48) return r ? `${h}시간 ${r}분` : `${h}시간`;
  const d = Math.floor(h / 24), rh = h % 24;
  return rh ? `${d}일 ${rh}시간` : `${d}일`;
}
interface Step { oper: string; name: string; state: 'done' | 'now' | 'skip' | 'todo'; start?: Date | null; end?: Date | null; eq?: string; user?: string }

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
    return { oper, name, state, start: toDate(st?.time), end: toDate(en?.time), eq: st?.res || en?.res || '', user: (en ?? st)?.userName } as Step;
  });
  // 시작 시각이 없으면 앞 단계가 끝난 시각
  steps.forEach((s, i) => { if (!s.start && i > 0) s.start = steps[i - 1].end ?? null; });
  return steps;
}

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

  return (
    <div className="mr-window mr-detail">
      <div className="mr-card">
        <div className="mr-card-h">
          {fromList && <button type="button" className="btn btn-ghost" onClick={() => nav(-1)}>← 목록</button>}
          <b>LOT {lotId}</b>
          {d && <button type="button" className="btn btn-ghost" onClick={exportXlsx}>엑셀(이력)</button>}
          <button type="button" className="btn btn-ghost mr-x" onClick={() => window.close()}>닫기</button>
        </div>
        {err && <div className="mr-err">{err}</div>}
        {!d && !err && <div className="mr-empty">불러오는 중…</div>}
        {l && (
          <div className="ld-sum">
            <div className="ld-sum-main">
              <span className={`ld-state${shipped ? ' ship' : ''}`}>
                {shipped ? '출하완료' : `${cur?.name ?? l.status} 중`}
                {!shipped && last && <small>{dur(now - last.getTime())}째</small>}
              </span>
              <span className={`ld-chip ${l.paid.includes('무상') ? 'free' : ''}`}>{l.paid || '유/무상 미상'}</span>
              {judge && <span className={`ld-chip ${judge === 'NG' ? 'ng' : judge === 'OK' ? 'ok' : ''}`}>판정 {judge}</span>}
              {visualBad.length > 0 && <span className="ld-chip ng">입고 외관 이상 · {visualBad.join(', ')}</span>}
            </div>
            <div className="ld-sum-title"><b>{l.matDesc}</b><span>{l.matId}</span><span>{l.customer}</span></div>
            <div className="ld-sum-facts">
              <span>AETS 입고 <b>{l.aetsInDay ?? '—'}</b></span>
              {shipped && <span>출고 <b>{l.aetsOutDay ?? '—'}</b></span>}
              {first && <span>{shipped ? '입고→출하' : '입고 후'} <b>{dur((leadEnd ?? new Date(now)).getTime() - first.getTime())}</b></span>}
              {d?.price && <span title={`${won(d.price.unit)}원 × ${d.price.qty}`}>금액 <b>{won(d.price.amount)}원</b>{d.price.qty !== 1 ? ` (${won(d.price.unit)} × ${d.price.qty})` : ''}</span>}
            </div>
          </div>
        )}
        {l && (
          <ol className="ld-steps">
            {steps.map((s, i) => (
              <li key={s.oper + i} className={s.state}>
                <i>{s.state === 'done' ? '✓' : s.state === 'skip' ? '–' : s.state === 'now' ? '●' : ''}</i>
                <b>{s.name}</b>
                <small>
                  {s.state === 'skip' ? '건너뜀'
                    : s.state === 'now' ? (s.start ? `${dur(now - s.start.getTime())}째` : '진행 중')
                      : s.end ? `${String(s.end.getMonth() + 1)}/${s.end.getDate()} ${String(s.end.getHours()).padStart(2, '0')}:${String(s.end.getMinutes()).padStart(2, '0')}` : ''}
                </small>
                {s.state === 'done' && s.start && s.end && s.end > s.start && <small className="d">{dur(s.end.getTime() - s.start.getTime())}</small>}
                {s.eq && <small className="eq">{s.eq}</small>}
              </li>
            ))}
          </ol>
        )}
      </div>

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
                {l.comment && <tr><th>COMMENT</th><td colSpan={3} className="cmt">{l.comment}</td></tr>}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {d && (
        <div className="mr-card">
          <div className="mr-card-h"><b>공정 이력</b><span className="mr-dim">{d.history.length}건</span></div>
          <div className="mr-scroll">
            <table className="mr-table mr-lots">
              <thead><tr><th className="no">#</th><th>일시</th><th>구분</th><th>공정</th><th>설비</th><th>작업자</th><th>COMMENT</th></tr></thead>
              <tbody>
                {d.history.map(h => {
                  const w = what(h);
                  return (
                    <tr key={h.seq}>
                      <td className="no">{h.seq}</td><td>{h.time}</td><td><span className={`mr-kind k-${h.code}`}>{w.kind}</span></td><td>{w.step}</td>
                      <td>{h.res}</td><td title={h.user}>{h.userName}</td><td className="w">{h.comment}</td>
                    </tr>
                  );
                })}
                {d.history.length === 0 && <tr><td colSpan={7} className="mr-empty">이력이 없습니다.</td></tr>}
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
        <div className="mr-card ld-out">
          <div className="mr-card-h"><b>반출 정보</b><span className="mr-dim">삼성이 보낼 때의 원본 기록 — 추적용(반입 때 S/N·라인이 바뀔 수 있음)</span></div>
          <table className="mr-kv">
            <tbody>
              <tr><th>반출번호</th><td>{l.outNo || '—'}</td><th>반출 S/N</th><td>{l.outSn || '—'}</td><th>SEC 반출라인</th><td>{l.line || '—'}</td></tr>
              <tr><th>PM 설비명</th><td>{l.pmEq || '—'}</td><th>분임조</th><td>{l.circle || '—'}</td><th>고객사</th><td>{l.customer || '—'}</td></tr>
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
