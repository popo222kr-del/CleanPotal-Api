import { useEffect, useMemo, useState, useCallback } from 'react';
import { api } from '../api/client';
import Combo from '../components/Combo';
import { useAuth } from '../auth/AuthContext';
import { useIsMobile } from '../hooks/useIsMobile';
import type { UserFull, AccessLevel, OrgDept, OrgTree } from '../api/types';
import '../styles/member-list.css';
import './Users.css';

const DEPT_ALL = '전체';

// 조직도가 '없음' 을 나타내려고 쓰는 표시용 이름. 실제 값이 아니므로 드롭다운에 올리지 않는다.
const ORG_PLACEHOLDERS = ['(부서 미지정)', '(팀 미지정)'];

/** 부서 이름을 화면 표시용으로 정리한다. 비어 있으면 '(부서 미지정)'. */
function deptOf(v: string | undefined): string {
  const d = (v ?? '').trim();
  return d.length > 0 ? d : '(부서 미지정)';
}

type AreaKey = 'accessSchedule' | 'accessRoster' | 'accessHandover' | 'accessField' | 'accessOffice' | 'accessMes';

// 영역 정의: 서버 키 ↔ 라벨 ↔ 포함 범위
const AREAS: { key: AreaKey; api: string; label: string; desc: string }[] = [
  { key: 'accessSchedule', api: 'schedule', label: '일정관리', desc: '통합 일정 달력 · 자재물류 일정 편집' },
  { key: 'accessRoster', api: 'roster', label: '근무표', desc: '근무표 도장(교대) 입력' },
  { key: 'accessHandover', api: 'handover', label: '현장 인수인계', desc: '기타세정·주간세정·생산팀 인수인계·요청사항·스케줄보드·배차·공지·업체' },
  { key: 'accessField', api: 'field', label: '현장 점검', desc: '재고관리 · 설비 ICP-MS · 체크시트' },
  { key: 'accessOffice', api: 'office', label: 'OFFICE 업무', desc: '견적서·주간보고·BROKEN·교육·업무분장·포탈 파일' },
  { key: 'accessMes', api: 'mes', label: 'MES (생산관리)', desc: 'LOT 현황·공정(OPER)·전산등록·조회' },
];
// 직급(호칭). 서버의 CleanPotal.Core.JobRank.All 과 같은 순서를 쓴다.
// 직위(jobTitle = QA팀장·세정팀장 등 맡은 일)와는 별개 항목이다.
const RANKS = ['사원', '주임', '대리', '과장', '차장', '부장', '상무', '전무', '부사장', '사장'];

const LEVELS: { v: AccessLevel; label: string }[] = [
  { v: 0, label: '없음' }, { v: 1, label: '조회' }, { v: 2, label: '편집' },
];
const levelName = (v: number) => LEVELS.find(l => l.v === v)?.label ?? '?';

// 영역별 하위 메뉴 (사이드바 구조) — 개별 표시/숨김 지정용
const AREA_SUBS: Record<AreaKey, { to: string; label: string }[]> = {
  accessSchedule: [{ to: '/calendar', label: '통합 일정 달력' }],
  accessRoster: [],
  accessHandover: [
    { to: '/handover', label: '기타세정 현황' }, { to: '/weekly', label: '주간세정 현황' },
    { to: '/meeting', label: '생산팀 인수인계' }, { to: '/prodreq', label: '생산팀 요청사항' },
    { to: '/schedule-board', label: '스케줄 보드' },
  ],
  accessField: [
    { to: '/temp-humidity', label: '온·습도 모니터링' },
    { to: '/inventory', label: '재고관리' }, { to: '/icpms', label: '설비 ICP-MS' },
    { to: '/checklist', label: '체크시트' },
  ],
  accessOffice: [
    { to: '/portal', label: '업무 파일 통합 관리' }, { to: '/quotation', label: '업체 견적서' },
    { to: '/weekly-report', label: '주간보고' }, { to: '/broken', label: 'BROKEN 관리' },
    { to: '/edu-dashboard', label: '교육 현황 대시보드' }, { to: '/work-assignment', label: '개인별 업무 분장표' },
  ],
  // MES 는 화면이 많아 큰 묶음만 둔다 — OPER 9개까지 한 줄씩 넣으면 목록이 읽히지 않는다.
  accessMes: [
    { to: '/mes', label: 'MES Dash Board' }, { to: '/mes/scan', label: 'LOT 스캔' },
    { to: '/mes/register', label: 'CREATE (전산등록)' }, { to: '/mes/history', label: 'LOT 현황 조회' },
    { to: '/mes/setup', label: 'MES 셋업' },
  ],
};
// MES 세부 권한 — 영역 등급(없음/조회/편집)과 다른 축이다. 편집 등급을 준 작업자라도
// 마스터를 고치거나 지나간 공정을 무효화하는 것은 사람을 골라 켜 준다.
// 코드 문자열은 서버(MesPermissionCodes)와 글자까지 같아야 한다.
const MES_PERMS: { code: string; label: string; desc: string }[] = [
  { code: 'AdminCustomer', label: '업체 마스터', desc: '셋업 > 업체 등록·수정' },
  { code: 'AdminProduct', label: '제품 마스터', desc: '셋업 > 제품(세정코드)·레시피·검사 파라미터·단가·이미지' },
  { code: 'AdminProcess', label: '공정 마스터', desc: '셋업 > 공정 정의·공정 플로우' },
  { code: 'AdminCertificate', label: '성적서 관리', desc: '성적서 양식 등록' },
  { code: 'Rollback', label: '공정 무효화', desc: '이미 지나간 공정 이력을 무효 처리' },
  { code: 'AdminUserManagement', label: 'MES 사용자 관리', desc: '데스크톱판 MES 계정·권한' },
];
function parseMesPerms(s: string): Set<string> {
  return new Set((s || '').split(',').map(v => v.trim()).filter(v => v !== ''));
}

