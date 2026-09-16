import { useCallback, useEffect, useState } from 'react';
import { api } from '../../api/client';
import { useAccess } from '../../auth/useAccess';
import { statusLabel, statusTone } from './lot';
import './Mes.css';

// MES Batch — 여러 LOT 을 하나로 묶거나 묶음을 푼다.

type Row = {
  lotId: number; lotNumber: string; cleaningCode: string | null; productName: string;
  customerName: string; currentProcessName: string; currentStatus: number; isBatch: boolean;
};

export default function MesBatch() {
  const canEdit = useAccess().canEditMes;
  const [keyword, setKeyword] = useState('');
  const [rows, setRows] = useState<Row[]>([]);
  const [sel, setSel] = useState<Set<number>>(new Set());
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  const [isError, setIsError] = useState(false);

  const load = useCallback(async (q: string) => {
    const list = await api.get<Row[]>(`/api/mes/batch?keyword=${encodeURIComponent(q.trim())}`);
    setRows(list);
    // 목록에서 사라진 LOT 은 선택도 푼다 — 안 보이는 것을 묶을 수는 없다.
    setSel(prev => new Set([...prev].filter(id => list.some(r => r.lotId === id))));
  }, []);

  const search = useCallback(async (q: string) => {
    setBusy(true); setMsg(null);
    try { await load(q); }
    catch { setIsError(true); setMsg('목록을 불러오지 못했습니다.'); }
    finally { setBusy(false); }
  }, [load]);

  // 들어오면 전체 목록을 한 번 보여 준다. 이후 조회는 [검색] 버튼이 한다.
  useEffect(() => { void search(''); }, [search]);

  async function act(path: string, label: string) {
    if (busy || sel.size === 0) return;
    setBusy(true); setMsg(null);
    try {
      const r = await api.post<{ success: boolean; message: string }>(path, { lotIds: [...sel] });
      setIsError(!r.success); setMsg(r.message);
      if (r.success) { setSel(new Set()); await load(keyword); }
    } catch {
      setIsError(true); setMsg(`${label} 중 문제가 발생했습니다.`);
    } finally { setBusy(false); }
  }

  return (
    <div className="mes-page">
      <header className="pg-header">
        <div><h2>Batch</h2></div>
        <input className="input mes-search" placeholder="LOT번호 / S/N / 제품명" value={keyword}
               onChange={e => setKeyword(e.target.value)}
               onKeyDown={e => { if (e.key === 'Enter') void search(keyword); }} />
        <button className="btn btn-ghost" onClick={() => void search(keyword)} disabled={busy}>검색</button>
        <button className="btn btn-primary" onClick={() => void act('/api/mes/batch', '묶기')}
                disabled={sel.size < 2 || busy || !canEdit}>선택 묶기 ({sel.size})</button>
        <button className="btn btn-ghost" onClick={() => void act('/api/mes/batch/unbatch', '해제')}
                disabled={sel.size === 0 || busy || !canEdit}>선택 해제</button>
        <span className="mes-stamp">{rows.length}건</span>
      </header>

      <div className="pg-body">
        {!canEdit && <p className="mes-alert warn">묶기·해제는 MES 편집 권한이 필요합니다.</p>}
        {msg && <p className={`mes-alert ${isError ? 'error' : 'ok'}`}>{msg}</p>}

        <div className="mes-scroll tall">
          <table className="mes-table">
            <thead>
              <tr>
                <th></th><th>LOT 번호</th><th>세정코드</th><th>제품명</th><th>업체</th>
                <th>현재 공정</th><th>상태</th><th>배치</th>
              </tr>
            </thead>
            <tbody>
              {rows.map(l => (
                <tr key={l.lotId}>
                  <td>
                    <input type="checkbox" checked={sel.has(l.lotId)} disabled={!canEdit}
                           onChange={e => setSel(prev => {
                             const next = new Set(prev);
                             if (e.target.checked) next.add(l.lotId); else next.delete(l.lotId);
                             return next;
                           })} />
                  </td>
                  <td>{l.lotNumber}</td>
                  <td>{l.cleaningCode ?? ''}</td>
                  <td>{l.productName}</td>
                  <td>{l.customerName}</td>
                  <td>{l.currentProcessName}</td>
                  <td><span className={`mes-badge ${statusTone(l.currentStatus)}`}>{statusLabel(l.currentStatus)}</span></td>
                  <td>{l.isBatch && <span className="mes-badge run">묶임</span>}</td>
                </tr>
              ))}
              {rows.length === 0 && (
                <tr><td colSpan={8} className="mes-empty">{busy ? '조회 중…' : '검색 결과가 없습니다.'}</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
