import { useEffect, useState, useCallback } from 'react';
import { api } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import type { UserFull, AccessLevel, OrgDept } from '../api/types';
import './Users.css';

type AreaKey = 'accessSchedule' | 'accessRoster' | 'accessHandover' | 'accessField' | 'accessOffice';

// 영역 정의: 서버 키 ↔ 라벨 ↔ 포함 범위
const AREAS: { key: AreaKey; api: string; label: string; desc: string }[] = [
  { key: 'accessSchedule', api: 'schedule', label: '일정관리', desc: '세정팀 일정 달력 · 자재물류 일정 편집' },
  { key: 'accessRoster', api: 'roster', label: '근무표', desc: '근무표 도장(교대) 입력' },
  { key: 'accessHandover', api: 'handover', label: '현장 인수인계', desc: '인수인계·주간세정·생산미팅·요청사항·스케줄보드·배차·공지·업체' },
  { key: 'accessField', api: 'field', label: '현장 점검', desc: '재고관리 · 설비 ICP-MS · 체크시트' },
  { key: 'accessOffice', api: 'office', label: 'OFFICE 업무', desc: '견적서·주간보고·BROKEN·교육·업무분장·포탈 파일' },
];
const LEVELS: { v: AccessLevel; label: string }[] = [
  { v: 0, label: '없음' }, { v: 1, label: '조회' }, { v: 2, label: '편집' },
];
const levelName = (v: number) => LEVELS.find(l => l.v === v)?.label ?? '?';

// 영역별 하위 메뉴 (사이드바 구조) — 개별 표시/숨김 지정용
const AREA_SUBS: Record<AreaKey, { to: string; label: string }[]> = {
  accessSchedule: [{ to: '/calendar', label: '세정팀 일정 달력' }],
  accessRoster: [],
  accessHandover: [
    { to: '/handover', label: '인수인계 현황' }, { to: '/weekly', label: '주간세정 현황' },
    { to: '/meeting', label: '생산미팅' }, { to: '/prodreq', label: '생산팀 요청사항' },
    { to: '/schedule-board', label: '스케줄 보드' },
  ],
  accessField: [
    { to: '/inventory', label: '재고관리' }, { to: '/icpms', label: '설비 ICP-MS' },
    { to: '/checklist', label: '체크시트' },
  ],
  accessOffice: [
    { to: '/portal', label: '업무 파일 통합 관리' }, { to: '/quotation', label: '업체 견적서' },
    { to: '/weekly-report', label: '주간보고' }, { to: '/broken', label: 'BROKEN 관리' },
    { to: '/edu-dashboard', label: '교육 현황 대시보드' }, { to: '/work-assignment', label: '개인별 업무 분장표' },
  ],
};
function parseHidden(s: string): Set<string> {
  try { const a = JSON.parse(s || '[]'); return new Set(Array.isArray(a) ? a.filter((x: unknown): x is string => typeof x === 'string') : []); }
  catch { return new Set(); }
}

// 역할 프리셋
const PRESETS: { name: string; desc: string; levels: Record<AreaKey, AccessLevel> }[] = [
  { name: '현장 작업자', desc: '인수인계·현장점검 편집, 나머지 조회', levels: { accessSchedule: 1, accessRoster: 1, accessHandover: 2, accessField: 2, accessOffice: 0 } },
  { name: '현장 리더', desc: '+ 일정·근무표 편집', levels: { accessSchedule: 2, accessRoster: 2, accessHandover: 2, accessField: 2, accessOffice: 0 } },
  { name: 'Office', desc: '전 영역 편집 (OFFICE 포함)', levels: { accessSchedule: 2, accessRoster: 2, accessHandover: 2, accessField: 2, accessOffice: 2 } },
  { name: '조회 전용', desc: '전 영역 조회만 (OFFICE 없음)', levels: { accessSchedule: 1, accessRoster: 1, accessHandover: 1, accessField: 1, accessOffice: 0 } },
];

interface AuditRow { id: number; targetUser: string; action: string; detail: string; byUser: string; createdAt: string; }