function parseHidden(s: string): Set<string> {
  try { const a = JSON.parse(s || '[]'); return new Set(Array.isArray(a) ? a.filter((x: unknown): x is string => typeof x === 'string') : []); }
  catch { return new Set(); }
}

// 역할 프리셋
const PRESETS: { name: string; desc: string; levels: Record<AreaKey, AccessLevel> }[] = [
  { name: '현장 작업자', desc: '인수인계·현장점검·MES 편집, 나머지 조회', levels: { accessSchedule: 1, accessRoster: 1, accessHandover: 2, accessField: 2, accessOffice: 0, accessMes: 2 } },
  { name: '현장 리더', desc: '+ 일정·근무표 편집', levels: { accessSchedule: 2, accessRoster: 2, accessHandover: 2, accessField: 2, accessOffice: 0, accessMes: 2 } },
  { name: 'Office', desc: '전 영역 편집 (OFFICE 포함)', levels: { accessSchedule: 2, accessRoster: 2, accessHandover: 2, accessField: 2, accessOffice: 2, accessMes: 2 } },
  { name: '조회 전용', desc: '전 영역 조회만 (OFFICE 없음)', levels: { accessSchedule: 1, accessRoster: 1, accessHandover: 1, accessField: 1, accessOffice: 0, accessMes: 1 } },
];

interface AuditRow { id: number; targetUser: string; action: string; detail: string; byUser: string; createdAt: string; }

type Form = Omit<UserFull, 'id'> & { password: string };
const emptyForm: Form = {
  username: '', password: '', realName: '', department: '', teamName: '', rank: '', jobTitle: '', email: '', phoneNumber: '',
  employeeNumber: '', hireDate: '', tenure: '', isResigned: false, resignDate: '', isAdmin: false,
  accessSchedule: 1, accessRoster: 1, accessHandover: 1, accessField: 1, accessOffice: 0, accessMes: 1,
  mesPermissions: '', hiddenMenus: '[]',
};

