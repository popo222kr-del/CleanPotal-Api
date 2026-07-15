import { useEffect, useState, useCallback } from 'react';
import { api } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import type { Notice as N } from '../api/types';
import './Notice.css';

export default function Notice() {
  const { user } = useAuth();
  const canManage = !!(user?.isAdmin || user?.canManageNotices);
  const [list, setList] = useState<N[]>([]);
  const [sel, setSel] = useState<N | 'new' | null>(null);
  const [form, setForm] = useState({ title: '', content: '' });

  const load = useCallback(async () => { setList(await api.get<N[]>('/api/notice')); }, []);
  useEffect(() => { load(); }, [load]);

  function openNew() { setSel('new'); setForm({ title: '', content: '' }); }
  function openView(n: N) { setSel(n); setForm({ title: n.title, content: n.content }); }
  async function save() {
    if (!form.title.trim()) { alert('제목을 입력하세요.'); return; }
    if (sel === 'new') await api.post('/api/notice', form);
    else if (sel) await api.put(`/api/notice/${sel.id}`, form);
    setSel(null); load();
  }
  async function del(id: number) {
    if (!confirm('이 공지를 삭제할까요?')) return;
    await api.del(`/api/notice/${id}`); setSel(null); load();
  }

  return (
    <div>
      <header className="pg-header">
        <div><h2>사무실 공지</h2></div>
        {canManage && <button className="btn btn-primary" onClick={openNew}>+ 공지 등록</button>}
      </header>
      <div className="pg-body">
        <div className="nt-layout">
          <div className="nt-list">
            {list.length === 0 && <div className="nt-empty">등록된 공지가 없습니다</div>}
            {list.map(n => (
              <button key={n.id} className={`nt-item ${sel !== 'new' && sel?.id === n.id ? 'active' : ''}`} onClick={() => openView(n)}>
                <div className="nt-item-t">{n.title}</div>
                <div className="nt-item-m">{n.author} · {n.createdAt?.slice(0, 10)}</div>
              </button>
            ))}
          </div>
          <div className="nt-detail">
            {!sel && <div className="nt-none"><div style={{ fontSize: 34 }}>📢</div><p>공지를 선택하세요</p></div>}
            {sel && (
              <div className="nt-form">
                <label>제목</label>
                <input className="input" value={form.title} readOnly={!canManage}
                  onChange={e => setForm({ ...form, title: e.target.value })} />
                <label>내용</label>
                <textarea className="input nt-ta" value={form.content} readOnly={!canManage}
                  onChange={e => setForm({ ...form, content: e.target.value })} />
                {canManage && (
                  <div className="nt-actions">
                    {sel !== 'new' && <button className="btn nt-del" onClick={() => del(sel.id)}>삭제</button>}
                    <div style={{ flex: 1 }} />
                    <button className="btn btn-ghost" onClick={() => setSel(null)}>취소</button>
                    <button className="btn btn-primary" onClick={save}>{sel === 'new' ? '등록' : '저장'}</button>
                  </div>
                )}
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