type Form = Omit<UserFull, 'id'> & { password: string };
const emptyForm: Form = {
  username: '', password: '', realName: '', department: '', teamName: '', jobTitle: '', email: '', phoneNumber: '',
  employeeNumber: '', hireDate: '', isResigned: false, resignDate: '', isAdmin: false,
  accessSchedule: 1, accessRoster: 1, accessHandover: 1, accessField: 1, accessOffice: 0,
  hiddenMenus: '[]',
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
  const [bulkLevel, setBulkLevel] = useState<AccessLevel>(1);
  const [teamMgr, setTeamMgr] = useState(false);
  const [org, setOrg] = useState<OrgDept[]>([]);
  const [matrixMode, setMatrixMode] = useState<'level' | 'menu'>('level');
  const [detailTab, setDetailTab] = useState<'perm' | 'info' | 'history'>('perm');
  const [dAudit, setDAudit] = useState<AuditRow[] | null>(null);

  const loadOrg = useCallback(async () => { setOrg(await api.get<OrgDept[]>('/api/users/org')); }, []);
  function openTeamMgr() { setTeamMgr(true); loadOrg(); }

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
    setDetailTab('perm');   // 권한 조정이 주 업무 → 권한 탭 우선
  }
  function startAdd() {
    setAdding(true); setSelId(null); setErr('');
    setForm(emptyForm);
    setDetailTab('info');   // 신규는 기본 정보부터
  }

  // 변경 이력 탭: 선택 사용자의 이력만 필터해 로드
  useEffect(() => {
    if (detailTab !== 'history' || !selected) return;
    api.get<AuditRow[]>('/api/users/audit').then(rows => {
      const rn = selected.realName, un = selected.username;
      setDAudit(rows.filter(a => a.targetUser.includes(rn) || a.targetUser.includes(un)));
    }).catch(() => setDAudit([]));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [detailTab, selId]);
  function applyPreset(levels: Record<AreaKey, AccessLevel>) {
    setForm(f => ({ ...f, ...levels }));
  }
  // 하위 메뉴 표시/숨김 토글 (체크=표시, 해제=숨김)
  function toggleMenu(route: string) {
    setForm(f => {
      const set = parseHidden(f.hiddenMenus);
      if (set.has(route)) set.delete(route); else set.add(route);
      return { ...f, hiddenMenus: JSON.stringify([...set]) };
    });
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

  // ── 매트릭스: 셀 클릭 = 없음→조회→편집 순환, 즉시 저장 ──
  const matrixUsers = active.filter(u => !teamFilter || u.teamName === teamFilter);
  async function cycleCell(u: UserFull, area: typeof AREAS[number]) {
    const next = ((u[area.key] + 1) % 3) as AccessLevel;
    await api.post('/api/users/perms', { changes: [{ id: u.id, key: area.api, value: next }] });
    load();
  }
  async function toggleAdmin(u: UserFull, value: boolean) {
    await api.post('/api/users/perms', { changes: [{ id: u.id, key: 'isAdmin', value: value ? 1 : 0 }] });
    load();
  }
  // 매트릭스 메뉴 모드: 하위 메뉴 표시/숨김 토글 (즉시 저장)
  async function toggleMenuCell(u: UserFull, route: string, show: boolean) {
    await api.post('/api/users/perms', { changes: [{ id: u.id, key: `menu:${route}`, value: show ? 1 : 0 }] });
    load();
  }
  // 메뉴 상세 매트릭스 컬럼 (영역 그룹 + 하위 메뉴)
  const MENU_GROUPS = AREAS.filter(a => AREA_SUBS[a.key].length > 0)
    .map(a => ({ area: a, subs: AREA_SUBS[a.key] }));
  async function applyColumn(area: typeof AREAS[number]) {
    const scope = teamFilter ? `'${teamFilter}' 팀 ${matrixUsers.length}명` : `표시된 ${matrixUsers.length}명`;
    if (!confirm(`${scope}의 [${area.label}] 등급을 '${levelName(bulkLevel)}'(으)로 일괄 적용할까요?`)) return;
    await api.post('/api/users/perms', {
      changes: matrixUsers.map(u => ({ id: u.id, key: area.api, value: bulkLevel })),
    });
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
        <button className="btn btn-ghost" onClick={openTeamMgr}>부서/팀 관리</button>
        <button className="btn btn-ghost" onClick={openAudit}>변경 이력</button>
      </header>
      <div className="pg-body">
        {view === 'matrix' ? (
          <div className="um-matrix-wrap">
            <div className="um-matrix-bar">
              <div className="um-mode">
                <button className={matrixMode === 'level' ? 'on' : ''} onClick={() => setMatrixMode('level')}>영역 등급</button>
                <button className={matrixMode === 'menu' ? 'on' : ''} onClick={() => setMatrixMode('menu')}>메뉴 표시</button>
              </div>
              <select className="input um-team-sel" value={teamFilter} onChange={e => setTeamFilter(e.target.value)}>
                <option value="">전체 팀</option>
                {teams.map(t => <option key={t}>{t}</option>)}
              </select>
              {matrixMode === 'level' ? (
                <>
                  <span className="um-flt-l">일괄 등급</span>
                  <select className="input um-lvl-sel" value={bulkLevel} onChange={e => setBulkLevel(Number(e.target.value) as AccessLevel)}>
                    {LEVELS.map(l => <option key={l.v} value={l.v}>{l.label}</option>)}
                  </select>
                  <span className="um-hint">셀 클릭 = 없음→조회→편집 순환 · 열 제목 클릭 = 표시 인원 일괄 등급 · 즉시 반영</span>
                </>
              ) : (
                <span className="um-hint">체크 = 메뉴 표시 / 해제 = 숨김 · 즉시 적용 · 영역 등급이 '없음'이면 그룹째 숨겨집니다</span>
              )}
            </div>
            <div className="um-matrix-scroll">
              {matrixMode === 'level' ? (
              <table className="um-matrix">
                <thead>
                  <tr>
                    <th className="l">사용자</th>
                    <th className="admin-col" title="관리자 = 전체 영역 편집 + 관리자 메뉴">관리자</th>
                    {AREAS.map(a => (
                      <th key={a.key} title={`${a.desc}\n(클릭: '${levelName(bulkLevel)}' 일괄 적용)`} onClick={() => applyColumn(a)}>{a.label}</th>
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
                          onChange={e => toggleAdmin(u, e.target.checked)} />
                      </td>
                      {AREAS.map(a => (
                        <td key={a.key}>
                          {u.isAdmin
                            ? <span className="um-lvl lv2 fixed">편집</span>
                            : <button className={`um-lvl lv${u[a.key]}`} title={`${a.desc} — 클릭하여 변경`}
                                onClick={() => cycleCell(u, a)}>{levelName(u[a.key])}</button>}
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
              ) : (
              <table className="um-matrix um-mmatrix">
                <thead>
                  <tr>
                    <th className="l" rowSpan={2}>사용자</th>
                    {MENU_GROUPS.map(g => <th key={g.area.key} colSpan={g.subs.length} className="um-grp">{g.area.label}</th>)}
                  </tr>
                  <tr>
                    {MENU_GROUPS.flatMap(g => g.subs.map(s => (
                      <th key={s.to} className="um-subcol">{s.label}</th>
                    )))}
                  </tr>
                </thead>
                <tbody>
                  {matrixUsers.map(u => {
                    const hidden = parseHidden(u.hiddenMenus);
                    return (
                    <tr key={u.id} className={u.isAdmin ? 'is-admin' : ''}>
                      <td className="l"><b>{u.realName}</b><small> {u.teamName || '-'} · {u.username}</small></td>
                      {MENU_GROUPS.flatMap(g => g.subs.map(s => {
                        const areaOff = !u.isAdmin && u[g.area.key] === 0;
                        const shown = u.isAdmin ? true : !hidden.has(s.to);
                        return (
                          <td key={s.to} className={`um-mcell ${areaOff ? 'off' : ''}`}>
                            <input type="checkbox" checked={shown} disabled={u.isAdmin || areaOff}
                              title={areaOff ? '영역 등급이 없음이라 그룹째 숨김' : (shown ? '표시 중 — 해제하면 숨김' : '숨김 — 체크하면 표시')}
                              onChange={e => toggleMenuCell(u, s.to, e.target.checked)} />
                          </td>
                        );
                      }))}
                    </tr>
                  );})}
                </tbody>
              </table>
              )}
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
                    <div className="um-meta">{[u.department, u.teamName, u.jobTitle].filter(Boolean).join(' · ') || '-'}</div>
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
                {/* 상단 요약 바 (고정) + 탭 */}
                <div className="um-dtop">
                  <div className="um-dhead">
                    <div className="um-avatar lg" style={adding ? { background: '#4E9D77' } : {}}>{adding ? '+' : (form.realName[0] ?? '?')}</div>
                    <div className="um-dhead-info">
                      <div className="um-dhead-name">
                        {adding ? '신규 사용자' : (form.realName || '이름 없음')}
                        {form.isAdmin && <span className="um-adm-badge">관리자</span>}
                      </div>
                      <div className="um-dhead-meta">
                        {[form.department, form.teamName, form.jobTitle].filter(Boolean).join(' · ') || '소속 미지정'}
                        {!adding && <span className="um-dhead-uid"> · {form.username}</span>}
                      </div>
                    </div>
                    <div className="um-dhead-acts">
                      <button type="button" className="btn btn-ghost" onClick={() => { setAdding(false); setSelId(null); }}>취소</button>
                      {!adding && !isMaster && <button type="button" className="btn um-del" onClick={remove}>삭제</button>}
                      <button type="submit" className="btn btn-primary">{adding ? '추가' : '저장'}</button>
                    </div>
                  </div>
                  <div className="um-dtabs">
                    <button type="button" className={detailTab === 'perm' ? 'on' : ''} onClick={() => setDetailTab('perm')}>권한 설정</button>
                    <button type="button" className={detailTab === 'info' ? 'on' : ''} onClick={() => setDetailTab('info')}>기본 정보</button>
                    {!adding && <button type="button" className={detailTab === 'history' ? 'on' : ''} onClick={() => setDetailTab('history')}>변경 이력</button>}
                  </div>
                </div>
                {err && <div className="um-err">{err}</div>}

                {/* 권한 설정 탭 */}
                {detailTab === 'perm' && (
                <div className="um-section">
                  <div className="um-section-t">권한 설정 <small className="um-hint-inline">영역별 없음/조회/편집 + 하위 메뉴 표시/숨김을 개별 지정합니다</small></div>
                  <div className="um-presets">
                    <span className="um-presets-l">프리셋:</span>
                    {PRESETS.map(p => (
                      <button key={p.name} type="button" className="um-preset" title={p.desc} disabled={isMaster}
                        onClick={() => applyPreset(p.levels)}>{p.name}</button>
                    ))}
                  </div>
                  <label className={`um-perm um-perm-admin ${form.isAdmin ? 'on' : ''}`} title="모든 영역 편집 + 사용자 관리 접근">
                    <input type="checkbox" disabled={isMaster || selected?.id === me?.id} checked={form.isAdmin}
                      onChange={e => setForm({ ...form, isAdmin: e.target.checked })} />
                    관리자 (전체 권한)
                  </label>
                  <div className="um-areas">
                    {AREAS.map(a => {
                      const subs = AREA_SUBS[a.key];
                      const effLevel = form.isAdmin ? 2 : form[a.key];
                      const hidden = parseHidden(form.hiddenMenus);
                      return (
                      <div key={a.key} className="um-area">
                        <div className="um-area-row">
                          <div className="um-area-info">
                            <b>{a.label}</b>
                            <small>{a.desc}</small>
                          </div>
                          <div className="um-area-seg">
                            {LEVELS.map(l => (
                              <button key={l.v} type="button" disabled={isMaster || form.isAdmin}
                                className={`um-seg lv${l.v} ${effLevel === l.v ? 'on' : ''}`}
                                onClick={() => setForm({ ...form, [a.key]: l.v })}>{l.label}</button>
                            ))}
                          </div>
                        </div>
                        {subs.length > 0 && (
                          <div className={`um-subs ${effLevel === 0 ? 'off' : ''}`}>
                            <span className="um-subs-l">표시 메뉴</span>
                            {subs.map(s => {
                              const on = !hidden.has(s.to);
                              return (
                                <button key={s.to} type="button"
                                  className={`um-subchip ${on ? 'on' : ''}`}
                                  disabled={isMaster || form.isAdmin || effLevel === 0}
                                  title={on ? '표시 중 — 클릭하면 이 사용자에게 숨김' : '숨김 — 클릭하면 표시'}
                                  onClick={() => toggleMenu(s.to)}>
                                  <span className="um-subchk">{on ? '✓' : ''}</span>{s.label}
                                </button>
                              );
                            })}
                          </div>
                        )}
                      </div>
                    );})}
                  </div>
                  {isMaster && <div className="um-hint">최고 관리자는 모든 권한을 가집니다</div>}
                  {!isMaster && selected?.id === me?.id && <div className="um-hint">본인의 관리자 권한은 스스로 해제할 수 없습니다</div>}
                </div>
                )}

                {/* 기본 정보 탭 */}
                {detailTab === 'info' && (
                <>
                <div className="um-section">
                  <div className="um-section-t">기본 정보</div>
                  <div className="um-grid um-grid3">
                    <F label={`아이디${adding ? ' * (4자+)' : ''}`}><input className="input" required value={form.username} readOnly={isMaster && !adding} onChange={e => setForm({ ...form, username: e.target.value })} /></F>
                    <F label={`비밀번호${adding ? ' *' : ' (변경 시 입력)'}`}><input className="input" type="password" required={adding} value={form.password} onChange={e => setForm({ ...form, password: e.target.value })} /></F>
                    <F label="이름 *"><input className="input" required value={form.realName} onChange={e => setForm({ ...form, realName: e.target.value })} /></F>
                    <F label="직위"><input className="input" value={form.jobTitle} onChange={e => setForm({ ...form, jobTitle: e.target.value })} /></F>
                    <F label="부서">
                      <input className="input" list="um-depts" value={form.department} onChange={e => setForm({ ...form, department: e.target.value })} placeholder="세정팀 / Office …" />
                      <datalist id="um-depts">{[...new Set(all.map(u => u.department).filter(Boolean))].map(d => <option key={d} value={d} />)}</datalist>
                    </F>
                    <F label="소속팀">
                      <input className="input" list="um-teams" value={form.teamName} onChange={e => setForm({ ...form, teamName: e.target.value })} placeholder="김팀 / 장팀 / Office" />
                      <datalist id="um-teams">{teams.map(t => <option key={t} value={t} />)}</datalist>
                    </F>
                    <F label="사번"><input className="input" value={form.employeeNumber} onChange={e => setForm({ ...form, employeeNumber: e.target.value })} /></F>
                    <F label="입사일"><input className="input" type="date" value={form.hireDate} onChange={e => setForm({ ...form, hireDate: e.target.value })} /></F>
                    <F label="이메일"><input className="input" value={form.email} onChange={e => setForm({ ...form, email: e.target.value })} /></F>
                    <F label="전화번호"><input className="input" value={form.phoneNumber} onChange={e => setForm({ ...form, phoneNumber: e.target.value })} /></F>
                  </div>
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
                </>
                )}

                {/* 변경 이력 탭 (선택 사용자) */}
                {detailTab === 'history' && (
                <div className="um-section">
                  <div className="um-section-t">변경 이력 <small className="um-hint-inline">이 사용자에 대한 최근 변경 기록</small></div>
                  <div className="um-dhist">
                    {dAudit === null && <div className="um-hint">불러오는 중…</div>}
                    {dAudit !== null && dAudit.length === 0 && <div className="um-hint">기록이 없습니다</div>}
                    {dAudit?.map(a => (
                      <div key={a.id} className="um-dhist-row">
                        <span className="um-dhist-date">{a.createdAt}</span>
                        <span className="um-dhist-act">{a.action}</span>
                        <span className="um-dhist-detail">{a.detail}</span>
                        <span className="um-dhist-by">{a.byUser}</span>
                      </div>
                    ))}
                  </div>
                </div>
                )}
              </form>
            )}
          </div>
        </div>
        )}
      </div>

      {teamMgr && (() => {
        const DEPT_NONE = '(부서 미지정)', TEAM_NONE = '(팀 미지정)';
        const reload = () => { loadOrg(); load(); };
        async function renameDept(dept: string) {
          const nv = prompt(`부서명 변경: ${dept} →`, dept === DEPT_NONE ? '' : dept);
          if (nv === null || !nv.trim() || nv.trim() === dept) return;
          await api.post('/api/users/dept-bulk', { oldDept: dept === DEPT_NONE ? '' : dept, newDept: nv.trim() });
          reload();
        }
        async function renameTeam(team: string) {
          const nv = prompt(`팀명 변경: ${team} →`, team === TEAM_NONE ? '' : team);
          if (!nv?.trim() || nv.trim() === team) return;
          await api.post('/api/users/team-bulk', { team: team === TEAM_NONE ? '' : team, newTeam: nv.trim(), newDepartment: null });
          reload();
        }
        async function moveTeam(team: string, curDept: string) {
          const nv = prompt(`'${team}' 팀을 이동할 부서:`, curDept === DEPT_NONE ? '' : curDept);
          if (nv === null) return;
          await api.post('/api/users/team-bulk', { team: team === TEAM_NONE ? '' : team, newTeam: null, newDepartment: nv.trim() });
          reload();
        }
        async function addDept() {
          const nv = prompt('추가할 부서명:');
          if (!nv?.trim()) return;
          try { await api.post('/api/users/org/add', { kind: 'dept', name: nv.trim(), parent: null }); reload(); }
          catch (e) { alert(e instanceof Error ? e.message : '추가 실패'); }
        }
        async function addTeam(dept: string) {
          const nv = prompt(`'${dept}' 부서에 추가할 팀명:`);
          if (!nv?.trim()) return;
          try { await api.post('/api/users/org/add', { kind: 'team', name: nv.trim(), parent: dept === DEPT_NONE ? '' : dept }); reload(); }
          catch (e) { alert(e instanceof Error ? e.message : '추가 실패'); }
        }
        async function delDept(dept: string) {
          if (!confirm(`'${dept}' 부서를 삭제할까요? (소속 인원이 있으면 삭제되지 않습니다)`)) return;
          try { await api.post('/api/users/org/delete', { kind: 'dept', name: dept, parent: null }); reload(); }
          catch (e) { alert(e instanceof Error ? e.message : '삭제 실패'); }
        }
        async function delTeam(team: string, dept: string) {
          if (!confirm(`'${team}' 팀을 삭제할까요? (소속 인원이 있으면 삭제되지 않습니다)`)) return;
          try { await api.post('/api/users/org/delete', { kind: 'team', name: team, parent: dept === DEPT_NONE ? '' : dept }); reload(); }
          catch (e) { alert(e instanceof Error ? e.message : '삭제 실패'); }
        }
        return (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setTeamMgr(false); }}>
          <div className="modal-box um-teammgr">
            <div className="um-tm-head">
              <h3>부서 · 팀 관리</h3>
              <button className="btn btn-primary um-mini" onClick={addDept}>+ 부서 추가</button>
            </div>
            <p className="um-hint" style={{ marginBottom: 12 }}>인원이 없어도 부서·팀을 미리 만들 수 있습니다. 이름 변경·부서 이동은 소속 인원 전체에 적용되며, 소속 인원이 있는 부서/팀은 삭제되지 않습니다.</p>
            <div className="um-orgtree">
              {org.map(dept => (
                <div key={dept.name} className="um-dept">
                  <div className="um-dept-head">
                    <div className="um-dept-title">
                      <span className="um-dept-name">{dept.name}{!dept.registered && dept.name !== DEPT_NONE && <em className="um-tag-auto">자동</em>}</span>
                      <span className="um-dept-meta">{dept.teams.length}팀 · {dept.teams.reduce((s, t) => s + t.members.length, 0)}명</span>
                    </div>
                    <div className="um-team-acts">
                      <button className="btn btn-ghost um-mini" onClick={() => addTeam(dept.name)}>+ 팀</button>
                      {dept.name !== DEPT_NONE && <button className="btn btn-ghost um-mini" onClick={() => renameDept(dept.name)}>이름</button>}
                      {dept.name !== DEPT_NONE && <button className="btn btn-ghost um-mini um-del-mini" onClick={() => delDept(dept.name)}>삭제</button>}
                    </div>
                  </div>
                  {dept.teams.length === 0 && <div className="um-team-empty">팀이 없습니다. "+ 팀"으로 추가하세요.</div>}
                  {dept.teams.map(team => (
                    <div key={team.name} className="um-teamrow">
                      <div className="um-team-top">
                        <b>{team.name}{!team.registered && team.name !== TEAM_NONE && <em className="um-tag-auto">자동</em>} <span className="um-team-cnt">{team.members.length}명</span></b>
                        <div className="um-team-acts">
                          {team.name !== TEAM_NONE && <button className="btn btn-ghost um-mini" onClick={() => renameTeam(team.name)}>이름</button>}
                          {team.name !== TEAM_NONE && <button className="btn btn-ghost um-mini" onClick={() => moveTeam(team.name, dept.name)}>이동</button>}
                          {team.name !== TEAM_NONE && <button className="btn btn-ghost um-mini um-del-mini" onClick={() => delTeam(team.name, dept.name)}>삭제</button>}
                        </div>
                      </div>
                      {team.members.length > 0 && (
                        <div className="um-team-members">
                          {team.members.map(m => <span key={m.id} className="um-mchip">{m.realName}{m.jobTitle && <i> {m.jobTitle}</i>}</span>)}
                        </div>
                      )}
                    </div>
                  ))}
                </div>
              ))}
            </div>
            <div className="modal-actions"><button className="btn btn-primary" onClick={() => setTeamMgr(false)}>닫기</button></div>
          </div>
        </div>
        );
      })()}

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
