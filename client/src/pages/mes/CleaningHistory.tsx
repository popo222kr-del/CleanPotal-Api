import { useCallback, useEffect, useState } from 'react';
import { api } from '../../api/client';
import { dateOnly, dateTime, statusLabel, statusTone } from './lot';
import './Mes.css';

// MES 세정 이력 조회 — [검사 · 공정 이력] / [감사 로그] 두 탭.
//
// 결과 표의 뒤쪽 열(입고·출고 파라미터 블록)은 제품마다 다르다. 어떤 열이 있는지는 서버가 행과
// 함께 내려주고 여기서는 그대로 그린다 — 화면이 열을 스스로 만들면 마스터에 없는 열이 생긴다.

type CustomerOption = { customerId: number; customerName: string };
type Leaf = { key: string; title: string; width: number };
type ColumnGroup = { title: string; isOutbound: boolean; leaves: Leaf[] };
type Row = {
  lotId: number; lotNumber: string; exportNumber: string | null; cleaningCode: string | null;
  productName: string; initialSerialNumber: string | null; serialNumber: string;
  line: string | null; pmEquipmentName: string | null; teamName: string | null;
  itemCategory: string | null; usageCount: number; receivedDate: string;
  currentStatus: number; passFail: string | null; shipDate: string | null;
  values: Record<string, string | null> | null;
};
type HistoryResult = { rows: Row[]; columnGroups: ColumnGroup[] };
type AuditLog = {
  auditLogId: number; occurredAt: string; actor: string; action: string;
  entityName: string; entityId: string; detail: string | null;
};

// MES 화면의 고정 열(순서 그대로).
const FIXED = [
  'LOT ID', '반출번호', '세정코드', '제품명', '반출 S/N', '반입 S/N', 'LINE', '업체명',
  '분임조', '품목 구분', '사용횟수', 'AETS 입고일', 'STATUS', '합/부', 'AETS 출고일',
];

const dash = (v: string | null | undefined) => (v && v.length > 0 ? v : '-');

