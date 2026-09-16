import { useCallback, useEffect, useMemo, useState } from 'react';
import { api } from '../../../api/client';
import { useAccess } from '../../../auth/useAccess';
import '../Mes.css';

// 셋업 > 업체 관리.
// 업체는 지우지 않고 중지만 한다 — 과거 LOT 과 제품이 이 업체를 참조하고 있어서,
// 지우면 그 기록들이 가리키는 곳이 사라진다.

type Customer = {
  customerId: number; customerCode: string; customerName: string; exportPrefix: string;
  lineDefinitionId: number | null; lineCode: string | null; isActive: boolean;
};
type Line = { lineId: number; code: string; description: string };
type SetupData = { customers: Customer[]; lines: Line[] };
type Result = { success: boolean; message: string };

const emptyForm = { customerCode: '', customerName: '', exportPrefix: '', lineDefinitionId: '' };

export default function CustomerTab() {
  const canEdit = useAccess().canEditMes;
  const [data, setData] = useState<SetupData>({ customers: [], lines: [] });
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState<Customer | null>(null);
  const [form, setForm] = useState(emptyForm);
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  const [isError, setIsError] = useState(false);

  const load = useCallback(async () => {
    setData(await api.get<SetupData>('/api/mes/setup/customers'));
  }, []);

  useEffect(() => {
    void (async () => {
      try { await load(); }
      catch { setIsError(true); setMsg('업체 목록을 불러오는 중 문제가 발생했습니다.'); }
    })();
  }, [load]);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return data.customers;
    return data.customers.filter(c =>
      c.customerName.toLowerCase().includes(q) || c.customerCode.toLowerCase().includes(q));
  }, [data.customers, search]);

  function pick(c: Customer) {
    setSelected(c);
    setForm({
      customerCode: c.customerCode,
      customerName: c.customerName,
      exportPrefix: c.exportPrefix,
      // 이미 없어진 LINE 을 가리키고 있으면 비워 둔다 — 고를 수 없는 값을 보여 주지 않는다.
      lineDefinitionId: data.lines.some(l => l.lineId === c.lineDefinitionId) ? String(c.lineDefinitionId) : '',
    });
    setMsg(null);
  }

  function newForm() {
    setSelected(null); setForm(emptyForm); setMsg(null);
  }

  async function run(work: () => Promise<Result>, after?: () => void) {
    if (busy) return;
    setBusy(true); setMsg(null);
    try {
      const r = await work();
      setIsError(!r.success); setMsg(r.message);
      if (r.success) { await load(); after?.(); }
    } catch {
      setIsError(true); setMsg('처리 중 문제가 발생했습니다.');
    } finally { setBusy(false); }
  }

  const save = () => run(async () => {
    const body = {
      customerCode: form.customerCode.trim(),
      customerName: form.customerName.trim(),
      exportPrefix: form.exportPrefix.trim(),
      lineDefinitionId: form.lineDefinitionId ? Number(form.lineDefinitionId) : null,
    };
    return selected
      ? api.put<Result>(`/api/mes/setup/customers/${selected.customerId}`, body)
      : api.post<Result>('/api/mes/setup/customers', body);
  }, newForm);

  const toggle = (c: Customer) => run(() =>
    api.post<Result>(`/api/mes/setup/customers/${c.customerId}/active`, { isActive: !c.isActive }));

  return (
    <div className="mes-setup">
      <section>
        <div className="mes-title">업체 목록<span className="mes-count">{filtered.length}건</span></div>
        <input className="input mes-search" placeholder="업체명 / 코드로 검색"
               value={search} onChange={e => setSearch(e.target.value)} />
        <div className="mes-scroll tall" style={{ marginTop: 8 }}>
          <table className="mes-table">
            <thead>
              <tr><th>업체 코드</th><th>업체명</th><th>LINE</th><th>상태</th><th></th></tr>
            </thead>
            <tbody>
              {filtered.map(c => (
                <tr key={c.customerId} className={c.customerId === selected?.customerId ? 'picked' : ''}
                    onClick={() => pick(c)} style={{ cursor: 'pointer' }}>
                  <td>{c.customerCode}</td>
                  <td>{c.customerName}</td>
                  <td>{c.lineCode ?? '-'}</td>
                  <td><span className={`mes-badge ${c.isActive ? 'done' : 'off'}`}>{c.isActive ? '사용' : '중지'}</span></td>
                  <td onClick={e => e.stopPropagation()}>
                    {canEdit && (
                      <button className="mes-sm" onClick={() => void toggle(c)} disabled={busy}>
                        {c.isActive ? '중지' : '활성화'}
                      </button>
                    )}
                  </td>
                </tr>
              ))}
              {filtered.length === 0 && (
                <tr><td colSpan={5} className="mes-empty">등록된 업체가 없습니다.</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </section>

      <section>
        <div className="mes-title">{selected ? '업체 수정' : '업체 등록'}</div>
        <div className="mes-info">
          <div className="mes-form">
            <label><span>업체 코드</span>
              <input className="input" value={form.customerCode} disabled={!canEdit}
                     onChange={e => setForm(f => ({ ...f, customerCode: e.target.value }))} /></label>
            <label><span>업체명</span>
              <input className="input" value={form.customerName} disabled={!canEdit}
                     onChange={e => setForm(f => ({ ...f, customerName: e.target.value }))} /></label>
            <label><span>반출번호 약어</span>
              <input className="input" value={form.exportPrefix} disabled={!canEdit}
                     onChange={e => setForm(f => ({ ...f, exportPrefix: e.target.value }))} /></label>
            <label><span>LINE</span>
              <select value={form.lineDefinitionId} disabled={!canEdit}
                      onChange={e => setForm(f => ({ ...f, lineDefinitionId: e.target.value }))}>
                <option value="">-</option>
                {data.lines.map(l => <option key={l.lineId} value={l.lineId}>{l.description}</option>)}
              </select>
            </label>
          </div>

          {msg && <p className={`mes-alert ${isError ? 'error' : 'ok'}`}>{msg}</p>}
          {!canEdit && <p className="mes-dim">마스터 수정은 MES 편집 권한이 필요합니다.</p>}

          <div className="mes-form-actions">
            <button className="btn btn-ghost" onClick={newForm} disabled={busy}>새로 작성</button>
            <button className="btn btn-primary" onClick={() => void save()}
                    disabled={busy || !canEdit || !form.customerName.trim()}>저장</button>
          </div>
        </div>
      </section>
    </div>
  );
}
