import { useEffect, useState, useCallback } from 'react';
import { api } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import type { Handover as HO } from '../api/types';
import './Handover.css';

const STATUSES = ['진행', '포장', '완료', '전체'];
const CATEGORIES = ['전체', 'QTZ', 'SEMES', '삼성'];
const NEXT_STATUS: Record<string, string> = { 진행: '포장', 포장: '완료' };

const emptyForm = { vendor: '', owner: '', content: '', inDate: '', outDate: '', deliveryMethod: '미정', memo: '' };

export default function Handover({ weekly = false }: { weekly?: boolean }) {
  const { user } = useAuth();
  const [items, setItems] = useState<HO[]>([]);
  const [counts, setCounts] = useState<Record<string, number>>({});
  const [status, setStatus] = useState('진행');
  const [category, setCategory] = useState('전체');
  const [search, setSearch] = useState('');
  const [modal, setModal] = useState(false);
  const [editId, setEditId] = useState<number | null>(null);
  const [form, setForm] = useState(emptyForm);

  const load = useCallback(async () => {
    const q = `?status=${encodeURIComponent(status)}&category=${encodeURIComponent(category)}&search=${encodeURIComponent(search)}&weekly=${weekly}`;
    setItems(await api.get<HO[]>(`/api/handover${q}`));
    setCounts(await api.get<Record<string, number>>(`/api/handover/counts?weekly=${weekly}`));
  }, [status, category, search, weekly]);

  useEffect(() => { load(); }, [load]);

  function openAdd() {
    setEditId(null);
    setForm({ ...emptyForm, owner: user?.realName ?? '' });
    setModal(true);
  }
  function openEdit(h: HO) {
    setEditId(h.id);
    setForm({
      vendor: h.vendor, owner: h.owner, content: h.content,
      inDate: h.inDate ?? '', outDate: h.outDate ?? '',
      deliveryMethod: h.deliveryMethod, memo: h.memo,
    });
    setModal(true);
  }
  async function save(e: React.FormEvent) {
    e.preventDefault();
    const body = {
      ...form,
      inDate: form.inDate || null,
      outDate: form.outDate || null,
      isWeekly: weekly,
    };
    if (editId) await api.put(`/api/handover/${editId}`, body);
    else await api.post('/api/handover', body);
    setModal(false);
    load();
  }
  async function changeStatus(h: HO, newStatus: string) {
    await api.patch(`/api/handover/${h.id}/status`, { status: newStatus });
    load();
  }
  async function remove(h: HO) {
    if (!confirm('삭제하시겠습니까?')) return;
    await api.del(`/api/handover/${h.id}`);
    load();
  }

  return (
    <div>
      <header className="pg-header">
        <div>
          <h2>{weekly ? '🧴 주간세정 현황' : '📦 인수인계'}</h2>
          <p>{weekly ? '주간팀 담당 업체의 진행 상황 및 배차 관리' : '출하 및 인수인계 관리'}</p>
        </div>
        <button className="btn btn-primary" onClick={openAdd}>+ {weekly ? '주간세정 등록' : '새 인수인계'}</button>
      </header>

      <div className="pg-body">
        <div className="ho-toolbar">
          {STATUSES.map(s => (
            <button key={s} className={`ho-tab ${status === s ? 'active' : ''}`} onClick={() => setStatus(s)}>
              {s}{s !== '전체' && counts[s] != null && <span className="ho-badge">{counts[s]}</span>}
            </button>
          ))}
          <span className="ho-sep" />
          {CATEGORIES.map(c => (
            <button key={c} className={`ho-cat ${category === c ? 'active' : ''}`} onClick={() => setCategory(c)}>{c}</button>
          ))}
          <input className="ho-search" placeholder="업체/내용/담당자 검색"
            value={search} onChange={e => setSearch(e.target.value)} />
        </div>

        <table className="ho-table">
          <thead>
            <tr><th>분류</th><th>업체</th><th>내용</th><th>날짜</th><th>담당</th><th>진행률</th><th>상태</th><th>관리</th></tr>
          </thead>
          <tbody>
            {items.length === 0 && <tr><td colSpan={8} className="ho-empty">항목이 없습니다</td></tr>}
            {items.map(h => (
              <tr key={h.id}>
                <td><span className={`cat-badge ${h.category}`}>{h.category}</span></td>
                <td className="ho-vendor">{h.vendor}</td>
                <td className="ho-content">{h.content}</td>
                <td className="ho-dates">
                  {h.inDate && <>입고 {h.inDate}<br /></>}
                  {h.outDate && <>출고 {h.outDate}</>}
                </td>
                <td>{h.owner}</td>
                <td>
                  <div className="prog-wrap"><div className="prog-bar" style={{ width: `${h.progressPercent}%` }} /></div>
                  <span className="prog-txt">{h.progressPercent}%</span>
                </td>
                <td><span className={`status-badge s-${h.status}`}>{h.status}</span></td>
                <td>
                  <div className="ho-actions">
                    {NEXT_STATUS[h.status] && (
                      <button className="ho-sm" onClick={() => changeStatus(h, NEXT_STATUS[h.status])}>{NEXT_STATUS[h.status]}</button>
                    )}
                    <button className="ho-sm" onClick={() => openEdit(h)}>수정</button>
                    <button className="ho-sm danger" onClick={() => remove(h)}>삭제</button>
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
            <h3>{editId ? '수정' : weekly ? '주간세정 등록' : '새 인수인계'}</h3>
            <label>업체명</label>
            <input className="input" required value={form.vendor} onChange={e => setForm({ ...form, vendor: e.target.value })} placeholder="예: 삼성전자, SEMES" />
            <label>내용</label>
            <textarea className="input ta" required value={form.content} onChange={e => setForm({ ...form, content: e.target.value })} />
            <div className="row">
              <div><label>담당자</label><input className="input" required value={form.owner} onChange={e => setForm({ ...form, owner: e.target.value })} /></div>
              <div><label>출하 방식</label>
                <select className="input" value={form.deliveryMethod} onChange={e => setForm({ ...form, deliveryMethod: e.target.value })}>
                  {['미정', '배차', '택배', '업체 회수', '직접수령'].map(d => <option key={d}>{d}</option>)}
                </select>
              </div>
            </div>
            <div className="row">
              <div><label>입고일</label><input className="input" type="date" value={form.inDate} onChange={e => setForm({ ...form, inDate: e.target.value })} /></div>
              <div><label>출고일</label><input className="input" type="date" value={form.outDate} onChange={e => setForm({ ...form, outDate: e.target.value })} /></div>
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
