import { Fragment, useEffect, useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { api } from '../../api/client';
import { downloadXlsx, type LotRow } from './mesReport';
import './MesReport.css';

// LOT 상세 새 창 — 세정 이력 조회에서 LOT ID 를 누르면 연다. 기존 MES 이력(시각·공정·설비·작업자·메모)과 측정값(입/출하 검사) 전체.

interface Hist { seq: string; time: string | null; code: string; oper: string; operName: string; oldOper: string; oldOperName: string; res: string; user: string; userName: string; comment: string }
interface Edc { seq: string; param: string; oper: string; operName: string; values: string[]; user: string; userName: string; time: string | null }
interface Spec { param: string; oper: string; desc: string; min: string; max: string }
interface Detail { lot: LotRow; history: Hist[]; edc: Edc[]; spec?: Spec[] }

/** 측정 항목 순서·이름 — 기존 MES 측정 입력 화면 기준. 스펙 표에 설명이 있으면 그걸 쓴다. */
const PARAMS: [string, string][] = [
  ['NTHK', '누적막두께(Å)'], ['CHIP', '깨짐'], ['CRACK', 'CRACK'], ['SCR', '긁힘'], ['STAIN', '얼룩'], ['PIT', '패임'], ['STRIP', '벗겨짐'],
  ['UNETCH', 'UNETCH'], ['WEIGHT', '무게(g)'], ['PARTICLE', '표면먼지(ea/sq.in)'], ['ROUGH', '표면거칠기(㎛)'], ['RESULT', '합부판정'],
];
const ORDER = new Map(PARAMS.map(([p], i) => [p, i]));
const DESC = new Map(PARAMS);
/** 외관 항목 — Y 면 이상 */
const VISUAL = new Set(['CHIP', 'CRACK', 'SCR', 'STAIN', 'PIT', 'STRIP', 'UNETCH']);
/** 입고검사 쪽 공정 — 나머지(최종검사 7000 등)는 출하검사 쪽 */
const IN_OPERS = new Set(['RCV', '2000', '2100']);
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
  const info: [string, string | null | undefined][] = l ? [
    ['PART\'S 명', l.matDesc], ['세정코드', l.matId], ['고객사', l.customer], ['반출번호', l.outNo], ['반출 S/N', l.outSn], ['반입 S/N', l.inSn],
    ['SEC 반출라인', l.line], ['SEC 반입라인', l.inLine && l.inLine !== l.line ? `${l.inLine} (반출라인과 다름)` : l.inLine], ['반입 위치', l.shipTo], ['PM 설비명', l.pmEq], ['분임조', l.circle], ['공정명', l.procName], ['TYPE', l.matType],
    ['누적(A)', l.life], ['사용횟수', l.useCount], ['유/무상', l.paid], ['AETS 입고일', l.aetsInDay], ['현재 상태', l.status], ['세정상태', l.hist?.cleanState], ['AETS 출고일', l.aetsOutDay],
  ] : [];

  return (
    <div className="mr-window mr-detail">
      <div className="mr-card">
        <div className="mr-card-h">
          {fromList && <button type="button" className="btn btn-ghost" onClick={() => nav(-1)}>← 목록</button>}
          <b>LOT {lotId}</b>
          {l && <span className="mr-dim">{l.status}</span>}
          {d && <button type="button" className="btn btn-ghost" onClick={exportXlsx}>엑셀(이력)</button>}
          <button type="button" className="btn btn-ghost mr-x" onClick={() => window.close()}>닫기</button>
        </div>
        {err && <div className="mr-err">{err}</div>}
        {!d && !err && <div className="mr-empty">불러오는 중…</div>}
        {l && (
          <table className="mr-kv">
            <tbody>
              {Array.from({ length: Math.ceil(info.length / 3) }, (_, r) => info.slice(r * 3, r * 3 + 3)).map((row, r) => (
                <tr key={r}>
                  {row.map(([k, v]) => <Fragment key={k}><th>{k}</th><td>{v || <span className="mr-dim">—</span>}</td></Fragment>)}
                  {row.length < 3 && <td colSpan={(3 - row.length) * 2} />}
                </tr>
              ))}
              {l.comment && <tr><th>COMMENT</th><td colSpan={5} className="cmt">{l.comment}</td></tr>}
            </tbody>
          </table>
        )}
      </div>

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
    </div>
  );
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
