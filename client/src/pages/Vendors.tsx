import { useEffect, useState, useCallback } from 'react';
import { api } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { useIsMobile } from '../hooks/useIsMobile';
import type { Vendor } from '../api/types';
import './Vendors.css';

const emptyForm = {
  vendorName: '', category: '일반', isWeekly: false, isFavorite: false,
  basePath: '', addresses: '', managers: '', contact: '', phone: '', note: '',
};

// 주소: [{IsMain, LocationName, FullAddress}], 담당자: [{ManagerName, ContactNumber}]
function pick(o: Record<string, unknown>, ...keys: string[]): string {
  for (const k of keys) { const v = o[k] ?? o[k[0].toLowerCase() + k.slice(1)]; if (v) return String(v); }
  return '';
}
function summarize(json: string): string {
  if (!json) return '';
  try {
    const v = JSON.parse(json);
    if (Array.isArray(v)) {
      return v.map(item => {
        if (typeof item === 'string') return item;
        const addr = pick(item, 'FullAddress');
        if (addr) { const loc = pick(item, 'LocationName'); return loc ? `${loc}: ${addr}` : addr; }
        const mgr = pick(item, 'ManagerName');
        if (mgr) { const tel = pick(item, 'ContactNumber'); return tel ? `${mgr} (${tel})` : mgr; }
        return Object.values(item).filter(Boolean).join(' / ');
      }).filter(Boolean).join(', ');
    }
    if (typeof v === 'object' && v) return Object.values(v).filter(Boolean).join(' / ');
    return String(v);
  } catch {
    return json;
  }
}

export default function Vendors() {
  const isMobile = useIsMobile();
  const { user } = useAuth();
  const canManage = !!(user?.isAdmin || user?.canManageVendors);
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
    setForm({
      vendorName: v.vendorName, category: v.category, isWeekly: v.isWeekly, isFavorite: v.isFavorite,
      basePath: v.basePath, addresses: v.addresses, managers: v.managers,
      contact: v.contact, phone: v.phone, note: v.note,
    });
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
  async function toggleFav(e: React.MouseEvent, v: Vendor) {
    e.stopPropagation();
    if (!canManage) return;
    await api.put(`/api/vendor/${v.id}`, { ...v, isFavorite: !v.isFavorite });
    load();
  }

  return (
    <div>
      <header className="pg-header">
        <div><h2>🏢 업체 관리</h2><p>업체 마스터 · 주소/담당자 · 주간세정 분류</p></div>
        <input className="vd-search" placeholder="업체/분류/담당자/주소 검색" value={search} onChange={e => setSearch(e.target.value)} />
        {canManage && <button className="btn btn-primary" onClick={openAdd}>+ 업체 등록</button>}
      </header>
      <div className="pg-body">
        {isMobile ? (
          <div className="vd-mlist">
            {list.length === 0 && <div className="vd-empty">등록된 업체가 없습니다</div>}
            {list.map(v => (
              <div key={v.id} className="vd-mcard">
                <div className="vd-mc-top">
                  <button className="vd-star" onClick={e => toggleFav(e, v)}>{v.isFavorite ? '★' : '☆'}</button>
                  <span className="vd-mc-name">{v.vendorName}</span>
                  {v.isWeekly ? <span className="vd-weekly">주간세정</span> : <span className="vd-normal">일반</span>}
                </div>
                {v.category && <div className="vd-mc-cat">{v.category}</div>}
                {summarize(v.addresses) && <div className="vd-mc-row">📍 {summarize(v.addresses)}</div>}
                {summarize(v.managers) && <div className="vd-mc-row">👤 {summarize(v.managers)}</div>}
                {canManage && (
                  <div className="vd-mc-foot">
                    <button className="vd-sm" onClick={() => openEdit(v)}>수정</button>
                    <button className="vd-sm danger" onClick={() => remove(v)}>삭제</button>
                  </div>
                )}
              </div>
            ))}
          </div>
        ) : (
        <div className="vd-table-wrap">
          <table className="vd-table">
            <thead><tr><th style={{ width: 36 }}>★</th><th>업체명</th><th>분류</th><th>주간세정</th><th>주소</th><th>담당자</th>{canManage && <th>관리</th>}</tr></thead>
            <tbody>
              {list.length === 0 && <tr><td colSpan={canManage ? 7 : 6} className="vd-empty">등록된 업체가 없습니다</td></tr>}
              {list.map(v => (
                <tr key={v.id}>
                  <td style={{ textAlign: 'center' }}><button className="vd-star" onClick={e => toggleFav(e, v)}>{v.isFavorite ? '★' : '☆'}</button></td>
                  <td className="vd-name">{v.vendorName}</td>
                  <td>{v.category}</td>
                  <td>{v.isWeekly ? <span className="vd-weekly">주간세정</span> : <span className="vd-normal">일반</span>}</td>
                  <td className="vd-note">{summarize(v.addresses) || '-'}</td>
                  <td className="vd-note">{summarize(v.managers) || '-'}</td>
                  {canManage && <td><div className="vd-actions"><button className="vd-sm" onClick={() => openEdit(v)}>수정</button><button className="vd-sm danger" onClick={() => remove(v)}>삭제</button></div></td>}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        )}
      </div>

      {modal && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setModal(false); }}>
          <form className="modal-box" onSubmit={save}>
            <h3>{editId ? '업체 수정' : '업체 등록'}</h3>
            <label>업체명</label>
            <input className="input" required value={form.vendorName} onChange={e => setForm({ ...form, vendorName: e.target.value })} />
            <label>분류</label>
            <input className="input" value={form.category} onChange={e => setForm({ ...form, category: e.target.value })} placeholder="일반 / QTZ / SEMES" />
            <div className="vd-checks">
              <label className="vd-check"><input type="checkbox" checked={form.isWeekly} onChange={e => setForm({ ...form, isWeekly: e.target.checked })} /> 주간세정 대상</label>
              <label className="vd-check"><input type="checkbox" checked={form.isFavorite} onChange={e => setForm({ ...form, isFavorite: e.target.checked })} /> 즐겨찾기</label>
            </div>
            <label>기본 경로</label>
            <input className="input" value={form.basePath} onChange={e => setForm({ ...form, basePath: e.target.value })} />
            <label>주소 <small>(여러 개면 JSON 배열)</small></label>
            <textarea className="input vd-ta" value={form.addresses} onChange={e => setForm({ ...form, addresses: e.target.value })} placeholder='예: ["주소1","주소2"]' />
            <label>담당자 <small>(여러 개면 JSON 배열)</small></label>
            <textarea className="input vd-ta" value={form.managers} onChange={e => setForm({ ...form, managers: e.target.value })} placeholder='예: ["홍길동 010-...","..."]' />
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
