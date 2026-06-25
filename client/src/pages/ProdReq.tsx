import { useEffect, useState, useCallback } from 'react';
import { api } from '../api/client';
import type { ProdReq as PR } from '../api/types';
import './ProdReq.css';

const STATUSES = ['진행', '완료', '보류', '전체'];
const emptyForm = { requestDate: '', dueDate: '', category: '', location: '', requestDetail: '', actionDate: '', actionDetail: '', assignee: '' };

export default function ProdReq() {
  const [items, setItems] = useState<PR[]>([]);
  const [status, setStatus] = useState('진행');
  const [search, setSearch] = useState('');
  const [modal, setModal] = useState(false);
  const [editId, setEditId] = useState<number | null>(null);
  const [form, setForm] = useState(emptyForm);

  const load = useCallback(async () => {
    const q = `?status=${encodeURIComponent(status)}&search=${encodeURIComponent(search)}`;
    setItems(await api.get<PR[]>(`/api/prodreq${q}`));
  }, [status, search]);
  useEffect(() => { load(); }, [load]);

  function openAdd() {
    setEditId(null);
    setForm({ ...emptyForm, requestDate: new Date().toISOString().slice(0, 10) });
    setModal(true);
  }
  function openEdit(p: PR) {
    setEditId(p.id);
    setForm({
      requestDate: p.requestDate ?? '', dueDate: p.dueDate ?? '', category: p.category,
      location: p.location, requestDetail: p.requestDetail, actionDate: p.actionDate ?? '',
      actionDetail: p.actionDetail, assignee: p.assignee,
    });
    setModal(true);
  }
  async function save(e: React.FormEvent) {
    e.preventDefault();
    const body = {
      requestDate: form.requestDate || null, dueDate: form.dueDate || null,
      category: form.category, location: form.location, requestDetail: form.requestDetail,
      actionDate: form.actionDate || null, actionDetail: form.actionDetail, assignee: form.assignee,
    };
    if (editId) await api.put(`/api/prodreq/${editId}`, body);
    else await api.post('/api/prodreq', body);
    setModal(false); load();
  }
  async function changeStatus(p: PR, s: string) { await api.patch(`/api/prodreq/${p.id}/status`, { status: s }); load(); }
  async function remove(p: PR) { if (confirm('삭제하시겠습니까?')) { await api.del(`/api/prodreq/${p.id}`); load(); } }

  function dDay(p: PR): string {
    if (p.status === '완료') return '완료';
    if (p.status === '보류') return '보류';
    if (!p.dueDate) return '-';
    const days = Math.ceil((new Date(p.dueDate).getTime() - Date.now()) / 86400000);
    if (days < 0) return `D+${-days} 지연`;
    if (days === 0) return 'D-day';
    return `D-${days}`;
  }

  return (
    <div>
      <header className="pg-header">
        <div><h2>📨 생산팀 요청사항</h2><p>현장 요청 접수 및 조치 관리</p></div>
        <button className="btn btn-primary" onClick={openAdd}>+ 새 요청</button>
      </header>
      <div className="pg-body">
        <div className="pr-toolbar">
          {STATUSES.map(s => <button key={s} className={`pr-tab ${status === s ? 'active' : ''}`} onClick={() => setStatus(s)}>{s}</button>)}
          <input className="pr-search" placeholder="내용/위치/요청자 검색" value={search} onChange={e => setSearch(e.target.value)} />
        </div>
        <table className="pr-table">
          <thead><tr><th>요청일</th><th>구분</th><th>위치</th><th>요청 내용</th><th>요청자</th><th>기한</th><th>조치</th><th>상태</th><th>관리</th></tr></thead>
          <tbody>
            {items.length === 0 && <tr><td colSpan={9} className="pr-empty">요청이 없습니다</td></tr>}
            {items.map(p => (
              <tr key={p.id}>
                <td>{p.requestDate ?? '-'}</td>
                <td>{p.category || '-'}</td>
                <td>{p.location || '-'}</td>
                <td className="pr-detail">{p.requestDetail}</td>
                <td>{p.requester}</td>
                <td><span className={`pr-dday ${dDay(p).includes('지연') ? 'late' : ''}`}>{dDay(p)}</span></td>
                <td className="pr-action">{p.actionDetail || '-'}</td>
                <td><span className={`pr-status s-${p.status}`}>{p.status}</span></td>
                <td>
                  <div className="pr-actions">
                    {p.status !== '완료' && <button className="pr-sm" onClick={() => changeStatus(p, '완료')}>완료</button>}
                    {p.status === '진행' && <button className="pr-sm" onClick={() => changeStatus(p, '보류')}>보류</button>}
                    <button className="pr-sm" onClick={() => openEdit(p)}>수정</button>
                    <button className="pr-sm danger" onClick={() => remove(p)}>삭제</button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {modal && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setModal(false); }}>
          <form className="modal-box" onSubmit={save}>
            <h3>{editId ? '요청 수정' : '새 요청'}</h3>
            <div className="row">
              <div><label>요청일</label><input className="input" type="date" value={form.requestDate} onChange={e => setForm({ ...form, requestDate: e.target.value })} /></div>
              <div><label>완료 기한</label><input className="input" type="date" value={form.dueDate} onChange={e => setForm({ ...form, dueDate: e.target.value })} /></div>
            </div>
            <div className="row">
              <div><label>구분</label><input className="input" value={form.category} onChange={e => setForm({ ...form, category: e.target.value })} placeholder="설비/시설/안전 등" /></div>
              <div><label>위치</label><input className="input" value={form.location} onChange={e => setForm({ ...form, location: e.target.value })} /></div>
            </div>
            <label>요청 내용</label>
            <textarea className="input ta" required value={form.requestDetail} onChange={e => setForm({ ...form, requestDetail: e.target.value })} />
            <label>조치 내용</label>
            <textarea className="input ta" value={form.actionDetail} onChange={e => setForm({ ...form, actionDetail: e.target.value })} />
            <div className="row">
              <div><label>담당자</label><input className="input" value={form.assignee} onChange={e => setForm({ ...form, assignee: e.target.value })} /></div>
              <div><label>조치일</label><input className="input" type="date" value={form.actionDate} onChange={e => setForm({ ...form, actionDate: e.target.value })} /></div>
            </div>
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
