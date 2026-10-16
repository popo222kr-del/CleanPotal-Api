import { useEffect, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { api } from '../../api/client';
import { toKeys } from '../../components/koreanKeys';
import type { LotRow } from './mesReport';
import './MesReport.css';

// LOT 현황 조회 — 기존 MES 'LOT 현황 조회'처럼 LOT ID · S/N · 반출번호로 찾는다(기간 없음).
// [조회]를 누르면 새 창이 열리고, 한 건이면 바로 LOT 상세, 여러 건이면 목록에서 고른다.

const FIELDS = [['lotId', 'LOT ID', '예: 1SB260929010P'], ['sn', 'S/N', '반출·반입 S/N'], ['outNo', '반출번호', '예: C0204899013 또는 C0204899013-1']] as const;
type Field = typeof FIELDS[number][0];

/** 입·출고 현황의 [LOT 현황 조회] 탭 — 조건만 넣고 새 창으로 */
export default function LotFindTab() {
  const [f, setF] = useState<Record<Field, string>>({ lotId: '', sn: '', outNo: '' });
  const ok = FIELDS.some(([k]) => f[k].trim().length >= 3);
  function open() {
    if (!ok) { alert('LOT ID · S/N · 반출번호 중 하나를 3글자 이상 넣으세요.'); return; }
    const p = new URLSearchParams();
    // 한글 입력기로 찍힌 바코드("1누261014004ㅔ")도 영문 키로
    FIELDS.forEach(([k]) => { const v = toKeys(f[k]); if (v) p.set(k, v); });
    const w = Math.min(1300, screen.availWidth - 40), h = Math.min(900, screen.availHeight - 60);
    // 클릭 안에서 바로 연다(응답을 기다린 뒤 열면 팝업 차단에 걸린다) — 찾기는 새 창이 한다.
    window.open(`/mes-report/lot-find?${p}`, '_blank', `width=${w},height=${h},left=${(screen.availWidth - w) / 2},top=30,resizable=yes,scrollbars=yes`);
  }
  return (
    <div className="mr-hist-filters mr-find">
      <div className="mr-find-row">
        {FIELDS.map(([k, label, ph]) => (
          <label key={k}><span>{label}</span>
            <input className="input" value={f[k]} placeholder={ph} onChange={e => setF(x => ({ ...x, [k]: e.target.value }))}
              onKeyDown={e => { if (e.key === 'Enter') open(); }} />
          </label>
        ))}
        <button type="button" className="btn btn-primary" onClick={open}>조회 (새 창)</button>
        <button type="button" className="btn btn-ghost" onClick={() => setF({ lotId: '', sn: '', outNo: '' })}>지우기</button>
      </div>
      <div className="mr-dim">하나만 넣어도 됩니다 · 앞부분만 맞아도 찾습니다 · 기간 제한 없음 · 한 건이면 바로 LOT 상세(공정 이력·측정값)가 열립니다.</div>
    </div>
  );
}

/** 새 창 — 찾아서 한 건이면 LOT 상세로 넘어가고, 여러 건이면 목록 */
export function LotFindWindow() {
  const [sp] = useSearchParams();
  const nav = useNavigate();
  const [rows, setRows] = useState<LotRow[] | null>(null);
  const [err, setErr] = useState('');
  const cond = FIELDS.filter(([k]) => sp.get(k)).map(([k, l]) => `${l} ${sp.get(k)}`).join(' · ');

  useEffect(() => {
    document.title = `LOT 현황 조회 - ${cond}`;
    api.get<LotRow[]>(`/api/mes-report/lot-search?${sp}`)
      .then(r => { if (r.length === 1) nav(`/mes-report/lot/${encodeURIComponent(r[0].lotId)}`, { replace: true }); else setRows(r); })
      .catch(e => setErr(e instanceof Error ? e.message : '찾지 못했습니다.'));
  }, [sp]);   // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <div className="mr-window mr-detail">
      <div className="mr-card mr-page">
        <div className="mr-card-h">
          <b>LOT 현황 조회</b><span className="mr-dim">{cond}</span>
          {rows && <span className="mr-dim">{rows.length.toLocaleString()}건{rows.length >= 300 ? ' (최대 300건 — 더 길게 넣어 보세요)' : ''}</span>}
          <button type="button" className="btn btn-ghost mr-x" onClick={() => window.close()}>닫기</button>
        </div>
        {err && <div className="mr-err">{err}</div>}
        {!rows && !err && <div className="mr-empty">찾는 중…</div>}
        {rows && (
          <div className="mr-scroll">
            <table className="mr-table mr-lots">
              <thead><tr><th>LOT ID</th><th>반출번호</th><th>반출 S/N</th><th>반입 S/N</th><th>PART'S 명</th><th>세정코드</th><th>고객사</th><th>AETS 입고일</th><th>현재 상태</th><th>AETS 출고일</th></tr></thead>
              <tbody>
                {rows.map(r => (
                  <tr key={r.lotId}>
                    <td><Link className="mr-num" to={`/mes-report/lot/${encodeURIComponent(r.lotId)}`} state={{ from: 'list' }}>{r.lotId}</Link></td>
                    <td>{r.outNo}</td><td>{r.outSn}</td><td>{r.inSn}</td><td className="w">{r.matDesc}</td><td>{r.matId}</td>
                    <td>{r.customer}</td><td>{r.aetsInDay}</td><td>{r.status}</td><td>{r.aetsOutDay}</td>
                  </tr>
                ))}
                {rows.length === 0 && <tr><td colSpan={10} className="mr-empty">찾은 LOT 가 없습니다.</td></tr>}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
