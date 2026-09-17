import { useCallback, useEffect, useState } from 'react';
import { api } from '../../api/client';
import { dateTime } from './lot';
import { useOpenLotHistory } from './shell/useOpenLot';
import './Mes.css';

// MES HOLD 관리 — HOLD 이력 조회.
// 해제는 이 화면이 아니라 LOT 이 있는 OPER 화면의 Release TRAN 으로 한다(해제도 공정 이력에 남아야 한다).

type Hold = {
  holdId: number; lotId: number; lotNumber: string; productName: string; processName: string;
  raisedBy: string; raisedAt: string; reason: string;
  isReleased: boolean; releasedBy: string | null; releasedAt: string | null;
};

export default function MesHolds() {
  const openLot = useOpenLotHistory();
  const [rows, setRows] = useState<Hold[]>([]);
  const [onlyOpen, setOnlyOpen] = useState(true);
  const [busy, setBusy] = useState(true);
  const [err, setErr] = useState<string | null>(null);

  const load = useCallback(async (open: boolean) => {
    setBusy(true);
    try {
      setRows(await api.get<Hold[]>(`/api/mes/holds?onlyOpen=${open}`));
      setErr(null);
    } catch {
      setErr('HOLD 목록을 불러오지 못했습니다.');
    } finally { setBusy(false); }
  }, []);

  useEffect(() => { void load(onlyOpen); }, [load, onlyOpen]);

  return (
    <div className="mes-page">
      <header className="pg-header">
        <div><h2>HOLD 관리</h2></div>
        <label className="mes-check">
          <input type="checkbox" checked={onlyOpen} onChange={e => setOnlyOpen(e.target.checked)} />
          미해제만 보기
        </label>
        <span className="mes-stamp">{rows.length}건</span>
      </header>

      <div className="pg-body">
        {err && <p className="mes-alert error">{err}</p>}
        <p className="mes-dim">해제는 LOT 이 있는 공정(OPER) 화면의 Release TRAN 으로 합니다.</p>

        <div className="mes-scroll tall">
          <table className="mes-table">
            <thead>
              <tr>
                <th>LOT 번호</th><th>제품명</th><th>공정</th><th>제기자</th><th>제기시각</th>
                <th>사유</th><th>상태</th><th>해제시각</th>
              </tr>
            </thead>
            <tbody>
              {rows.map(h => (
                <tr key={h.holdId}>
                  <td>
                    <button className="mes-link" onClick={() => openLot(h.lotNumber)}>
                      {h.lotNumber}
                    </button>
                  </td>
                  <td>{h.productName}</td>
                  <td>{h.processName}</td>
                  <td>{h.raisedBy}</td>
                  <td>{dateTime(h.raisedAt)}</td>
                  <td>{h.reason}</td>
                  <td><span className={`mes-badge ${h.isReleased ? 'off' : 'hold'}`}>{h.isReleased ? '해제됨' : 'HOLD'}</span></td>
                  <td>{h.releasedAt ? dateTime(h.releasedAt) : '-'}</td>
                </tr>
              ))}
              {rows.length === 0 && (
                <tr><td colSpan={8} className="mes-empty">{busy ? '불러오는 중…' : 'HOLD 이력이 없습니다.'}</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