export default function Users() {
  const { user: me } = useAuth();
  const isMobile = useIsMobile();
  const [view, setView] = useState<'list' | 'matrix'>('list');
  const [all, setAll] = useState<UserFull[]>([]);
  const [tab, setTab] = useState<'active' | 'resigned'>('active');
  const [search, setSearch] = useState('');
  const [selId, setSelId] = useState<number | null>(null);
  const [adding, setAdding] = useState(false);
  const [copiedFrom, setCopiedFrom] = useState('');
  const [form, setForm] = useState<Form>(emptyForm);
  const [err, setErr] = useState('');
  const [audit, setAudit] = useState<AuditRow[] | null>(null);
  const [teamFilter, setTeamFilter] = useState('');
  const [bulkLevel, setBulkLevel] = useState<AccessLevel>(1);
  const [teamMgr, setTeamMgr] = useState(false);
  const [org, setOrg] = useState<OrgDept[]>([]);
  const [divisions, setDivisions] = useState<string[]>([]);
  const [dept, setDept] = useState(DEPT_ALL);
  const [matrixMode, setMatrixMode] = useState<'level' | 'menu'>('level');
  const [detailTab, setDetailTab] = useState<'perm' | 'info' | 'history'>('perm');
  const [dAudit, setDAudit] = useState<AuditRow[] | null>(null);

  const loadOrg = useCallback(async () => {
    // 백엔드가 아직 옛 버전이면 배열이 그대로 온다 — 본부 없이 부서만 보여 준다.
    const res = await api.get<OrgTree | OrgDept[]>('/api/users/org');
    if (Array.isArray(res)) { setOrg(res); setDivisions([]); }
    else { setOrg(res.depts ?? []); setDivisions(res.divisions ?? []); }
  }, []);
  function openTeamMgr() { setTeamMgr(true); loadOrg(); }

  const load = useCallback(async () => {
    setAll(await api.get<UserFull[]>('/api/users?includeResigned=true'));
  }, []);
  useEffect(() => { load(); }, [load]);
  useEffect(() => { loadOrg().catch(() => {}); }, [loadOrg]);

  const active = all.filter(u => !u.isResigned);
  const resigned = all.filter(u => u.isResigned);
  let list = tab === 'active' ? active : resigned;

  // 부서 필터 — 인원이 많아 한 부서만 보고 싶을 때. 탭을 바꿔 그 부서가 사라지면 '전체' 로 돌아간다.
  // 마스터(관리자) 계정뿐인 부서는 탭에서 뺀다 — 로그인용 버킷일 뿐 실제 조직이 아니다.
  // '전체' 탭에는 그대로 남으므로 계정 관리 자체는 그대로 할 수 있다.
  const byDept = new Map<string, UserFull[]>();
  for (const u of list) {
    const d = deptOf(u.department);
    const arr = byDept.get(d);
    if (arr) arr.push(u); else byDept.set(d, [u]);
  }
  const deptCounts = new Map<string, number>();
  for (const [d, us] of byDept) {
    if (us.every(u => u.isAdmin)) continue;
    deptCounts.set(d, us.length);
  }
  const depts = [...deptCounts.keys()].sort();
  const deptTotal = list.length;
  const curDept = depts.includes(dept) ? dept : DEPT_ALL;
  if (curDept !== DEPT_ALL) list = list.filter(u => deptOf(u.department) === curDept);

  if (search.trim()) {
    const q = search.trim().toLowerCase();
    list = list.filter(u => u.realName.toLowerCase().includes(q) || u.username.toLowerCase().includes(q) || u.teamName.toLowerCase().includes(q));
  }
  const selected = all.find(u => u.id === selId) ?? null;
  const teams = [...new Set(active.map(u => u.teamName).filter(Boolean))].sort();

  // 부서·소속팀 드롭다운은 '부서/팀 관리'(조직도)를 따른다. 사용자들이 적어 둔 값을
  // 그대로 긁어 쓰던 탓에 관리자 계정뿐인 '관리자' 부서까지 선택지로 올라왔고,
  // 조직에 새로 만든 부서는 인원이 붙기 전까지 아예 나오지 않았다.
  // 서버가 등록된 것 먼저, 사용자에게만 남은 값을 뒤에 붙여 내려 준다(UserService.GetOrgAsync).
  const orgDepts = useMemo(
    () => org.map(d => d.name).filter(n => n && !ORG_PLACEHOLDERS.includes(n)),
    [org]);
  // 부서를 고르면 그 부서의 팀만 보여 준다. 부서가 비었거나 조직도에 없는 이름이면 전체를 보여 준다
  // — 목록이 통째로 비어 고를 것이 없어지는 상황을 만들지 않는다.
  const orgTeams = useMemo(() => {
    const names = (d: OrgDept) => d.teams.map(t => t.name).filter(n => n && !ORG_PLACEHOLDERS.includes(n));
    const cur = form.department.trim().toLowerCase();
    const hit = cur ? org.filter(d => d.name.trim().toLowerCase() === cur) : [];
    const list = (hit.length > 0 ? hit : org).flatMap(names);
    // 이미 들어 있는 팀이 그 부서에 없더라도 목록에서 사라지지 않게 함께 싣는다
    const held = form.teamName.trim();
    return [...new Set(held ? [...list, held] : list)];
  }, [org, form.department, form.teamName]);

  function pick(u: UserFull) {
    setAdding(false); setErr(''); setCopiedFrom('');
    setSelId(u.id);
    setForm({ ...u, password: '' });
    setDetailTab('perm');   // 권한 조정이 주 업무 → 권한 탭 우선
  }
  function startAdd() {
    setAdding(true); setSelId(null); setErr(''); setCopiedFrom('');
    setForm(emptyForm);
    setDetailTab('info');   // 신규는 기본 정보부터
  }

  // 같은 부서에 여러 명을 넣을 때 소속과 권한을 매번 다시 고르지 않게 한다.
  // 사람을 가리는 값(이름·아이디·비밀번호·사번·연락처·입사일·직급·직위·퇴사)은 가져오지 않는다 —
  // 틀린 값이 미리 들어가 있는 것이 빈 칸보다 나쁘다.
  // 관리자 여부도 가져오지 않는다. 계정을 베끼다 관리자가 딸려 나오면 안 된다.
  function startCopy(u: UserFull) {
    setAdding(true); setSelId(null); setErr('');
    setForm({
      ...emptyForm,
      department: u.department,
      teamName: u.teamName,
      accessSchedule: u.accessSchedule, accessRoster: u.accessRoster, accessHandover: u.accessHandover,
      accessField: u.accessField, accessOffice: u.accessOffice, accessMes: u.accessMes,
      mesPermissions: u.mesPermissions, hiddenMenus: u.hiddenMenus,
    });
    setCopiedFrom(u.realName);
    setDetailTab('info');   // 이름·아이디부터 채워야 하므로
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
  function toggleMesPerm(code: string) {
    setForm(f => {
      const set = parseMesPerms(f.mesPermissions);
      if (set.has(code)) set.delete(code); else set.add(code);
      // 저장 순서를 목록 순서로 고정한다 — 같은 권한이면 같은 문자열이라 이력이 깨끗하다.
      return { ...f, mesPermissions: MES_PERMS.filter(p => set.has(p.code)).map(p => p.code).join(',') };
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
                      {AREAS.map(a => {
                        // MES 칸에는 세부 권한이 몇 개 켜져 있는지 같이 보여 준다. 등급과 다른 축이라
                        // 여기서 안 보이면 팀 전체를 볼 때 사람을 하나씩 열어 봐야 한다.
                        const perms = a.key === 'accessMes' ? parseMesPerms(u.mesPermissions) : null;
                        return (
                        <td key={a.key}>
                          {u.isAdmin
                            ? <span className="um-lvl lv2 fixed">편집</span>
                            : <button className={`um-lvl lv${u[a.key]}`} title={`${a.desc} — 클릭하여 변경`}
                                onClick={() => cycleCell(u, a)}>{levelName(u[a.key])}</button>}
                          {perms && perms.size > 0 && !u.isAdmin && (
                            <span className="um-mesperm"
                                  title={`MES 세부 권한: ${MES_PERMS.filter(x => perms.has(x.code)).map(x => x.label).join(' · ')}`}>
                              +{perms.size}
                            </span>
                          )}
                        </td>
                      );})}
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
          {/* 모바일: 선택 전 = 목록만, 선택 후 = 상세만 (마스터-디테일 전환) */}
          {(!isMobile || !showForm) && (
          <div className="um-left">
            <div className="um-tabs">
              <button className={tab === 'active' ? 'active' : ''} onClick={() => setTab('active')}>재직 중 <span>{active.length}</span></button>
              <button className={tab === 'resigned' ? 'active' : ''} onClick={() => setTab('resigned')}>퇴사자 <span>{resigned.length}</span></button>
            </div>
            {depts.length > 1 && (
              <div className="um-deptbar">
                <button className={curDept === DEPT_ALL ? 'active' : ''} onClick={() => setDept(DEPT_ALL)}>
                  전체 <span>{deptTotal}</span>
                </button>
                {depts.map(d => (
                  <button key={d} className={curDept === d ? 'active' : ''} onClick={() => setDept(d)}>
                    {d} <span>{deptCounts.get(d)}</span>
                  </button>
                ))}
              </div>
            )}
            <input className="input um-search" placeholder="검색…" value={search} onChange={e => setSearch(e.target.value)} />
            <div className="um-list">
              {list.map(u => (
                <div key={u.id} className={`um-item ${selId === u.id ? 'active' : ''}`} onClick={() => pick(u)}>
                  <div className="um-avatar">{u.realName[0] ?? '?'}</div>
                  <div className="um-info">
                    <div className="um-name">{u.realName}{u.isAdmin && <span className="um-adm-badge">관리자</span>}</div>
                    <div className="um-meta">{[u.department, u.teamName, u.rank, u.jobTitle].filter(Boolean).join(' · ') || '-'}</div>
                  </div>
                  <div className="um-uid">{u.username}</div>
                </div>
              ))}
              {list.length === 0 && <div className="um-no">사용자가 없습니다</div>}
            </div>
            {tab === 'active' && <button className="btn btn-primary um-add" onClick={startAdd}>+ 신규 사용자</button>}
          </div>
          )}

          {(!isMobile || showForm) && (
          <div className="um-right">
            {!showForm && <div className="um-empty"><div style={{ fontSize: 36 }}>👥</div><p>사용자를 선택하세요</p></div>}
            {showForm && (
              <form onSubmit={save}>
                {/* 상단 요약 바 (고정) + 탭 */}
                <div className="um-dtop">
                  <div className="um-dhead">
                    {isMobile && <button type="button" className="um-back" onClick={() => { setAdding(false); setSelId(null); }} aria-label="목록으로">‹</button>}
                    <div className="um-avatar lg" style={adding ? { background: '#4E9D77' } : {}}>{adding ? '+' : (form.realName[0] ?? '?')}</div>
                    <div className="um-dhead-info">
                      <div className="um-dhead-name">
                        {adding ? '신규 사용자' : (form.realName || '이름 없음')}
                        {form.isAdmin && <span className="um-adm-badge">관리자</span>}
                      </div>
                      <div className="um-dhead-meta">
                        {[form.department, form.teamName, form.rank, form.jobTitle].filter(Boolean).join(' · ') || '소속 미지정'}
                        {!adding && <span className="um-dhead-uid"> · {form.username}</span>}
                      </div>
                    </div>
                    <div className="um-dhead-acts">
                      <button type="button" className="btn btn-ghost" onClick={() => { setAdding(false); setSelId(null); }}>취소</button>
                      {!adding && selected && <button type="button" className="btn btn-ghost" onClick={() => startCopy(selected)}>복사 등록</button>}
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
                {adding && copiedFrom && (
                  <div className="um-copied">
                    <b>{copiedFrom}</b> 님의 <b>부서·소속팀·권한</b>을 가져왔습니다.
                    이름·아이디·비밀번호는 새로 적어 주세요 — 직급·직위·사번·연락처와 관리자 여부는 가져오지 않았습니다.
                  </div>
                )}
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
                      const mesPerms = parseMesPerms(form.mesPermissions);
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
                        {a.key === 'accessMes' && (
                          <div className={`um-subs ${effLevel === 0 ? 'off' : ''}`}>
                            <span className="um-subs-l" title="등급과 별개로 켜 주는 권한입니다. 조회·편집 등급만으로는 아래 항목을 할 수 없습니다.">세부 권한</span>
                            {MES_PERMS.map(perm => {
                              const on = form.isAdmin || mesPerms.has(perm.code);
                              return (
                                <button key={perm.code} type="button"
                                  className={`um-subchip ${on ? 'on' : ''}`}
                                  disabled={isMaster || form.isAdmin || effLevel === 0}
                                  title={`${perm.desc}${form.isAdmin ? ' — 관리자는 항상 가집니다' : ''}`}
                                  onClick={() => toggleMesPerm(perm.code)}>
                                  <span className="um-subchk">{on ? '✓' : ''}</span>{perm.label}
                                </button>
                              );
                            })}
                          </div>
                        )}
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
                  <div className="um-ginfo">
                    {/* 신원 */}
                    <F label="이름 *"><input className="input" required value={form.realName} onChange={e => setForm({ ...form, realName: e.target.value })} /></F>
                    <F label="직급">
                      <select className="input" value={form.rank} onChange={e => setForm({ ...form, rank: e.target.value })}>
                        <option value="">(미지정)</option>
                        {/* 옛 값이 목록에 없더라도 사라지지 않게 그 값을 함께 싣는다 */}
                        {!RANKS.includes(form.rank) && form.rank && <option value={form.rank}>{form.rank}</option>}
                        {RANKS.map(r => <option key={r} value={r}>{r}</option>)}
                      </select>
                    </F>
                    <F label="직위"><input className="input" value={form.jobTitle} onChange={e => setForm({ ...form, jobTitle: e.target.value })} placeholder="QA팀장 / 세정팀장 …" /></F>
                    <F label="부서">
                      <Combo value={form.department} options={orgDepts} placeholder="세정팀 / Office …"
                        emptyText="부서/팀 관리에 등록된 부서가 없습니다"
                        onChange={v => setForm({ ...form, department: v })} />
                    </F>
                    <F label="소속팀">
                      <Combo value={form.teamName} options={orgTeams} placeholder="1팀 / 2팀 / Office"
                        emptyText={form.department.trim() ? '이 부서에 등록된 팀이 없습니다' : '부서/팀 관리에 등록된 팀이 없습니다'}
                        onChange={v => setForm({ ...form, teamName: v })} />
                    </F>
                    {/* 계정 */}
                    <F label={`아이디${adding ? ' * (4자+)' : ''}`}><input className="input" required value={form.username} readOnly={isMaster && !adding} onChange={e => setForm({ ...form, username: e.target.value })} /></F>
                    <F label={`비밀번호${adding ? ' *' : ' (변경 시 입력)'}`}><input className="input" type="password" required={adding} value={form.password} onChange={e => setForm({ ...form, password: e.target.value })} /></F>
                    <F label="사번"><input className="input" value={form.employeeNumber} onChange={e => setForm({ ...form, employeeNumber: e.target.value })} /></F>
                    <F label="입사일"><input className="input" type="date" value={form.hireDate} onChange={e => setForm({ ...form, hireDate: e.target.value })} /></F>
                    <F label="근속">
                      <input className="input" readOnly value={selected?.tenure || '-'}
                        title="입사일로 서버가 계산합니다. 입사일을 바꾸면 저장 후 반영됩니다." />
                    </F>
                    {/* 연락처 */}
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
          )}
        </div>
        )}
      </div>

      {teamMgr && (() => {
        const DEPT_NONE = '(부서 미지정)', TEAM_NONE = '(팀 미지정)';
        const reload = () => { loadOrg(); load(); };
        // 교대 조 지정 — 근무표·달력·오늘 현황이 이 값을 보고 주/야를 예측한다
        const setShift = async (name: string, shiftGroup: number, dept: string) => {
          try { await api.post('/api/users/org/shift', { name, shiftGroup, parent: dept === DEPT_NONE ? '' : dept }); reload(); }
          catch (e) { alert(e instanceof Error ? e.message : '교대 조를 바꾸지 못했습니다.'); }
        };
        // 생산팀 여부 — 근무표에 나올지, 통계에서 생산직으로 셀지를 가른다(교대조와 별개 축).
        // 조직에서 지우는 것과 다르다 — 인원도 과거 일정도 그대로 두고 목록에서만 뺀다.
        const setVisible = async (
          kind: 'dept' | 'team', name: string, dept: string,
          patch: { showOnDashboard?: boolean; showOnCalendar?: boolean },
        ) => {
          try {
            await api.post('/api/users/org/visibility', {
              kind, name, parent: kind === 'team' ? (dept === DEPT_NONE ? '' : dept) : null, ...patch,
            });
            reload();
          } catch (e) { alert(e instanceof Error ? e.message : '표시 설정에 실패했습니다.'); }
        };

        const setProduction = async (name: string, isProduction: boolean, dept: string) => {
          try { await api.post('/api/users/org/production', { name, isProduction, parent: dept === DEPT_NONE ? '' : dept }); reload(); }
          catch (e) { alert(e instanceof Error ? e.message : '생산팀 여부를 바꾸지 못했습니다.'); }
        };
        // WPF 를 아직 쓰는 동안, WPF 가 기록하는 옛 팀 이름을 현재 이름으로 바꿔 넣게 한다.
        // 달력에서 쓸 부서 색. 비우면 자동값으로 되돌아간다.
        // 약칭은 쓰지 않는다 — 어디서든 부서 이름을 그대로 보여 준다.
        const setDeptStyle = async (name: string, color: string) => {
          const c = prompt(`'${name}' 부서의 달력 색 (#RRGGBB).\n비우면 자동으로 정합니다.`, color);
          if (c === null) return;
          try { await api.post('/api/users/org/dept-style', { name, color: c, shortName: '' }); reload(); }
          catch (e) { alert(e instanceof Error ? e.message : '달력 색 저장에 실패했습니다.'); }
        };
        const setLegacy = async (name: string, current: string, dept: string) => {
          const v = prompt(
            `'${name}' 팀이 WPF 에서 쓰던 이름을 쉼표로 구분해 입력하세요.\n` +
            `WPF 에서 새로 등록되는 직원과 근무 기록을 이 팀으로 받아옵니다.\n` +
            `WPF 를 더 이상 쓰지 않으면 비워 두세요.`, current);
          if (v === null) return;
          try { await api.post('/api/users/org/legacy-names', { name, legacyNames: v, parent: dept === DEPT_NONE ? '' : dept }); reload(); }
          catch (e) { alert(e instanceof Error ? e.message : '옛 이름을 바꾸지 못했습니다.'); }
        };
        async function renameDept(dept: string) {
          const nv = prompt(`부서명 변경: ${dept} →`, dept === DEPT_NONE ? '' : dept);
          if (nv === null || !nv.trim() || nv.trim() === dept) return;
          await api.post('/api/users/dept-bulk', { oldDept: dept === DEPT_NONE ? '' : dept, newDept: nv.trim() });
          reload();
        }
        // 팀 이름/부서 변경에는 '현재 부서'를 함께 보낸다.
        // Office 처럼 같은 이름 팀이 여러 부서에 있을 수 있어, 안 보내면 다른 부서 팀까지 바뀐다.
        async function renameTeam(team: string, curDept: string) {
          const nv = prompt(`팀명 변경: ${team} →`, team === TEAM_NONE ? '' : team);
          if (!nv?.trim() || nv.trim() === team) return;
          await api.post('/api/users/team-bulk', {
            team: team === TEAM_NONE ? '' : team, newTeam: nv.trim(), newDepartment: null,
            department: curDept === DEPT_NONE ? '' : curDept,
          });
          reload();
        }
        async function moveTeam(team: string, curDept: string) {
          const nv = prompt(`'${team}' 팀을 이동할 부서:`, curDept === DEPT_NONE ? '' : curDept);
          if (nv === null) return;
          await api.post('/api/users/team-bulk', {
            team: team === TEAM_NONE ? '' : team, newTeam: null, newDepartment: nv.trim(),
            department: curDept === DEPT_NONE ? '' : curDept,
          });
          reload();
        }
        async function addDept(division?: string) {
          const nv = prompt(division ? `'${division}' 본부에 추가할 부서명:` : '추가할 부서명:');
          if (!nv?.trim()) return;
          try { await api.post('/api/users/org/add', { kind: 'dept', name: nv.trim(), parent: division ?? null }); reload(); }
          catch (e) { alert(e instanceof Error ? e.message : '추가 실패'); }
        }
        // ── 본부(사업본부) ──
        async function addDivision() {
          const nv = prompt('추가할 본부명 (예: Wafer 사업본부):');
          if (!nv?.trim()) return;
          try { await api.post('/api/users/org/add', { kind: 'division', name: nv.trim(), parent: null }); reload(); }
          catch (e) { alert(e instanceof Error ? e.message : '추가 실패'); }
        }
        async function renameDivision(name: string) {
          const nv = prompt(`본부명 변경: ${name} →`, name);
          if (!nv?.trim() || nv.trim() === name) return;
          try { await api.post('/api/users/org/division-rename', { oldName: name, newName: nv.trim() }); reload(); }
          catch (e) { alert(e instanceof Error ? e.message : '변경 실패'); }
        }
        async function delDivision(name: string) {
          if (!confirm(`'${name}' 본부를 삭제할까요? (소속 부서가 있으면 삭제되지 않습니다)`)) return;
          try { await api.post('/api/users/org/delete', { kind: 'division', name, parent: null }); reload(); }
          catch (e) { alert(e instanceof Error ? e.message : '삭제 실패'); }
        }
        async function setDeptDivision(deptName: string, division: string) {
          try { await api.post('/api/users/org/dept-division', { dept: deptName, division }); reload(); }
          catch (e) { alert(e instanceof Error ? e.message : '본부를 바꾸지 못했습니다.'); }
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
        // 본부 > 부서로 묶는다. 본부가 지정되지 않은 부서는 맨 아래 '본부 미지정' 묶음으로.
        const DIV_NONE = '본부 미지정';
        const groups: { division: string; depts: typeof org }[] = [];
        for (const d of divisions) groups.push({ division: d, depts: org.filter(x => x.division === d) });
        const loose = org.filter(x => !x.division || !divisions.includes(x.division));
        if (loose.length > 0 || divisions.length === 0) groups.push({ division: '', depts: loose });

        const deptCard = (dept: OrgDept) => (
                <div key={dept.name} className="um-dept">
                  <div className="um-dept-head">
                    <div className="um-dept-title">
                      <span className="um-dept-name">
                        {dept.registered && <i className="um-dept-dot" style={{ background: dept.color }} title={`달력 색 ${dept.color}`} />}
                        {dept.name}
                        {!dept.registered && dept.name !== DEPT_NONE && <em className="um-tag-auto">자동</em>}
                      </span>
                      <span className="um-dept-meta">{dept.teams.length}팀 · {dept.teams.reduce((s, t) => s + t.members.length, 0)}명</span>
                    </div>
                    <div className="um-team-acts">
                      {/* 본부는 부서에 달아 둔다 — 인원은 그대로 '부서 + 팀'만 가지므로 소속을 다시 입력할 일이 없다 */}
                      {dept.name !== DEPT_NONE && divisions.length > 0 && (
                        <select className="um-div-sel" value={dept.division} title="소속 본부(사업본부)"
                          onChange={e => setDeptDivision(dept.name, e.target.value)}>
                          <option value="">본부 미지정</option>
                          {divisions.map(d => <option key={d} value={d}>{d}</option>)}
                        </select>
                      )}
                      <button className="btn btn-ghost um-mini" onClick={() => addTeam(dept.name)}>+ 팀</button>
                      {dept.name !== DEPT_NONE && dept.registered && (
                        <>
                          <label className="um-prod-chk" title="대시보드 '오늘의 근무 현황' 에 이 부서 줄을 띄웁니다. 꺼도 인원과 근무표는 그대로입니다.">
                            <input type="checkbox" checked={dept.showOnDashboard}
                              onChange={e => setVisible('dept', dept.name, dept.name, { showOnDashboard: e.target.checked })} />
                            대시보드
                          </label>
                          <label className="um-prod-chk" title="일정 달력의 부서 목록에 띄웁니다. 꺼도 이미 달려 있는 과거 일정은 그대로 보입니다.">
                            <input type="checkbox" checked={dept.showOnCalendar}
                              onChange={e => setVisible('dept', dept.name, dept.name, { showOnCalendar: e.target.checked })} />
                            달력
                          </label>
                        </>
                      )}
                      {dept.name !== DEPT_NONE && (
                        <button className="btn btn-ghost um-mini" title="달력에서 쓸 색"
                          onClick={() => setDeptStyle(dept.name, dept.color)}>달력 색</button>
                      )}
                      {dept.name !== DEPT_NONE && <button className="btn btn-ghost um-mini" onClick={() => renameDept(dept.name)}>이름</button>}
                      {dept.name !== DEPT_NONE && <button className="btn btn-ghost um-mini um-del-mini" onClick={() => delDept(dept.name)}>삭제</button>}
                    </div>
                  </div>
                  {dept.teams.length === 0 && <div className="um-team-empty">팀이 없습니다. "+ 팀"으로 추가하세요.</div>}
                  {dept.teams.map(team => (
                    <div key={team.name} className="um-teamrow">
                      <div className="um-team-top">
                        <b>
                          {team.name}{!team.registered && team.name !== TEAM_NONE && <em className="um-tag-auto">자동</em>}
                          {team.isProduction && <em className="um-tag-prod">생산</em>}
                          {team.shiftGroup > 0 && <em className="um-tag-shift">{team.shiftGroup}조</em>}
                          {team.legacyNames && <em className="um-tag-legacy" title="WPF 에서 쓰던 이름">WPF: {team.legacyNames}</em>}
                          {' '}<span className="um-team-cnt">{team.members.length}명</span>
                        </b>
                        <div className="um-team-acts">
                          {/* 근무 예측은 팀 이름이 아니라 이 값을 본다 — 이름을 바꿔도 일정이 따라온다 */}
                          {/* 교대조가 지정된 팀은 정의상 생산팀이라 해제할 수 없다 */}
                          {team.name !== TEAM_NONE && team.registered && (
                            <label className="um-prod-chk" title="대시보드 '오늘의 근무 현황' 에 이 팀 줄을 띄웁니다. 꺼도 인원과 근무표는 그대로입니다.">
                              <input type="checkbox" checked={team.showOnDashboard}
                                onChange={e => setVisible('team', team.name, dept.name, { showOnDashboard: e.target.checked })} />
                              대시보드
                            </label>
                          )}
                          {team.name !== TEAM_NONE && (
                            <label className="um-prod-chk"
                              title={team.shiftGroup > 0
                                ? '교대조가 지정된 팀은 생산팀에서 뺄 수 없습니다. 먼저 교대 조를 해제하세요.'
                                : '생산팀이면 근무표에 나오고 통계에서 생산직으로 셉니다.'}>
                              <input type="checkbox" checked={team.isProduction} disabled={team.shiftGroup > 0}
                                onChange={e => setProduction(team.name, e.target.checked, dept.name)} />
                              생산팀
                            </label>
                          )}
                          {team.name !== TEAM_NONE && (
                            <select className="um-shift-sel" value={team.shiftGroup}
                              title="교대 조. 1조와 2조는 항상 반대 근무입니다. 근무표·달력이 팀 이름 대신 이 값을 봅니다."
                              onChange={e => setShift(team.name, Number(e.target.value), dept.name)}>
                              <option value={0}>교대 없음</option>
                              <option value={1}>1조</option>
                              <option value={2}>2조</option>
                            </select>
                          )}
                          {team.name !== TEAM_NONE && (
                            <button className="btn btn-ghost um-mini" title="WPF 에서 쓰던 옛 팀 이름 (병행 기간용)"
                              onClick={() => setLegacy(team.name, team.legacyNames, dept.name)}>WPF명</button>
                          )}
                          {team.name !== TEAM_NONE && <button className="btn btn-ghost um-mini" onClick={() => renameTeam(team.name, dept.name)}>이름</button>}
                          {team.name !== TEAM_NONE && <button className="btn btn-ghost um-mini" onClick={() => moveTeam(team.name, dept.name)}>이동</button>}
                          {team.name !== TEAM_NONE && <button className="btn btn-ghost um-mini um-del-mini" onClick={() => delTeam(team.name, dept.name)}>삭제</button>}
                        </div>
                      </div>
                      {team.members.length > 0 && (
                        <div className="um-team-members">
                          {team.members.map(m => (
                            <span key={m.id} className="um-mchip">
                              {m.realName}{[m.rank, m.jobTitle].filter(Boolean).length > 0 && <i> {[m.rank, m.jobTitle].filter(Boolean).join(' ')}</i>}
                            </span>
                          ))}
                        </div>
                      )}
                    </div>
                  ))}
                </div>
        );

        return (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setTeamMgr(false); }}>
          <div className="modal-box um-teammgr">
            <div className="um-tm-head">
              <h3>조직 관리 <small>본부 · 부서 · 팀</small></h3>
              <div className="um-team-acts">
                <button className="btn btn-ghost um-mini" onClick={addDivision}>+ 본부 추가</button>
                <button className="btn btn-primary um-mini" onClick={() => addDept()}>+ 부서 추가</button>
              </div>
            </div>
            <p className="um-hint" style={{ marginBottom: 12 }}>
              본부 &gt; 부서 &gt; 팀 순서로 관리합니다. 인원이 없어도 미리 만들어 둘 수 있고,
              이름 변경·부서 이동은 소속 인원 전체에 적용되며, 소속 인원이 있는 부서/팀은 삭제되지 않습니다.
              같은 팀 이름(예: Office)을 여러 부서에 둘 수 있습니다.
            </p>
            <div className="um-orgtree">
              {groups.map(g => (
                <div key={g.division || DIV_NONE} className="um-divgroup">
                  <div className="um-div-head">
                    <span className="um-div-name">
                      {g.division || DIV_NONE}
                      <em className="um-div-meta">
                        부서 {g.depts.length} · {g.depts.reduce((s, d) => s + d.teams.reduce((n, t) => n + t.members.length, 0), 0)}명
                      </em>
                    </span>
                    {g.division && (
                      <div className="um-team-acts">
                        <button className="btn btn-ghost um-mini" onClick={() => addDept(g.division)}>+ 부서</button>
                        <button className="btn btn-ghost um-mini" onClick={() => renameDivision(g.division)}>이름</button>
                        <button className="btn btn-ghost um-mini um-del-mini" onClick={() => delDivision(g.division)}>삭제</button>
                      </div>
                    )}
                  </div>
                  {g.depts.length === 0 && <div className="um-team-empty">부서가 없습니다. "+ 부서"로 추가하세요.</div>}
                  {g.depts.map(deptCard)}
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

function F({ label, children, span }: { label: string; children: React.ReactNode; span?: boolean }) {
  return <div className={`um-field${span ? ' span2' : ''}`}><label>{label}</label>{children}</div>;
}
