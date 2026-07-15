import { useEffect, useState, useCallback } from 'react';
import { api } from '../api/client';
import type { UserFull } from '../api/types';
import './Users.css';

type PermKey = 'canManageFiles' | 'canManageNotices' | 'canManageVendors' | 'canManageSchedule'
  | 'canManageBroken' | 'canAccessEtcMenu' | 'canManageShiftBoard' | 'canManageInventory';
const PERMS: { key: PermKey; label: string }[] = [
  { key: 'canManageFiles', label: '파일 관리' },
  { key: 'canManageNotices', label: '공지 관리' },
  { key: 'canManageVendors', label: '업체 관리' },
  { key: 'canManageSchedule', label: '일정/교육 관리' },
  { key: 'canManageBroken', label: 'BROKEN 관리' },
  { key: 'canManageShiftBoard', label: '생산근무표' },
  { key: 'canManageInventory', label: '재고 관리' },
  { key: 'canAccessEtcMenu', label: '기타 메뉴' },
];

type Form = Omit<UserFull, 'id' | 'isAdmin'> & { password: string };
const emptyForm: Form = {
  username: '', password: '', realName: '', teamName: '', jobTitle: '', email: '', phoneNumber: '',
  employeeNumber: '', hireDate: '', isResigned: false, resignDate: '',
  canManageFiles: false, canManageNotices: false, canManageVendors: false,
  canManageSchedule: false, canManageBroken: false, canAccessEtcMenu: false,
  canManageShiftBoard: false, canManageInventory: false,
};