export default function MesCleaningHistory() {
  const [tab, setTab] = useState<'history' | 'audit'>('history');

  const [customers, setCustomers] = useState<CustomerOption[]>([]);
  const [customerId, setCustomerId] = useState('');
  const [cleaningCode, setCleaningCode] = useState('');
  const [itemCode, setItemCode] = useState('');
  const [serialNumber, setSerialNumber] = useState('');
  const [exportNumber, setExportNumber] = useState('');
  const [dateFrom, setDateFrom] = useState('');
  const [dateTo, setDateTo] = useState('');
  const [result, setResult] = useState<HistoryResult>({ rows: [], columnGroups: [] });
  const [status, setStatus] = useState<string | null>(null);

  const [auditKeyword, setAuditKeyword] = useState('');
  const [auditFrom, setAuditFrom] = useState('');
  const [auditTo, setAuditTo] = useState('');
  const [audit, setAudit] = useState<AuditLog[]>([]);
  const [auditError, setAuditError] = useState<string | null>(null);

  const [busy, setBusy] = useState(false);

  // 조건을 인자로 받는다 — 상태를 읽게 두면 "처음 한 번" 효과가 타이핑마다 다시 돈다.
  const loadAudit = useCallback(async (keyword: string, from: string, to: string) => {
    setBusy(true);
    try {
      const q = new URLSearchParams();
      if (keyword.trim()) q.set('keyword', keyword.trim());
      if (from) q.set('dateFrom', from);
      if (to) q.set('dateTo', to);
      setAudit(await api.get<AuditLog[]>(`/api/mes/cleaning-history/audit?${q}`));
      setAuditError(null);
    } catch {
      setAuditError('감사 로그를 불러오지 못했습니다.');
    } finally { setBusy(false); }
  }, []);

  useEffect(() => {
    void (async () => {
      try { setCustomers(await api.get<CustomerOption[]>('/api/mes/cleaning-history/customers')); }
      catch { /* 업체 목록만 비어 있게 둔다 — 나머지 조건으로는 조회할 수 있다 */ }
      // MES 와 같이 감사 로그는 자동 조회, 검사·공정 이력은 [조회] 를 눌러야 채운다.
      await loadAudit('', '', '');
    })();
  }, [loadAudit]);

  async function search() {
    if (busy) return;
    setBusy(true);
    try {
      const q = new URLSearchParams();
      if (customerId) q.set('customerId', customerId);
      if (cleaningCode.trim()) q.set('cleaningCode', cleaningCode.trim());
      if (itemCode.trim()) q.set('itemCode', itemCode.trim());
      if (serialNumber.trim()) q.set('serialNumber', serialNumber.trim());
      if (exportNumber.trim()) q.set('exportNumber', exportNumber.trim());
      if (dateFrom) q.set('dateFrom', dateFrom);
      if (dateTo) q.set('dateTo', dateTo);

      const r = await api.get<HistoryResult>(`/api/mes/cleaning-history?${q}`);
      setResult(r);
      setStatus(r.rows.length === 0
        ? '조건에 맞는 이력이 없습니다. S/N · 공정 등 입력값을 확인해 주세요(출하 완료 제품도 이력에는 표시됩니다).'
        : `${r.rows.length}건 조회됨.`);
    } catch {
      setStatus('조회 중 문제가 발생했습니다.');
    } finally { setBusy(false); }
  }

  function reset() {
    setCustomerId(''); setCleaningCode(''); setItemCode('');
    setSerialNumber(''); setExportNumber(''); setDateFrom(''); setDateTo('');
    setResult({ rows: [], columnGroups: [] });
    setStatus(null);
  }

  const searchAudit = () => loadAudit(auditKeyword, auditFrom, auditTo);

  const leaves = result.columnGroups.flatMap(g => g.leaves);

  return (
    <div className="mes-page">
      <header className="pg-header"><div><h2>세정 이력 조회</h2></div></header>

      <div className="pg-body">
        <div className="mes-tabs">
          <button className={tab === 'history' ? 'active' : ''} onClick={() => setTab('history')}>검사 · 공정 이력</button>
          <button className={tab === 'audit' ? 'active' : ''} onClick={() => setTab('audit')}>감사 로그 (Audit Log)</button>
        </div>

        {tab === 'history' ? (
          <>
            <div className="mes-filters">
              <label><span>날짜</span>
                <span className="mes-daterange">
                  <input type="date" className="input" value={dateFrom} onChange={e => setDateFrom(e.target.value)} />
                  <span className="mes-dim">~</span>
                  <input type="date" className="input" value={dateTo} onChange={e => setDateTo(e.target.value)} />
                </span>
              </label>
              <label><span>업체명</span>
                <select value={customerId} onChange={e => setCustomerId(e.target.value)}>
                  <option value="">(전체)</option>
                  {customers.map(c => <option key={c.customerId} value={c.customerId}>{c.customerName}</option>)}
                </select>
              </label>
              <label><span>세정코드</span>
                <input className="input" value={cleaningCode} onChange={e => setCleaningCode(e.target.value)} /></label>
              <label><span>품목코드</span>
                <input className="input" value={itemCode} onChange={e => setItemCode(e.target.value)} /></label>
              <label><span>S/N</span>
                <input className="input" value={serialNumber} onChange={e => setSerialNumber(e.target.value)} /></label>
              <label><span>반출번호</span>
                <input className="input" value={exportNumber} onChange={e => setExportNumber(e.target.value)} /></label>
              <span className="mes-filters-actions">
                <button className="btn btn-ghost" onClick={reset} disabled={busy}>초기화</button>
                <button className="btn btn-primary" onClick={() => void search()} disabled={busy}>조회</button>
              </span>
            </div>

            <div className="mes-scroll tall">
              <table className="mes-table">
                <thead>
                  <tr>
                    {FIXED.map(h => <th key={h} rowSpan={2}>{h}</th>)}
                    {result.columnGroups.map(g => (
                      <th key={g.title} colSpan={g.leaves.length} className={g.isOutbound ? 'band-out' : 'band-in'}>
                        {g.title}
                      </th>
                    ))}
                  </tr>
                  <tr>
                    {leaves.map(l => <th key={l.key} title={l.title}>{l.title}</th>)}
                  </tr>
                </thead>
                <tbody>
                  {result.rows.map(r => (
                    <tr key={r.lotId}>
                      <td>{r.lotNumber}</td>
                      <td>{dash(r.exportNumber)}</td>
                      <td>{dash(r.cleaningCode)}</td>
                      <td>{r.productName}</td>
                      <td>{dash(r.initialSerialNumber)}</td>
                      <td>{r.serialNumber}</td>
                      <td>{dash(r.line)}</td>
                      <td>{dash(r.pmEquipmentName)}</td>
                      <td>{dash(r.teamName)}</td>
                      <td>{dash(r.itemCategory)}</td>
                      <td>{r.usageCount}</td>
                      <td>{dateTime(r.receivedDate)}</td>
                      <td><span className={`mes-badge ${statusTone(r.currentStatus)}`}>{statusLabel(r.currentStatus)}</span></td>
                      <td>{dash(r.passFail)}</td>
                      <td>{r.shipDate ? dateOnly(r.shipDate) : '-'}</td>
                      {leaves.map(l => <td key={l.key}>{dash(r.values?.[l.key])}</td>)}
                    </tr>
                  ))}
                  {result.rows.length === 0 && (
                    <tr><td colSpan={FIXED.length + leaves.length} className="mes-empty">
                      {busy ? '조회 중…' : '조건을 입력하고 [조회] 를 누르세요.'}
                    </td></tr>
                  )}
                </tbody>
              </table>
            </div>
            {status && <p className="mes-status">{status}</p>}
          </>
        ) : (
          <>
            <div className="mes-filters">
              <label><span>사용자 / 작업 / 대상 / 내용</span>
                <input className="input wide" value={auditKeyword} onChange={e => setAuditKeyword(e.target.value)}
                       onKeyDown={e => { if (e.key === 'Enter') void searchAudit(); }} /></label>
              <label><span>기간</span>
                <span className="mes-daterange">
                  <input type="date" className="input" value={auditFrom} onChange={e => setAuditFrom(e.target.value)} />
                  <span className="mes-dim">~</span>
                  <input type="date" className="input" value={auditTo} onChange={e => setAuditTo(e.target.value)} />
                </span>
              </label>
              <span className="mes-filters-actions">
                <button className="btn btn-primary" onClick={() => void searchAudit()} disabled={busy}>검색</button>
              </span>
            </div>

            {auditError && <p className="mes-alert error">{auditError}</p>}

            <div className="mes-scroll tall">
              <table className="mes-table">
                <thead>
                  <tr><th>시간</th><th>사용자</th><th>작업</th><th>대상</th><th>대상 ID</th><th>상세</th></tr>
                </thead>
                <tbody>
                  {audit.map(a => (
                    <tr key={a.auditLogId}>
                      <td>{dateTime(a.occurredAt)}</td>
                      <td>{a.actor}</td>
                      <td>{a.action}</td>
                      <td>{a.entityName}</td>
                      <td>{a.entityId}</td>
                      <td className="mes-clip" title={a.detail ?? ''}>{a.detail ?? ''}</td>
                    </tr>
                  ))}
                  {audit.length === 0 && (
                    <tr><td colSpan={6} className="mes-empty">{busy ? '조회 중…' : '감사 로그가 없습니다.'}</td></tr>
                  )}
                </tbody>
              </table>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
