import { useEffect, useState, useCallback } from 'react';
import { api } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import type { Dispatch as D } from '../api/types';
import './ProductMaster.css';

const blank = () => ({
  vendorName: '', outgoingDetails: '', incomingDetails: '', managerName: '',
  contactNumber: '', fullAddress: '', note: '',
});

export default function Dispatch() {
  const { user } = useAuth();
  const canManage = !!(user?.isAdmin || user?.canManageVendors);
  const [list, setList] = useState<D[]>([]);
  const [search, setSearch] = useState('');
  const [edit, setEdit] = useState<D | 'new' | null>(null);
  const [form, setForm] = useState(blank());

  const load = useCallback(async () => {
    setList(await api.get<D[]>(`/api/dispatch?search=${encodeURIComponent(search)}`));
  }, [search]);
  useEffect(() => { load(); }, [load]);

  function openNew() { setEdit('new'); setForm(blank()); }
  function openEdit(d: D) {
    setEdit(d);
    setForm({
      vendorName: d.vendorName, outgoingDetails: d.outgoingDetails, incomingDetails: d.incomingDetails,
      managerName: d.managerName, contactNumber: d.contactNumber, fullAddress: d.fullAddress, note: d.note,
    });
  }
  async function save() {
    if (edit === 'new') await api.post('/api/dispatch', form);
    else if (edit) await api.put(`/api/dispatch/${edit.id}`, form);
    setEdit(null); load();
  }
  async function del(id: number) {
    if (!confirm('이 배차를 삭제할까요?')) return;
    await api.del(`/api/dispatch/${id}`); setEdit(null); load();
  }

  return (
    <div>
      <header className="pg-header">
        <div><h2>🚚 배차 관리</h2><p>업체별 출고·입고 배차 기록</p></div>
      </header>
      <div className="pg-body">
        <div className="pm-toolbar">
          <input className="input pm-search" placeholder="업체/담당자/주소 검색" value={search} onChange={e => setSearch(e.target.value)} />
          <span className="pm-count">{list.length}건</span>
          {canManage && <button className="btn btn-primary" onClick={openNew}>+ 배차 등록</button>}
        </div>
        <div className="pm-tablewrap">
          <table className="pm-table">
            <thead><tr><th>업체명</th><th>출고 내역</th><th>입고 내역</th><th>담당자</th><th>연락처</th><th>주소</th><th>일자</th></tr></thead>
            <tbody>
              {list.length === 0 && <tr><td colSpan={7} className="pm-empty">배차 기록이 없습니다</td></tr>}
              {list.map(d => (
                <tr key={d.id} className="pm-row" onClick={() => canManage && openEdit(d)}>
                  <td><b>{d.vendorName}</b></td>
                  <td className="pm-path">{d.outgoingDetails}</td>
                  <td className="pm-path">{d.incomingDetails}</td>
                  <td>{d.managerName}</td>
                  <td>{d.contactNumber}</td>
                  <td className="pm-path">{d.fullAddress}</td>
                  <td className="pm-upd">{d.createDate?.slice(0, 10)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {edit && (
          <div className="pm-modal-bg" onClick={() => setEdit(null)}>
            <div className="pm-modal" onClick={e => e.stopPropagation()}>
              <div className="pm-modal-head"><h3>{edit === 'new' ? '배차 등록' : '배차 수정'}</h3><button className="pm-x" onClick={() => setEdit(null)}>✕</button></div>
              <div className="pm-modal-body">
                <FF l="업체명"><input className="input" value={form.vendorName} onChange={e => setForm({ ...form, vendorName: e.target.value })} /></FF>
                <FF l="출고 내역"><input className="input" value={form.outgoingDetails} onChange={e => setForm({ ...form, outgoingDetails: e.target.value })} /></FF>
                <FF l="입고 내역"><input className="input" value={form.incomingDetails} onChange={e => setForm({ ...form, incomingDetails: e.target.value })} /></FF>
                <FF l="담당자"><input className="input" value={form.managerName} onChange={e => setForm({ ...form, managerName: e.target.value })} /></FF>
                <FF l="연락처"><input className="input" value={form.contactNumber} onChange={e => setForm({ ...form, contactNumber: e.target.value })} /></FF>
                <FF l="주소"><input className="input" value={form.fullAddress} onChange={e => setForm({ ...form, fullAddress: e.target.value })} /></FF>
                <FF l="비고"><input className="input" value={form.note} onChange={e => setForm({ ...form, note: e.target.value })} /></FF>
              </div>
              <div className="pm-modal-foot">
                {edit !== 'new' && <button className="btn pm-del" onClick={() => del(edit.id)}>삭제</button>}
                <div style={{ flex: 1 }} />
                <button className="btn btn-ghost" onClick={() => setEdit(null)}>취소</button>
                <button className="btn btn-primary" onClick={save}>저장</button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

function FF({ l, children }: { l: string; children: React.ReactNode }) {
  return <div className="pm-field"><label>{l}</label>{children}</div>;
}
