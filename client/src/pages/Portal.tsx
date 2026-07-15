import { useEffect, useState, useCallback } from 'react';
import { api } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import type { PortalGroup } from '../api/types';
import './Portal.css';

const TYPE_LABEL: Record<string, string> = { folder: '폴더', excel: 'XLS', ppt: 'PPT', pdf: 'PDF', web: 'URL' };
const TYPE_OPTIONS = [
  { v: '', label: '자동' }, { v: 'folder', label: '폴더' }, { v: 'excel', label: 'XLS' },
  { v: 'ppt', label: 'PPT' }, { v: 'pdf', label: 'PDF' }, { v: 'web', label: 'URL' },
];

export default function Portal() {
  const { user } = useAuth();
  const canManage = !!(user?.isAdmin || user?.canManageFiles);
  const [groups, setGroups] = useState<PortalGroup[]>([]);
  const [search, setSearch] = useState('');
  const [toast, setToast] = useState('');
  const [itemModal, setItemModal] = useState<{ groupId: number; id?: number } | null>(null);
  const [form, setForm] = useState({ title: '', path: '', type: '' });

  const load = useCallback(async () => {
    setGroups(await api.get<PortalGroup[]>('/api/portal/groups'));
  }, []);
  useEffect(() => { load(); }, [load]);

  function showToast(msg: string) {
    setToast(msg);
    setTimeout(() => setToast(''), 2000);
  }
  function copyPath(path: string) {
    navigator.clipboard.writeText(path).then(() => showToast('경로가 복사되었습니다')).catch(() => prompt('경로 복사:', path));
  }

  async function addGroup() {
    const name = prompt('새 그룹 이름');
    if (!name?.trim()) return;
    await api.post('/api/portal/groups', { name: name.trim() });
    load();
  }
  async function renameGroup(g: PortalGroup) {
    const name = prompt('그룹 이름 수정', g.name);
    if (!name?.trim()) return;
    await api.put(`/api/portal/groups/${g.id}`, { name: name.trim() });
    load();
  }
  async function deleteGroup(g: PortalGroup) {
    if (!confirm(`[${g.name}] 그룹과 항목을 모두 삭제할까요?`)) return;
    await api.del(`/api/portal/groups/${g.id}`);
    load();
  }
  function openItemAdd(groupId: number) {
    setForm({ title: '', path: '', type: '' });
    setItemModal({ groupId });
  }
  function openItemEdit(groupId: number, it: { id: number; title: string; path: string; type: string }) {
    setForm({ title: it.title, path: it.path, type: it.type });
    setItemModal({ groupId, id: it.id });
  }
  async function saveItem(e: React.FormEvent) {
    e.preventDefault();
    if (!itemModal) return;
    const body = { groupId: itemModal.groupId, title: form.title, path: form.path, type: form.type || null };
    if (itemModal.id) await api.put(`/api/portal/items/${itemModal.id}`, body);
    else await api.post('/api/portal/items', body);
    setItemModal(null);
    load();
  }
  async function deleteItem(id: number) {
    if (!confirm('삭제할까요?')) return;
    await api.del(`/api/portal/items/${id}`);
    load();
  }

  const q = search.trim().toLowerCase();
  const filtered = groups
    .map(g => {
      const groupMatch = q && g.name.toLowerCase().includes(q);
      const items = q && !groupMatch
        ? g.items.filter(i => i.title.toLowerCase().includes(q) || i.path.toLowerCase().includes(q))
        : g.items;
      return { ...g, items };
    })
    .filter(g => !q || g.items.length > 0 || g.name.toLowerCase().includes(q));

  return (
    <div>
      <header className="pg-header">
        <div><h2>업무 파일 통합 관리</h2></div>
        <input className="pt-search" placeholder="검색…" value={search} onChange={e => setSearch(e.target.value)} />
        {canManage && <button className="btn btn-primary" onClick={addGroup}>+ 그룹</button>}
      </header>

      <div className="pg-body">
        <div className="pt-note">🔗 URL(웹 문서)은 클릭 시 바로 열립니다. 파일·폴더는 브라우저 보안상 바로 열 수 없어 <b>경로가 복사</b>되니, Windows 탐색기 주소창에 붙여넣기(Ctrl+V) 하세요.</div>
        {filtered.length === 0 && <div className="pt-empty">{q ? '검색 결과가 없습니다' : '등록된 바로가기가 없습니다'}</div>}
        <div className="pt-grid">
          {filtered.map(g => (
            <div key={g.id} className="pt-card">
              <div className="pt-card-head">
                <span className="pt-group-name">{g.name}</span>
                {canManage && (
                  <div className="pt-admin">
                    <button onClick={() => renameGroup(g)} title="수정">✎</button>
                    <button onClick={() => deleteGroup(g)} title="삭제" className="danger">×</button>
                  </div>
                )}
              </div>
              <div className="pt-divider" />
              <div className="pt-items">
                {g.items.length === 0 && <div className="pt-no-items">항목이 없습니다</div>}
                {g.items.map(it => (
                  <div key={it.id} className="pt-item">
                    <span className={`pt-badge ${it.type}`}>{TYPE_LABEL[it.type] ?? '폴더'}</span>
                    {it.type === 'web'
                      ? <a className="pt-title link" href={it.path} target="_blank" rel="noopener">{it.title}</a>
                      : <span className="pt-title" onClick={() => copyPath(it.path)} title="클릭하여 경로 복사">{it.title}</span>}
                    <div className="pt-item-actions">
                      {it.type === 'web'
                        ? <a className="pt-open" href={it.path} target="_blank" rel="noopener" title="열기">→</a>
                        : <button className="pt-open" onClick={() => copyPath(it.path)} title="경로 복사">⧉</button>}
                      {canManage && <>
                        <button className="pt-icon" onClick={() => openItemEdit(g.id, it)} title="수정">✎</button>
                        <button className="pt-icon danger" onClick={() => deleteItem(it.id)} title="삭제">×</button>
                      </>}
                    </div>
                  </div>
                ))}
              </div>
              {canManage && <button className="btn btn-ghost pt-add" onClick={() => openItemAdd(g.id)}>+ 바로가기 추가</button>}
            </div>
          ))}
        </div>
      </div>

      {itemModal && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setItemModal(null); }}>
          <form className="modal-box" onSubmit={saveItem}>
            <h3>{itemModal.id ? '바로가기 수정' : '바로가기 추가'}</h3>
            <label>버튼 이름</label>
            <input className="input" required value={form.title} onChange={e => setForm({ ...form, title: e.target.value })} />
            <label>경로 / URL</label>
            <input className="input" value={form.path} placeholder="https:// 또는 \\서버\경로" onChange={e => setForm({ ...form, path: e.target.value })} />
            <label>유형</label>
            <div className="pt-type-sel">
              {TYPE_OPTIONS.map(o => (
                <label key={o.v} className={form.type === o.v ? 'on' : ''}>
                  <input type="radio" name="ptype" checked={form.type === o.v} onChange={() => setForm({ ...form, type: o.v })} />
                  {o.label}
                </label>
              ))}
            </div>
            <div className="modal-actions">
              <button type="button" className="btn btn-ghost" onClick={() => setItemModal(null)}>취소</button>
              <button type="submit" className="btn btn-primary">저장</button>
            </div>
          </form>
        </div>
      )}

      {toast && <div className="pt-toast">{toast}</div>}
    </div>
  );
}