export default function Users() {
  const [all, setAll] = useState<UserFull[]>([]);
  const [tab, setTab] = useState<'active' | 'resigned'>('active');
  const [search, setSearch] = useState('');
  const [selId, setSelId] = useState<number | null>(null);
  const [adding, setAdding] = useState(false);
  const [form, setForm] = useState<Form>(emptyForm);
  const [err, setErr] = useState('');

  const load = useCallback(async () => {
    setAll(await api.get<UserFull[]>('/api/users?includeResigned=true'));
  }, []);
  useEffect(() => { load(); }, [load]);

  const active = all.filter(u => !u.isResigned);
  const resigned = all.filter(u => u.isResigned);
  let list = tab === 'active' ? active : resigned;
  if (search.trim()) {
    const q = search.trim().toLowerCase();
    list = list.filter(u => u.realName.toLowerCase().includes(q) || u.username.toLowerCase().includes(q) || u.teamName.toLowerCase().includes(q));
  }
  const selected = all.find(u => u.id === selId) ?? null;

  function pick(u: UserFull) {
    setAdding(false); setErr('');
    setSelId(u.id);
    setForm({ ...u, password: '' });
  }
  function startAdd() {
    setAdding(true); setSelId(null); setErr('');
    setForm(emptyForm);
  }
  async function save(e: React.FormEvent) {
    e.preventDefault();
    setErr('');
    try {
      if (adding) {
        const created = await api.post<UserFull>('/api/users', form);
        await load();
        setAdding(false);
        setSelId(created.id);
      } else if (selected) {
        const updated = await api.put<UserFull>(`/api/users/${selected.id}`, form);
        await load();
        setSelId(updated.id);
      }
    } catch (e) {
      setErr(e instanceof Error ? e.message : '저장 실패');
    }
  }
  async function remove() {
    if (!selected || !confirm(`'${selected.realName}' 사용자를 삭제할까요?`)) return;
    try {
      await api.del(`/api/users/${selected.id}`);
      await load();
      setSelId(null);
    } catch (e) {
      setErr(e instanceof Error ? e.message : '삭제 실패');
    }
  }

  const isMaster = selected?.username === '1004' || form.username === '1004';
  const showForm = adding || selected;

  return (
    <div>
      <header className="pg-header"><div><h2>👥 사용자 계정 관리</h2></div></header>
      <div className="pg-body">
        <div className="um-layout">
          <div className="um-left">
            <div className="um-tabs">
              <button className={tab === 'active' ? 'active' : ''} onClick={() => setTab('active')}>재직 중 <span>{active.length}</span></button>
              <button className={tab === 'resigned' ? 'active' : ''} onClick={() => setTab('resigned')}>퇴사자 <span>{resigned.length}</span></button>
            </div>
            <input className="input um-search" placeholder="검색…" value={search} onChange={e => setSearch(e.target.value)} />
            <div className="um-list">
              {list.map(u => (
                <div key={u.id} className={`um-item ${selId === u.id ? 'active' : ''}`} onClick={() => pick(u)}>
                  <div className="um-avatar">{u.realName[0] ?? '?'}</div>
                  <div className="um-info">
                    <div className="um-name">{u.realName}</div>
                    <div className="um-meta">{u.teamName || '-'} · {u.jobTitle || '-'}</div>
                  </div>
                  <div className="um-uid">{u.username}</div>
                </div>
              ))}
              {list.length === 0 && <div className="um-no">사용자가 없습니다</div>}
            </div>
            {tab === 'active' && <button className="btn btn-primary um-add" onClick={startAdd}>+ 신규 사용자</button>}
          </div>

          <div className="um-right">
            {!showForm && <div className="um-empty"><div style={{ fontSize: 36 }}>👥</div><p>사용자를 선택하세요</p></div>}
            {showForm && (
              <form onSubmit={save}>
                <div className="um-detail-head">
                  <div className="um-avatar lg" style={adding ? { background: '#10B981' } : {}}>{adding ? '+' : (form.realName[0] ?? '?')}</div>
                  <div>
                    <div className="um-detail-name">{adding ? '신규 사용자' : form.realName}</div>
                    {form.teamName && <span className="um-team-badge">{form.teamName}</span>}
                  </div>
                </div>
                {err && <div className="um-err">{err}</div>}

                <div className="um-section">
                  <div className="um-section-t">기본 정보</div>
                  <div className="um-grid">
                    <F label={`아이디${adding ? ' * (4자+)' : ''}`}><input className="input" required value={form.username} readOnly={isMaster && !adding} onChange={e => setForm({ ...form, username: e.target.value })} /></F>
                    <F label={`비밀번호${adding ? ' *' : ' (변경 시 입력)'}`}><input className="input" type="password" required={adding} value={form.password} onChange={e => setForm({ ...form, password: e.target.value })} /></F>
                    <F label="이름 *"><input className="input" required value={form.realName} onChange={e => setForm({ ...form, realName: e.target.value })} /></F>
                    <F label="직위"><input className="input" value={form.jobTitle} onChange={e => setForm({ ...form, jobTitle: e.target.value })} /></F>
                    <F label="소속팀"><input className="input" value={form.teamName} onChange={e => setForm({ ...form, teamName: e.target.value })} placeholder="김팀 / 장팀 / Office" /></F>
                    <F label="사번"><input className="input" value={form.employeeNumber} onChange={e => setForm({ ...form, employeeNumber: e.target.value })} /></F>
                    <F label="입사일"><input className="input" type="date" value={form.hireDate} onChange={e => setForm({ ...form, hireDate: e.target.value })} /></F>
                    <F label="이메일"><input className="input" value={form.email} onChange={e => setForm({ ...form, email: e.target.value })} /></F>
                    <F label="전화번호"><input className="input" value={form.phoneNumber} onChange={e => setForm({ ...form, phoneNumber: e.target.value })} /></F>
                  </div>
                </div>

                <div className="um-section">
                  <div className="um-section-t">권한 설정</div>
                  <div className="um-perms">
                    {PERMS.map(p => (
                      <label key={p.key} className={`um-perm ${form[p.key] ? 'on' : ''}`}>
                        <input type="checkbox" disabled={isMaster} checked={!!form[p.key]} onChange={e => setForm({ ...form, [p.key]: e.target.checked })} />
                        {p.label}
                      </label>
                    ))}
                  </div>
                  {isMaster && <div className="um-hint">최고 관리자는 모든 권한을 가집니다</div>}
                </div>

                <div className="um-section">
                  <div className="um-section-t">퇴사 관리</div>
                  <div className="um-resign">
                    <label className={`um-perm ${form.isResigned ? 'on' : ''}`}>
                      <input type="checkbox" disabled={isMaster} checked={form.isResigned} onChange={e => setForm({ ...form, isResigned: e.target.checked })} /> 퇴사 처리
                    </label>
                    {form.isResigned && <input className="input" type="date" style={{ width: 160 }} value={form.resignDate} onChange={e => setForm({ ...form, resignDate: e.target.value })} />}
                  </div>
                </div>

                <div className="um-actions">
                  <button type="button" className="btn btn-ghost" onClick={() => { setAdding(false); setSelId(null); }}>취소</button>
                  {!adding && !isMaster && <button type="button" className="btn um-del" onClick={remove}>삭제</button>}
                  <button type="submit" className="btn btn-primary">{adding ? '추가' : '저장'}</button>
                </div>
              </form>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

function F({ label, children }: { label: string; children: React.ReactNode }) {
  return <div className="um-field"><label>{label}</label>{children}</div>;
}
