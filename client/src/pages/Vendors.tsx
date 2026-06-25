import { useEffect, useState, useCallback } from 'react';
import { api } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import type { Vendor } from '../api/types';
import './Vendors.css';

const emptyForm = { vendorName: '', category: '일반', isWeekly: false, contact: '', phone: '', note: '' };

export default function Vendors() {
  const { user } = useAuth();
  const canManage = !!(user?.isAdmin);  // perm=vendors는 토큰에 있으나 메뉴 노출은 admin 기준; 관리 API가 정책으로 보호됨
  const [list, setList] = useState<Vendor[]>([]);
  const [search, setSearch] = useState('');
  const [modal, setModal] = useState(false);
  const [editId, setEditId] = useState<number | null>(null);
  const [form, setForm] = useState(emptyForm);

  const load = useCallback(async () => {
    setList(await api.get<Vendor[]>(`/api/vendor?search=${encodeURIComponent(search)}`));
  }, [search]);
  useEffect(() => { load(); }, [load]);

  function openAdd() { setEditId(null); setForm(emptyForm); setModal(true); }
  function openEdit(v: Vendor) {
    setEditId(v.id);
    setForm({ vendorName: v.vendorName, category: v.category, isWeekly: v.isWeekly, contact: v.contact, phone: v.phone, note: v.note });
    setModal(true);
  }
  async function save(e: React.FormEvent) {
    e.preventDefault();
    if (editId) await api.put(`/api/vendor/${editId}`, form);
    else await api.post('/api/vendor', form);
    setModal(false); load();
  }
  async function remove(v: Vendor) {
    if (!confirm(`'${v.vendorName}' 업체를 삭제할까요?`)) return;
    await api.del(`/api/vendor/${v.id}`); load();
  }

  return (
    <div>
      <header className="pg-header">
        <div><h2>🏢 업체 관리</h2><p>업체 마스터 · 주간세정 대상 분류</p></div>
        <input className="vd-search" placeholder="업체/분류/담당자 검색" value={search} onChange={e => setSearch(e.target.value)} />
        {canManage && <button className="btn btn-primary" onClick={openAdd}>+ 업체 등록</button>}
      </header>
      <div className="pg-body">
        <table className="vd-table">
          <thead><tr><th>업체명</th><th>분류</th><th>주간세정</th><th>담당자</th><th>연락처</th><th>비고</th>{canManage && <th>관리</th>}</tr></thead>
          <tbody>
            {list.length === 0 && <tr><td colSpan={canManage ? 7 : 6} className="vd-empty">등록된 업체가 없습니다</td></tr>}
            {list.map(v => (
              <tr key={v.id}>
                <td className="vd-name">{v.vendorName}</td>
                <td>{v.category}</td>
                <td>{v.isWeekly ? <span className="vd-weekly">주간세정</span> : <span className="vd-normal">일반</span>}</td>
                <td>{v.contact || '-'}</td>
                <td>{v.phone || '-'}</td>
                <td className="vd-note">{v.note || '-'}</td>
                {canManage && <td><div className="vd-actions"><button className="vd-sm" onClick={() => openEdit(v)}>수정</button><button className="vd-sm danger" onClick={() => remove(v)}>삭제</button></div></td>}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {modal && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setModal(false); }}>
          <form className="modal-box" onSubmit={save}>
            <h3>{editId ? '업체 수정' : '업체 등록'}</h3>
            <label>업체명</label>
            <input className="input" required value={form.vendorName} onChange={e => setForm({ ...form, vendorName: e.target.value })} />
            <label>분류</label>
            <input className="input" value={form.category} onChange={e => setForm({ ...form, category: e.target.value })} placeholder="일반 / QTZ / SEMES" />
            <label className="vd-check">
              <input type="checkbox" checked={form.isWeekly} onChange={e => setForm({ ...form, isWeekly: e.target.checked })} />
              주간세정 대상 업체
            </label>
            <div className="vd-row">
              <div><label>담당자</label><input className="input" value={form.contact} onChange={e => setForm({ ...form, contact: e.target.value })} /></div>
              <div><label>연락처</label><input className="input" value={form.phone} onChange={e => setForm({ ...form, phone: e.target.value })} /></div>
            </div>
            <label>비고</label>
            <input className="input" value={form.note} onChange={e => setForm({ ...form, note: e.target.value })} />
            <div className="modal-actions">
              <button type="button" className="btn btn-ghost" onClick={() => setModal(false)}>취소</button>
              <button type="submit" className="btn btn-primary">{editId ? '저장' : '등록'}</button>
            </div>
          </form>
        </div>
      )}
    </div>
  );
}
