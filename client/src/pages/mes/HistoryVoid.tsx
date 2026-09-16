import { useState } from 'react';
import { api } from '../../api/client';
import { useAccess } from '../../auth/useAccess';
import { dateTime, resultLabel, statusLabel, statusTone } from './lot';
import './Mes.css';

// MES 이력 삭제 — 공정 이력 한 건을 무효화한다.
// 행을 지우지 않고 "무효화됨" 만 표시한다. 무엇이 있었는지 지워 버리면 나중에 왜 그렇게 됐는지 알 수 없다.

type HistoryRow = {
  processHistoryId: number; lotId: number; lotNumber: string; productName: string; customerName: string;
  processName: string; worker: string; startedAt: string; completedAt: string | null;
  status: number; result: number | null; quantity: number; defectQuantity: number;
  attemptNumber: number; remarks: string | null; equipmentId: string | null; isVoided: boolean;
};

export default function MesHistoryVoid() {
  const canEdit = useAccess().canEditMes;
  const [lotNumber, setLotNumber] = useState('');
  const [rows, setRows] = useState<HistoryRow[]>([]);
  const [target, setTarget] = useState<HistoryRow | null>(null);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  const [isError, setIsError] = useState(false);
  const [searched, setSearched] = useState(false);

  async function search() {
    if (busy) return;
    setBusy(true); setMsg(null); setTarget(null);
    try {
      setRows(await api.get<HistoryRow[]>(`/api/mes/process-history?lotNumber=${encodeURIComponent(lotNumber.trim())}`));
      setSearched(true);
    } catch {
      setIsError(true); setMsg('이력을 조회하지 못했습니다.');
    } finally { setBusy(false); }
  }

  async function confirmVoid() {
    if (!target || busy) return;
    setBusy(true); setMsg(null);
    try {
      const r = await api.post<{ success: boolean; message: string }>(
        `/api/mes/process-history/${target.processHistoryId}/void`, { reason: reason.trim() || null });
      setIsError(!r.success); setMsg(r.message);
      setTarget(null); setReason('');
      if (r.success) {
        setRows(await api.get<HistoryRow[]>(`/api/mes/process-history?lotNumber=${encodeURIComponent(lotNumber.trim())}`));
      }
    } catch {
      setIsError(true); setMsg('무효화 중 문제가 발생했습니다.');
    } finally { setBusy(false); }
  }

  return (
    <div className="mes-page">
      <header className="pg-header">
        <div><h2>이력 삭제</h2></div>
        <input className="input mes-search" placeholder="LOT 번호" value={lotNumber}
               onChange={e => setLotNumber(e.target.value)}
               onKeyDown={e => { if (e.key === 'Enter') void search(); }} />
        <button className="btn btn-primary" onClick={() => void search()} disabled={busy}>조회</button>
        <span className="mes-stamp">{rows.length}건</span>
      </header>

      <div className="pg-body">
        <p className="mes-dim">이력을 지우지 않고 "무효화됨" 으로 표시합니다. 기록 자체는 남습니다.</p>
        {msg && <p className={`mes-alert ${isError ? 'error' : 'ok'}`}>{msg}</p>}

        {target && (
          <div className="mes-alert warn">
            <div className="mes-pre">
              <strong>{target.lotNumber}</strong> / {target.processName} / {dateTime(target.startedAt)} 이력을 무효화할까요?
            </div>
            <div className="mes-confirm">
              <input className="input" placeholder="사유(선택)" value={reason} onChange={e => setReason(e.target.value)} />
              <button className="btn btn-primary" onClick={() => void confirmVoid()} disabled={busy}>무효화</button>
              <button className="btn btn-ghost" onClick={() => { setTarget(null); setReason(''); }}>취소</button>
            </div>
          </div>
        )}

        <div className="mes-scroll tall">
          <table className="mes-table">
            <thead>
              <tr>
                <th>LOT 번호</th><th>제품명</th><th>업체</th><th>공정</th><th>작업자</th>
                <th>시작</th><th>완료</th><th>상태</th><th>결과</th>
                <th className="num">수량</th><th className="num">불량</th><th className="num">시도</th>
                <th>설비</th><th>비고</th><th>무효화</th><th></th>
              </tr>
            </thead>
            <tbody>
              {rows.map(h => (
                <tr key={h.processHistoryId} className={h.isVoided ? 'voided' : ''}>
                  <td>{h.lotNumber}</td>
                  <td>{h.productName}</td>
                  <td>{h.customerName}</td>
                  <td>{h.processName}</td>
                  <td>{h.worker}</td>
                  <td>{dateTime(h.startedAt)}</td>
                  <td>{h.completedAt ? dateTime(h.completedAt) : '-'}</td>
                  <td><span className={`mes-badge ${statusTone(h.status)}`}>{statusLabel(h.status)}</span></td>
                  <td>{resultLabel(h.result)}</td>
                  <td className="num">{h.quantity}</td>
                  <td className="num">{h.defectQuantity}</td>
                  <td className="num">{h.attemptNumber}</td>
                  <td>{h.equipmentId ?? ''}</td>
                  <td>{h.remarks ?? ''}</td>
                  <td>{h.isVoided ? '무효화됨' : ''}</td>
                  <td>
                    {!h.isVoided && canEdit && (
                      <button className="mes-sm danger" onClick={() => setTarget(h)} disabled={busy}>무효화</button>
                    )}
                  </td>
                </tr>
              ))}
              {rows.length === 0 && (
                <tr><td colSpan={16} className="mes-empty">
                  {busy ? '조회 중…' : searched ? '해당 LOT 의 이력이 없습니다.' : 'LOT 번호로 조회하세요.'}
                </td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
