import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../../api/client';
import { dateTime } from './lot';
import './Mes.css';

// MES 재작업 관리 — 재작업 결정 이력 조회.

type Rework = {
  reworkId: number; lotId: number; lotNumber: string; productName: string; processName: string;
  attemptNumber: number; reason: string; decidedBy: string; decidedAt: string; remarks: string | null;
};

export default function MesReworks() {
  const nav = useNavigate();
  const [rows, setRows] = useState<Rework[]>([]);
  const [busy, setBusy] = useState(true);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    void (async () => {
      try { setRows(await api.get<Rework[]>('/api/mes/reworks')); }
      catch { setErr('재작업 목록을 불러오지 못했습니다.'); }
      finally { setBusy(false); }
    })();
  }, []);

  return (
    <div className="mes-page">
      <header className="pg-header">
        <div><h2>재작업 관리</h2></div>
        <span className="mes-stamp">{rows.length}건</span>
      </header>

      <div className="pg-body">
        {err && <p className="mes-alert error">{err}</p>}
        <p className="mes-dim">LOT 번호를 누르면 LOT 현황 조회로 이동합니다.</p>

        <div className="mes-scroll tall">
          <table className="mes-table">
            <thead>
              <tr>
                <th>LOT 번호</th><th>제품명</th><th>공정</th><th className="num">차수</th><th>사유</th>
                <th>결정자</th><th>결정시각</th><th>비고</th>
              </tr>
            </thead>
            <tbody>
              {rows.map(r => (
                <tr key={r.reworkId}>
                  <td>
                    <button className="mes-link" onClick={() => nav(`/mes/history?lot=${encodeURIComponent(r.lotNumber)}`)}>
                      {r.lotNumber}
                    </button>
                  </td>
                  <td>{r.productName}</td>
                  <td>{r.processName}</td>
                  <td className="num">{r.attemptNumber}</td>
                  <td>{r.reason}</td>
                  <td>{r.decidedBy}</td>
                  <td>{dateTime(r.decidedAt)}</td>
                  <td>{r.remarks ?? ''}</td>
                </tr>
              ))}
              {rows.length === 0 && (
                <tr><td colSpan={8} className="mes-empty">{busy ? '불러오는 중…' : '재작업 이력이 없습니다.'}</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
