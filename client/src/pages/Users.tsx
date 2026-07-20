import { useEffect, useState, useCallback } from 'react';
import { api } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import type { UserFull } from '../api/types';
import './Users.css';

type PermKey = 'canManageFiles' | 'canManageNotices' | 'canManageVendors' | 'canManageSchedule'
  | 'canManageBroken' | 'canAccessEtcMenu' | 'canManageShiftBoard' | 'canManageInventory';

// 권한 정의: 서버 키 ↔ 화면 라벨 ↔ 영향 범위 설명
const PERMS: { key: PermKey; api: string; label: string; desc: string }[] = [
  { key: 'canManageFiles', api: 'files', label: '파일 관리', desc: '업무 파일 통합 관리(포탈) 등록/수정' },
  { key: 'canManageNotices', api: 'notices', label: '공지 관리', desc: '인수인계 화면의 Office 공지 등록/수정' },
  { key: 'canManageVendors', api: 'vendors', label: '업체 관리', desc: '업체 정보 등록/수정/삭제' },
  { key: 'canManageSchedule', api: 'schedule', label: '일정/교육 관리', desc: '팀 일정·교육·체크시트 항목·자재물류 인원' },
  { key: 'canManageBroken', api: 'broken', label: 'BROKEN 관리', desc: 'BROKEN 기록·교육·목표 등록/수정' },
  { key: 'canManageShiftBoard', api: 'shiftboard', label: '생산근무표', desc: '근무표 도장(교대 기록) 입력' },
  { key: 'canManageInventory', api: 'inventory', label: '재고 관리', desc: '재고 분석/편집·주간 마감·실사 확정' },
  { key: 'canAccessEtcMenu', api: 'etc', label: '기타 메뉴', desc: '기타(성적서 등) 메뉴 접근' },
];

// 역할 프리셋: 원클릭으로 권한 세트 적용 후 미세조정
const PRESETS: { name: string; desc: string; keys: PermKey[] }[] = [
  { name: '현장 작업자', desc: '조회만 (권한 없음)', keys: [] },
  { name: '현장 리더', desc: '근무표·재고·일정', keys: ['canManageShiftBoard', 'canManageInventory', 'canManageSchedule'] },
  { name: 'Office', desc: '파일·공지·업체·일정·재고', keys: ['canManageFiles', 'canManageNotices', 'canManageVendors', 'canManageSchedule', 'canManageInventory'] },
  { name: '전체 권한', desc: '8개 권한 모두 (관리자 아님)', keys: PERMS.map(p => p.key) },
];

interface AuditRow { id: number; targetUser: string; action: string; detail: string; byUser: string; createdAt: string; }

type Form = Omit<UserFull, 'id'> & { password: string };
const emptyForm: Form = {
  username: '', password: '', realName: '', teamName: '', jobTitle: '', email: '', phoneNumber: '',
  employeeNumber: '', hireDate: '', isResigned: false, resignDate: '', isAdmin: false,
  canManageFiles: false, canManageNotices: false, canManageVendors: false,
  canManageSchedule: false, canManageBroken: false, canAccessEtcMenu: false,
  canManageShiftBoard: false, canManageInventory: false,
};

