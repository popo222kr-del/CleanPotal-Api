import { Fragment, useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { api } from '../../api/client';
import { downloadXlsx, type LotRow } from './mesReport';
import './MesReport.css';

// LOT 상세 새 창 — 세정 이력 조회에서 LOT ID 를 누르면 연다. 기존 MES 이력(시각·공정·설비·작업자·메모)과 측정값(입/출하 검사) 전체.

interface Hist { seq: string; time: string | null; code: string; oper: string; operName: string; oldOper: string; oldOperName: string; res: string; user: string; userName: string; comment: string }
interface Edc { seq: string; param: string; oper: string; operName: string; values: string[]; user: string; userName: string; time: string | null }
interface Detail { lot: LotRow; history: Hist[]; edc: Edc[] }

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
    ['SEC 반출라인', l.line], ['PM 설비명', l.pmEq], ['분임조', l.circle], ['공정명', l.procName], ['TYPE', l.matType], ['누적(A)', l.life],
    ['사용횟수', l.useCount], ['유/무상', l.paid], ['AETS 입고일', l.aetsInDay], ['현재 상태', l.status], ['AETS 출고일', l.aetsOutDay], ['SEC 출고일', l.secOutDay],
  ] : [];

  return (
    <div className="mr-window mr-detail">
      <div className="mr-card">
        <div className="mr-card-h">
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
        <div className="mr-card">
          <div className="mr-card-h"><b>측정값 (입/출하 검사)</b><span className="mr-dim">{d.edc.length}건</span></div>
          <div className="mr-scroll">
            <table className="mr-table mr-lots">
              <thead><tr><th>공정</th><th>항목</th><th>값</th><th>입력자</th><th>일시</th></tr></thead>
              <tbody>
                {d.edc.map((e, i) => (
                  <tr key={`${e.seq}-${e.param}-${i}`}>
                    <td>{e.operName}</td><td><b>{e.param}</b></td><td>{e.values.join(' / ')}</td><td title={e.user}>{e.userName}</td><td>{e.time}</td>
                  </tr>
                ))}
                {d.edc.length === 0 && <tr><td colSpan={5} className="mr-empty">측정값이 없습니다.</td></tr>}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}