export default function Users() {
  const { user: me } = useAuth();
  const [view, setView] = useState<'list' | 'matrix'>('list');
  const [all, setAll] = useState<UserFull[]>([]);
  const [tab, setTab] = useState<'active' | 'resigned'>('active');
  const [search, setSearch] = useState('');
  const [selId, setSelId] = useState<number | null>(null);
  const [adding, setAdding] = useState(false);
  const [form, setForm] = useState<Form>(emptyForm);
  const [err, setErr] = useState('');
  const [audit, setAudit] = useState<AuditRow[] | null>(null);
  const [teamFilter, setTeamFilter] = useState('');

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
  const teams = [...new Set(active.map(u => u.teamName).filter(Boolean))].sort();

  function pick(u: UserFull) {
    setAdding(false); setErr('');
    setSelId(u.id);
    setForm({ ...u, password: '' });
  }
  function startAdd() {
    setAdding(true); setSelId(null); setErr('');
    setForm(emptyForm);
  }
  function applyPreset(keys: PermKey[]) {
    const next = { ...form };
    for (const p of PERMS) next[p.key] = keys.includes(p.key);
    setForm(next);
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
  async function openAudit() { setAudit(await api.get<AuditRow[]>('/api/users/audit')); }

  // ── 매트릭스: 셀 토글 즉시 저장, 열 헤더 = 일괄 토글 ──
  const matrixUsers = active.filter(u => !teamFilter || u.teamName === teamFilter);
  async function toggleCell(u: UserFull, apiKey: string, value: boolean) {
    await api.post('/api/users/perms', { changes: [{ id: u.id, key: apiKey, value }] });
    load();
  }
  async function toggleColumn(apiKey: string, label: string, getter: (u: UserFull) => boolean) {
    const targets = matrixUsers.filter(u => u.username !== '1004' || apiKey !== 'isAdmin');
    const allOn = targets.every(getter);
    const value = !allOn;
    const scope = teamFilter ? `'${teamFilter}' 팀 ${targets.length}명` : `표시된 ${targets.length}명`;
    if (!confirm(`${scope}에게 [${label}] 권한을 ${value ? '일괄 부여' : '일괄 회수'}할까요?`)) return;
    await api.post('/api/users/perms', { changes: targets.map(u => ({ id: u.id, key: apiKey, value })) });
    load();
  }

  const isMaster = selected?.username === '1004' || form.username === '1004';
  const showForm = adding || selected;

  return (
    <div>
      <header className="pg-header">
        <div><h2>사용자 계정 관리</h2></div>
        <div className="um-viewtabs">
          <button className={view === 'list' ? 'on' : ''} onClick={() => setView('list')}>사용자 목록</button>
          <button className={view === 'matrix' ? 'on' : ''} onClick={() => setView('matrix')}>권한 매트릭스</button>
        </div>
        <button className="btn btn-ghost" onClick={openAudit}>변경 이력</button>
      </header>
      <div className="pg-body">
        {view === 'matrix' ? (
          <div className="um-matrix-wrap">
            <div className="um-matrix-bar">
              <select className="input um-team-sel" value={teamFilter} onChange={e => setTeamFilter(e.target.value)}>
                <option value="">전체 팀</option>
                {teams.map(t => <option key={t}>{t}</option>)}
              </select>
              <span className="um-hint">셀 클릭 = 즉시 적용 · 열 제목 클릭 = 표시 인원 일괄 부여/회수 · 변경은 당사자 재로그인 없이 바로 반영됩니다</span>
            </div>
            <div className="um-matrix-scroll">
              <table className="um-matrix">
                <thead>
                  <tr>
                    <th className="l">사용자</th>
                    <th className="admin-col" title="관리자 (전체 권한)" onClick={() => toggleColumn('isAdmin', '관리자', u => u.isAdmin)}>관리자</th>
                    {PERMS.map(p => (
                      <th key={p.key} title={`${p.desc}\n(클릭: 일괄 부여/회수)`} onClick={() => toggleColumn(p.api, p.label, u => u[p.key])}>{p.label}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {matrixUsers.map(u => (
                    <tr key={u.id} className={u.isAdmin ? 'is-admin' : ''}>
                      <td className="l">
                        <b>{u.realName}</b><small> {u.teamName || '-'} · {u.username}</small>
                      </td>
                      <td className="admin-col">
                        <input type="checkbox" checked={u.isAdmin} disabled={u.username === '1004'}
                          onChange={e => toggleCell(u, 'isAdmin', e.target.checked)} />
                      </td>
                      {PERMS.map(p => (
                        <td key={p.key}>
                          <input type="checkbox" checked={u.isAdmin || u[p.key]} disabled={u.isAdmin}
                            title={u.isAdmin ? '관리자는 모든 권한 보유' : p.desc}
                            onChange={e => toggleCell(u, p.api, e.target.checked)} />
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        ) : (
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
                    <div className="um-name">{u.realName}{u.isAdmin && <span className="um-adm-badge">관리자</span>}</div>
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
                  <div className="um-presets">
                    <span className="um-presets-l">프리셋:</span>
                    {PRESETS.map(p => (
                      <button key={p.name} type="button" className="um-preset" title={p.desc} disabled={isMaster}
                        onClick={() => applyPreset(p.keys)}>{p.name}</button>
                    ))}
                  </div>
                  <label className={`um-perm um-perm-admin ${form.isAdmin ? 'on' : ''}`} title="모든 메뉴·권한 + 사용자 관리 접근">
                    <input type="checkbox" disabled={isMaster || selected?.id === me?.id} checked={form.isAdmin}
                      onChange={e => setForm({ ...form, isAdmin: e.target.checked })} />
                    관리자 (전체 권한)
                  </label>
                  <div className="um-perms">
                    {PERMS.map(p => (
                      <label key={p.key} className={`um-perm ${form.isAdmin || form[p.key] ? 'on' : ''}`} title={p.desc}>
                        <input type="checkbox" disabled={isMaster || form.isAdmin} checked={form.isAdmin || !!form[p.key]} onChange={e => setForm({ ...form, [p.key]: e.target.checked })} />
                        <span>{p.label}<small>{p.desc}</small></span>
                      </label>
                    ))}
                  </div>
                  {isMaster && <div className="um-hint">최고 관리자는 모든 권한을 가집니다</div>}
                  {!isMaster && selected?.id === me?.id && <div className="um-hint">본인의 관리자 권한은 스스로 해제할 수 없습니다</div>}
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
        )}
      </div>

      {audit && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setAudit(null); }}>
          <div className="modal-box um-audit">
            <h3>사용자/권한 변경 이력 (최근 500)</h3>
            <div className="um-audit-wrap">
              <table className="um-audit-t">
                <thead><tr><th>일시</th><th>대상</th><th>구분</th><th>내용</th><th>수행자</th></tr></thead>
                <tbody>
                  {audit.map(a => <tr key={a.id}><td>{a.createdAt}</td><td>{a.targetUser}</td><td>{a.action}</td><td className="l">{a.detail}</td><td>{a.byUser}</td></tr>)}
                  {audit.length === 0 && <tr><td colSpan={5} style={{ padding: 20, color: '#94A3B8' }}>기록이 없습니다</td></tr>}
                </tbody>
              </table>
            </div>
            <div className="modal-actions"><button className="btn btn-primary" onClick={() => setAudit(null)}>닫기</button></div>
          </div>
        </div>
      )}
    </div>
  );
}

function F({ label, children }: { label: string; children: React.ReactNode }) {
  return <div className="um-field"><label>{label}</label>{children}</div>;
}
