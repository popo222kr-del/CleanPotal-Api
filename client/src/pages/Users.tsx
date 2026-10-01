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
const TEAM_NONE = '(팀 미지정)';
const TREE_KEY = 'um_tree_open';   // 접고 편 상태는 다음에 들어와도 그대로

/** 조직도에 있는 순서를 먼저 따르고, 거기 없는 이름은 뒤에 가나다로 붙인다. */
function byOrgOrder(have: string[], order: string[]) {
  const rank = new Map(order.map((n, i) => [n, i]));
  return [...have].sort((a, b) => {
    const ra = rank.get(a) ?? Infinity, rb = rank.get(b) ?? Infinity;
    return ra !== rb ? ra - rb : a.localeCompare(b, 'ko');
  });
}

/** 부서 이름을 화면 표시용으로 정리한다. 비어 있으면 '(부서 미지정)'. */
function deptOf(v: string | undefined): string {
  const d = (v ?? '').trim();
  return d.length > 0 ? d : '(부서 미지정)';
}

type AreaKey = 'accessSchedule' | 'accessRoster' | 'accessHandover' | 'accessField' | 'accessMaterial' | 'accessOffice' | 'accessMes';

// 영역 정의: 서버 키 ↔ 라벨 ↔ 포함 범위.
// 2026-09-30 메뉴 재편 — 권한은 메뉴 묶음과 1:1 이 아니다(보고·일정·근무 묶음은 메뉴마다 권한이 다르다).
// 설명에 '묶음 › 메뉴' 로 적어 어느 메뉴가 이 권한을 따르는지 보이게 한다.
const AREAS: { key: AreaKey; api: string; label: string; desc: string }[] = [
  { key: 'accessSchedule', api: 'schedule', label: '일정', desc: '일정·근무 › 통합 일정 달력 (자재물류 일정 편집 포함)' },
  { key: 'accessRoster', api: 'roster', label: '근무표', desc: '일정·근무 › 근무표 도장(교대) 입력' },
  { key: 'accessHandover', api: 'handover', label: '세정 작업·인수인계', desc: '세정 작업 전체(기타세정·주간세정·배차표·스케줄 보드·요청사항) + 보고 › 생산팀 인수인계 + 일정·근무 › 공지' },
  { key: 'accessField', api: 'field', label: '설비·공정 관리', desc: '체크시트 현장·설비(조회 등급이면 QR 점검 가능, 설비 월간은 설비팀)·약액·KOH·폐액·BAKE·ICP-MS(주간 분석·Daily)·양식' },
  { key: 'accessMaterial', api: 'material', label: '자재·물류', desc: '재고관리·폐기품 관리·온·습도 모니터링' },
  { key: 'accessOffice', api: 'office', label: 'OFFICE 업무', desc: 'OFFICE 업무(업체·견적서·BROKEN) + 보고 › Daily 업무 보고·주간보고 + 일정·근무 › 교육 현황·업무 분장표' },
  { key: 'accessMes', api: 'mes', label: 'MES (생산관리)', desc: 'LOT 현황·공정(OPER)·전산등록·조회' },
];
// 직급(호칭). 서버의 CleanPotal.Core.JobRank.All 과 같은 순서를 쓴다.
// 직위(jobTitle = QA팀장·세정팀장 등 맡은 일)와는 별개 항목이다.
const RANKS = ['사원', '주임', '대리', '과장', '차장', '부장', '상무', '전무', '부사장', '사장'];

const LEVELS: { v: AccessLevel; label: string }[] = [
  { v: 0, label: '없음' }, { v: 1, label: '조회' }, { v: 2, label: '편집' },
];
const levelName = (v: number) => LEVELS.find(l => l.v === v)?.label ?? '?';

// 메뉴 묶음 — 사이드바와 같은 묶음·순서. 권한은 메뉴마다 없음/조회/편집으로 고르고,
// 서버가 쓰는 영역 등급·조회만·숨김은 여기서 계산한다(영역 등급 = 그 영역 메뉴 중 가장 높은 등급).
type MenuItemDef = { to: string; label: string; area: AreaKey };
const MENU_TREE: { group: string; items: MenuItemDef[] }[] = [
  { group: '일정·근무', items: [
    { to: '/notice', label: '공지', area: 'accessHandover' },
    { to: '/calendar', label: '통합 일정 달력', area: 'accessSchedule' },
    { to: '/roster', label: '근무표', area: 'accessRoster' },
    { to: '/edu-dashboard', label: '교육 현황 대시보드', area: 'accessOffice' },
    { to: '/work-assignment', label: '개인별 업무 분장표', area: 'accessOffice' },
  ]},
  { group: '보고', items: [
    { to: '/work/report', label: 'Daily 업무 보고', area: 'accessOffice' },
    { to: '/meeting', label: '생산팀 인수인계', area: 'accessHandover' },
    { to: '/weekly-report', label: '주간보고', area: 'accessOffice' },
  ]},
  { group: '세정 작업', items: [
    { to: '/handover', label: '기타세정 현황', area: 'accessHandover' },
    { to: '/weekly', label: '주간세정 현황', area: 'accessHandover' },
    { to: '/dispatch', label: '배차표', area: 'accessHandover' },
    { to: '/schedule-board', label: '스케줄 보드', area: 'accessHandover' },
    { to: '/prodreq', label: '생산팀 요청사항', area: 'accessHandover' },
  ]},
  { group: '설비·공정 관리', items: [
    { to: '/checklist', label: '체크시트 (현장)', area: 'accessField' },
    { to: '/eq-check', label: '체크시트 (설비)', area: 'accessField' },
    { to: '/work/chemical', label: '약액 교체 기록', area: 'accessField' },
    { to: '/work/waste', label: 'KOH·폐액 현황', area: 'accessField' },
    { to: '/work/bake', label: 'BAKE 진행 현황', area: 'accessField' },
    { to: '/icpms', label: 'ICP-MS (주간 분석)', area: 'accessField' },
    { to: '/work/icpms', label: 'ICP-MS (Daily)', area: 'accessField' },
    { to: '/work/forms', label: '양식 다운로드', area: 'accessField' },
  ]},
  { group: '자재·물류', items: [
    { to: '/inventory', label: '재고관리', area: 'accessMaterial' },
    { to: '/work/scrap', label: '폐기품 관리', area: 'accessMaterial' },
    { to: '/temp-humidity', label: '온·습도 모니터링', area: 'accessMaterial' },
  ]},
  { group: 'OFFICE 업무', items: [
    { to: '/vendors', label: '업체 관리', area: 'accessOffice' },
    { to: '/quotation', label: '업체 견적서', area: 'accessOffice' },
    { to: '/broken', label: 'BROKEN 관리', area: 'accessOffice' },
  ]},
  // MES 는 화면이 많아 큰 묶음만 둔다 — OPER 9개까지 한 줄씩 넣으면 목록이 읽히지 않는다.
  { group: 'MES', items: [
    { to: '/mes', label: 'MES Dash Board', area: 'accessMes' },
    { to: '/mes/scan', label: 'LOT 스캔', area: 'accessMes' },
    { to: '/mes/register', label: 'CREATE (전산등록)', area: 'accessMes' },
    { to: '/mes/history', label: 'LOT 현황 조회', area: 'accessMes' },
    { to: '/mes/setup', label: 'MES 셋업', area: 'accessMes' },
  ]},
];
const ALL_MENU_ITEMS = MENU_TREE.flatMap(g => g.items);

/** 권한 상태 — 영역 등급 + 조회만 메뉴 + 숨긴 메뉴. 화면에서는 메뉴별 등급으로 보여 준다. */
type PermState = { levels: Record<AreaKey, AccessLevel>; ro: Set<string>; hide: Set<string> };
function menuLevelOf(st: PermState, it: MenuItemDef): AccessLevel {
  const a = st.levels[it.area];
  if (a === 0 || st.hide.has(it.to)) return 0;
  if (st.ro.has(it.to) || a === 1) return 1;
  return 2;
}
/** 메뉴 등급을 바꾼 뒤 영역 등급(= 그 영역 메뉴 중 최고)·조회만·숨김을 다시 계산한다. 같은 영역의 다른 메뉴 등급은 그대로 유지된다. */
function setMenuLevels(st: PermState, changes: Map<string, AccessLevel>): PermState {
  const levels = { ...st.levels };
  const ro = new Set(st.ro), hide = new Set(st.hide);
  const areas = new Set(ALL_MENU_ITEMS.filter(i => changes.has(i.to)).map(i => i.area));
  for (const area of areas) {
    const want = ALL_MENU_ITEMS.filter(i => i.area === area).map(i => ({ i, v: changes.get(i.to) ?? menuLevelOf(st, i) }));
    const max = Math.max(0, ...want.map(w => w.v)) as AccessLevel;
    levels[area] = max;
    for (const { i, v } of want) {
      ro.delete(i.to); hide.delete(i.to);
      if (max === 0) continue;
      if (v === 0) hide.add(i.to);
      else if (v === 1 && max === 2) ro.add(i.to);
    }
  }
  return { levels, ro, hide };
}
const pickLevels = (x: Record<AreaKey, AccessLevel>): Record<AreaKey, AccessLevel> =>
  Object.fromEntries(AREAS.map(a => [a.key, x[a.key]])) as Record<AreaKey, AccessLevel>;
const permOf = (x: Record<AreaKey, AccessLevel> & { readOnlyMenus?: string; hiddenMenus: string }): PermState =>
  ({ levels: pickLevels(x), ro: parseHidden(x.readOnlyMenus ?? '[]'), hide: parseHidden(x.hiddenMenus) });
const permFields = (st: PermState) =>
  ({ ...st.levels, readOnlyMenus: JSON.stringify([...st.ro].sort()), hiddenMenus: JSON.stringify([...st.hide].sort()) });
const ADMIN_PERM: PermState = { levels: Object.fromEntries(AREAS.map(a => [a.key, 2])) as Record<AreaKey, AccessLevel>, ro: new Set(), hide: new Set() };
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

// 역할 프리셋 — 영역 등급 + 메뉴별 '조회만'·숨김. 서버에 저장되고, 관리자가 '프리셋 관리'에서 만들고 고친다.
// 적용은 그 순간 값을 사람에게 복사하는 것이라, 프리셋을 나중에 고쳐도 이미 적용한 사람은 그대로다.
interface Preset {
  id: number; name: string; description: string; sortOrder: number;
  accessSchedule: AccessLevel; accessRoster: AccessLevel; accessHandover: AccessLevel; accessField: AccessLevel;
  accessMaterial: AccessLevel; accessOffice: AccessLevel; accessMes: AccessLevel;
  readOnlyMenus: string; hiddenMenus: string;
}
const presetLevels = (p: Preset): Record<AreaKey, AccessLevel> =>
  Object.fromEntries(AREAS.map(a => [a.key, p[a.key]])) as Record<AreaKey, AccessLevel>;

interface AuditRow { id: number; targetUser: string; action: string; detail: string; byUser: string; createdAt: string; }

type Form = Omit<UserFull, 'id'> & { password: string };
const emptyForm: Form = {
  username: '', password: '', realName: '', department: '', teamName: '', rank: '', jobTitle: '', email: '', phoneNumber: '',
  employeeNumber: '', hireDate: '', tenure: '', isResigned: false, resignDate: '', isAdmin: false,
  accessSchedule: 1, accessRoster: 1, accessHandover: 1, accessField: 1, accessMaterial: 1, accessOffice: 0, accessMes: 1,
  mesPermissions: '', hiddenMenus: '[]', readOnlyMenus: '[]',
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
  // 부서는 기본 접힘(필요한 것만 연다), 팀은 부서를 열면 함께 펼친다.
  const [openDepts, setOpenDepts] = useState<Set<string>>(new Set());
  const [closedTeams, setClosedTeams] = useState<Set<string>>(new Set());
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
  const [presets, setPresets] = useState<Preset[]>([]);
  const [presetMgr, setPresetMgr] = useState(false);
  // 권한 매트릭스에서 체크한 사람들 — 프리셋 일괄 적용 대상
  const [picked, setPicked] = useState<Set<number>>(new Set());
  const [bulkPreset, setBulkPreset] = useState(0);
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
  const loadPresets = useCallback(async () => { setPresets(await api.get<Preset[]>('/api/users/presets')); }, []);
  useEffect(() => { loadPresets().catch(() => {}); }, [loadPresets]);
  useEffect(() => { loadOrg().catch(() => {}); }, [loadOrg]);
  useEffect(() => {
    try {
      const raw = localStorage.getItem(TREE_KEY);
      if (!raw) return;
      const v = JSON.parse(raw) as { depts?: string[]; teams?: string[] };
      setOpenDepts(new Set(v.depts ?? []));
      setClosedTeams(new Set(v.teams ?? []));
    } catch { /* 읽지 못하면 기본값(전부 접힘)으로 둔다 */ }
  }, []);

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

  // 왼쪽 목록을 부서 > 팀 > 인원으로 묶는다. 68명이 한 줄로 늘어서 있으면
  // 부서는 알겠는데 그 안의 팀이 눈에 들어오지 않는다.
  const tree = useMemo(() => {
    const groups = new Map<string, Map<string, UserFull[]>>();
    for (const u of list) {
      const d = deptOf(u.department);
      const t = (u.teamName ?? '').trim() || TEAM_NONE;
      let teams = groups.get(d);
      if (!teams) { teams = new Map(); groups.set(d, teams); }
      const arr = teams.get(t);
      if (arr) arr.push(u); else teams.set(t, [u]);
    }
    return byOrgOrder([...groups.keys()], org.map(d => d.name)).map(dept => {
      const teams = groups.get(dept)!;
      const order = org.find(o => o.name === dept)?.teams.map(t => t.name) ?? [];
      const rows = byOrgOrder([...teams.keys()], order).map(team => ({
        team,
        members: [...teams.get(team)!].sort((a, b) => a.realName.localeCompare(b.realName, 'ko')),
      }));
      return { dept, teams: rows, count: rows.reduce((n, r) => n + r.members.length, 0) };
    });
  }, [list, org]);

  // 찾는 중에는 전부 펼친다 — 접혀 있으면 찾은 사람이 안 보인다.
  const searching = search.trim().length > 0;
  // 부서가 하나뿐이면(부서 칩을 골랐을 때) 굳이 한 번 더 누르게 하지 않는다.
  const soleDept = tree.length === 1 ? tree[0].dept : '';
  const deptOpen = (d: string) => searching || d === soleDept || openDepts.has(d);
  const teamOpen = (d: string, t: string) => searching || !closedTeams.has(`${d}|${t}`);

  function toggleDept(d: string) {
    setOpenDepts(prev => {
      const next = new Set(prev);
      if (next.has(d)) next.delete(d); else next.add(d);
      saveTree(next, closedTeams);
      return next;
    });
  }
  function toggleTeam(d: string, t: string) {
    const k = `${d}|${t}`;
    setClosedTeams(prev => {
      const next = new Set(prev);
      if (next.has(k)) next.delete(k); else next.add(k);
      saveTree(openDepts, next);
      return next;
    });
  }
  function saveTree(depts: Set<string>, teams: Set<string>) {
    // 저장이 막혀 있어도(사생활 보호 창 등) 화면은 그대로 돌아가야 한다
    try { localStorage.setItem(TREE_KEY, JSON.stringify({ depts: [...depts], teams: [...teams] })); } catch { /* 무시 */ }
  }

  function pick(u: UserFull) {
    setAdding(false); setErr(''); setCopiedFrom('');
    setSelId(u.id);
    // 고른 사람이 접힌 칸 안에 있으면 어디 있는지 보이지 않는다
    const d = deptOf(u.department);
    const t = (u.teamName ?? '').trim() || TEAM_NONE;
    const nd = new Set(openDepts).add(d);
    const nt = new Set(closedTeams); nt.delete(`${d}|${t}`);
    setOpenDepts(nd); setClosedTeams(nt); saveTree(nd, nt);
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
      accessField: u.accessField, accessMaterial: u.accessMaterial, accessOffice: u.accessOffice, accessMes: u.accessMes,
      mesPermissions: u.mesPermissions, hiddenMenus: u.hiddenMenus, readOnlyMenus: u.readOnlyMenus ?? '[]',
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
  function applyPreset(p: Preset) {
    setForm(f => ({ ...f, ...presetLevels(p), readOnlyMenus: p.readOnlyMenus || '[]', hiddenMenus: p.hiddenMenus || '[]' }));
  }
  // 매트릭스에서 체크한 사람들에게 프리셋 적용 (관리자 계정은 서버가 건너뛴다)
  async function applyPresetBulk() {
    const p = presets.find(x => x.id === bulkPreset);
    const ids = [...picked].filter(id => matrixUsers.some(u => u.id === id && !u.isAdmin));
    if (!p || ids.length === 0) return;
    if (!confirm(`선택한 ${ids.length}명에게 프리셋 '${p.name}' 을(를) 적용할까요?\n영역 등급과 메뉴별 조회·숨김이 프리셋 값으로 바뀝니다.`)) return;
    const r = await api.post<{ applied: number }>('/api/users/presets/apply', { presetId: p.id, userIds: ids });
    alert(`${r.applied}명에게 적용했습니다.`);
    setPicked(new Set());
    load();
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
  // 매트릭스 메뉴 칸: 편집 → 조회 → 없음 → 편집 (즉시 저장). 영역 등급·조회만·숨김 중 바뀐 것만 보낸다.
  async function cycleMenuCell(u: UserFull, it: MenuItemDef) {
    const before = permOf(u);
    const cur = menuLevelOf(before, it);
    const next = (cur === 2 ? 1 : cur === 1 ? 0 : 2) as AccessLevel;
    const after = setMenuLevels(before, new Map([[it.to, next]]));
    const changes: { id: number; key: string; value: number }[] = [];
    for (const a of AREAS) if (before.levels[a.key] !== after.levels[a.key]) changes.push({ id: u.id, key: a.api, value: after.levels[a.key] });
    for (const r of new Set([...before.ro, ...after.ro])) if (before.ro.has(r) !== after.ro.has(r)) changes.push({ id: u.id, key: `ro:${r}`, value: after.ro.has(r) ? 1 : 0 });
    for (const r of new Set([...before.hide, ...after.hide])) if (before.hide.has(r) !== after.hide.has(r)) changes.push({ id: u.id, key: `menu:${r}`, value: after.hide.has(r) ? 0 : 1 });
    if (changes.length) await api.post('/api/users/perms', { changes });
    load();
  }
  async function applyColumn(area: typeof AREAS[number]) {
    const scope = teamFilter ? `'${teamFilter}' 팀 ${matrixUsers.length}명` : `표시된 ${matrixUsers.length}명`;
    if (!confirm(`${scope}의 [${area.label}] 등급을 '${levelName(bulkLevel)}'(으)로 일괄 적용할까요?`)) return;
    await api.post('/api/users/perms', {
      changes: matrixUsers.map(u => ({ id: u.id, key: area.api, value: bulkLevel })),
    });
    load();
  }

  const pickable = matrixUsers.filter(u => !u.isAdmin);
  const allPicked = pickable.length > 0 && pickable.every(u => picked.has(u.id));
  const togglePick = (id: number) => setPicked(prev => { const n = new Set(prev); if (n.has(id)) n.delete(id); else n.add(id); return n; });
  const toggleAll = () => setPicked(allPicked ? new Set() : new Set(pickable.map(u => u.id)));
  const PickHead = <th className="um-pick" title="프리셋 일괄 적용 대상 — 모두 선택/해제"><input type="checkbox" checked={allPicked} onChange={toggleAll} /></th>;
  const pickCell = (u: UserFull) => (
    <td className="um-pick">
      <input type="checkbox" checked={picked.has(u.id)} disabled={u.isAdmin} title={u.isAdmin ? '관리자는 원래 전부 편집 — 프리셋 대상이 아닙니다' : '프리셋 일괄 적용 대상'}
        onChange={() => togglePick(u.id)} />
    </td>
  );
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
        <button className="btn btn-ghost" onClick={() => setPresetMgr(true)}>프리셋 관리</button>
        <button className="btn btn-ghost" onClick={openTeamMgr}>부서/팀 관리</button>
        <button className="btn btn-ghost" onClick={openAudit}>변경 이력</button>
      </header>
      <div className="pg-body">
        {view === 'matrix' ? (
          <div className="um-matrix-wrap">
            <div className="um-matrix-bar">
              <div className="um-mode">
                <button className={matrixMode === 'level' ? 'on' : ''} onClick={() => setMatrixMode('level')}>영역 등급</button>
                <button className={matrixMode === 'menu' ? 'on' : ''} onClick={() => setMatrixMode('menu')}>메뉴별 편집·조회</button>
              </div>
              <div className="um-bulk-preset" title="왼쪽 칸에서 사람을 체크한 뒤 프리셋을 골라 한 번에 적용합니다">
                <span className="um-flt-l">선택 {[...picked].filter(id => matrixUsers.some(u => u.id === id && !u.isAdmin)).length}명</span>
                <select className="input um-preset-sel" value={bulkPreset} onChange={e => setBulkPreset(Number(e.target.value))}>
                  <option value={0}>프리셋 선택</option>
                  {presets.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
                </select>
                <button className="btn btn-primary um-bulk-btn" disabled={!bulkPreset || picked.size === 0} onClick={applyPresetBulk}>적용</button>
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
                <span className="um-hint">칸을 누를 때마다 편집 → 조회 → 없음 · 즉시 적용 · 사이드바와 같은 메뉴 묶음</span>
              )}
            </div>
            <div className="um-matrix-scroll">
              {matrixMode === 'level' ? (
              <table className="um-matrix">
                <thead>
                  <tr>
                    {PickHead}
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
                      {pickCell(u)}
                      <td className="l">
                        <b>{u.realName}</b>{u.jobTitle && <span className="um-jt">{u.jobTitle}</span>}<small> {u.teamName || '-'} · {u.username}</small>
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
                    <th className="um-pick" rowSpan={2}><input type="checkbox" checked={allPicked} onChange={toggleAll} title="모두 선택/해제" /></th>
                    <th className="l" rowSpan={2}>사용자</th>
                    {MENU_TREE.map(g => <th key={g.group} colSpan={g.items.length} className="um-grp">{g.group}</th>)}
                  </tr>
                  <tr>
                    {ALL_MENU_ITEMS.map(it => <th key={it.to} className="um-subcol">{it.label}</th>)}
                  </tr>
                </thead>
                <tbody>
                  {matrixUsers.map(u => (
                    <tr key={u.id} className={u.isAdmin ? 'is-admin' : ''}>
                      {pickCell(u)}
                      <td className="l"><b>{u.realName}</b>{u.jobTitle && <span className="um-jt">{u.jobTitle}</span>}<small> {u.teamName || '-'} · {u.username}</small></td>
                      {ALL_MENU_ITEMS.map(it => {
                        const lv = u.isAdmin ? 2 : menuLevelOf(permOf(u), it);
                        return (
                          <td key={it.to} className="um-mcell">
                            <button type="button" className={`um-mstate lv${lv}`} disabled={u.isAdmin}
                              title="누를 때마다 편집 → 조회 → 없음" onClick={() => cycleMenuCell(u, it)}>{levelName(lv)}</button>
                          </td>
                        );
                      })}
                    </tr>
                  ))}
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
            <div className="um-list um-tree">
              {tree.length === 0 && <div className="um-no">사용자가 없습니다</div>}
              {tree.map(g => {
                const dOpen = deptOpen(g.dept);
                return (
                  <div key={g.dept} className="um-tg">
                    <button type="button" className={`um-tg-h${dOpen ? ' on' : ''}`}
                      onClick={() => toggleDept(g.dept)}>
                      <span className="um-tg-c">{dOpen ? '▾' : '▸'}</span>
                      <span className="um-tg-n">{g.dept}</span>
                      <span className="um-tg-k">{g.count}</span>
                    </button>
                    {dOpen && g.teams.map(t => {
                      const tOpen = teamOpen(g.dept, t.team);
                      return (
                        <div key={t.team} className="um-tt">
                          <button type="button" className={`um-tt-h${tOpen ? ' on' : ''}`}
                            onClick={() => toggleTeam(g.dept, t.team)}>
                            <span className="um-tg-c">{tOpen ? '▾' : '▸'}</span>
                            <span className="um-tt-n">{t.team}</span>
                            <span className="um-tg-k">{t.members.length}</span>
                          </button>
                          {tOpen && t.members.map(u => (
                            <div key={u.id} className={`um-item um-tm ${selId === u.id ? 'active' : ''}`} onClick={() => pick(u)}>
                              <div className="um-avatar">{u.realName[0] ?? '?'}</div>
                              <div className="um-info">
                                <div className="um-name">{u.realName}{u.isAdmin && <span className="um-adm-badge">관리자</span>}</div>
                                {/* 부서·팀은 위에 적혀 있으니 여기서는 직급·직위만 */}
                                <div className="um-meta">{[u.rank, u.jobTitle].filter(Boolean).join(' · ') || '-'}</div>
                              </div>
                              <div className="um-uid">{u.username}</div>
                            </div>
                          ))}
                        </div>
                      );
                    })}
                  </div>
                );
              })}
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
                  <div className="um-section-t">권한 설정 <small className="um-hint-inline">사이드바 메뉴 묶음별로 메뉴마다 없음/조회/편집을 지정합니다</small></div>
                  <div className="um-presets">
                    <span className="um-presets-l">프리셋:</span>
                    {presets.map(p => (
                      <button key={p.id} type="button" className="um-preset" title={p.description} disabled={isMaster}
                        onClick={() => applyPreset(p)}>{p.name}</button>
                    ))}
                    <button type="button" className="um-preset um-preset-mgr" onClick={() => setPresetMgr(true)} title="프리셋 만들기·고치기">프리셋 관리</button>
                  </div>
                  <label className={`um-perm um-perm-admin ${form.isAdmin ? 'on' : ''}`} title="모든 영역 편집 + 사용자 관리 접근">
                    <input type="checkbox" disabled={isMaster || selected?.id === me?.id} checked={form.isAdmin}
                      onChange={e => setForm({ ...form, isAdmin: e.target.checked })} />
                    관리자 (전체 권한)
                  </label>
                  <p className="um-hide-note">메뉴마다 <b>없음 · 조회 · 편집</b>을 고릅니다. 조회면 그 화면에 편집 버튼이 나오지 않고 서버도 저장을 막습니다. 없음이면 메뉴가 숨겨지고 주소로 여는 것도 막힙니다. 묶음 제목 옆 버튼은 그 묶음 전체를 한 번에 바꿉니다.</p>
                  <MenuPermEditor st={form.isAdmin ? ADMIN_PERM : permOf(form)} disabled={isMaster || form.isAdmin}
                    onChange={st => setForm(f => ({ ...f, ...permFields(st) }))}
                    extra={{ MES: (() => {
                      const mesPerms = parseMesPerms(form.mesPermissions);
                      const mesOff = !form.isAdmin && form.accessMes === 0;
                      return (
                        <div className={`um-subs ${mesOff ? 'off' : ''}`}>
                          <span className="um-subs-l" title="등급과 별개로 켜 주는 권한입니다. 조회·편집 등급만으로는 아래 항목을 할 수 없습니다.">세부 권한</span>
                          {MES_PERMS.map(perm => {
                            const on = form.isAdmin || mesPerms.has(perm.code);
                            return (
                              <button key={perm.code} type="button" className={`um-subchip ${on ? 'on' : ''}`}
                                disabled={isMaster || form.isAdmin || mesOff}
                                title={`${perm.desc}${form.isAdmin ? ' — 관리자는 항상 가집니다' : ''}`}
                                onClick={() => toggleMesPerm(perm.code)}>
                                <span className="um-subchk">{on ? '✓' : ''}</span>{perm.label}
                              </button>
                            );
                          })}
                        </div>
                      );
                    })() }} />
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
                      {/* 옛 값이 목록에 없더라도 사라지지 않게 그 값을 함께 싣는다 */}
                      <Combo value={form.rank} clearLabel="(미지정)"
                        options={RANKS.includes(form.rank) || !form.rank ? RANKS : [form.rank, ...RANKS]}
                        onChange={v => setForm({ ...form, rank: v })} />
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
          patch: { showOnDashboard?: boolean; showOnCalendar?: boolean; usesDeptData?: boolean },
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
                          <label className="um-prod-chk" title="업체 관리·업체 견적서·체크시트·주간보고·교육 현황·업무 분장표를 이 부서가 따로 씁니다. 켠 부서만 관리자 화면의 부서 칩과 '등록 부서' 고르기에 나옵니다. 꺼도 이 부서 자료와 이 부서 사람의 화면은 그대로입니다.">
                            <input type="checkbox" checked={dept.usesDeptData}
                              onChange={e => setVisible('dept', dept.name, dept.name, { usesDeptData: e.target.checked })} />
                            부서별 자료
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
                          {/* 대시보드는 부서 = 묶음, 팀 = 한 줄. 팀마다 따로 켜고 끈다(자동 팀도 켜고 끄면 조직도에 등록된다). */}
                          {team.name !== TEAM_NONE && (
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

      {presetMgr && <PresetManager presets={presets} onClose={() => setPresetMgr(false)} onSaved={list => setPresets(list)} />}

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

/**
 * 프리셋 관리 — 관리자가 역할(프리셋)을 만들고 고친다. 왼쪽 목록(순서 = 버튼 순서), 오른쪽 편집.
 * 영역 등급은 없음/조회/편집, 메뉴 칩은 누를 때마다 편집(영역 등급대로) → 조회 → 숨김. '저장' 을 눌러야 서버에 반영된다.
 */
function PresetManager({ presets, onClose, onSaved }: { presets: Preset[]; onClose: () => void; onSaved: (list: Preset[]) => void }) {
  const [draft, setDraft] = useState<Preset[]>(() => presets.map(p => ({ ...p })));
  const [sel, setSel] = useState(0);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState('');
  const cur = draft[sel];
  const patch = (p: Partial<Preset>) => setDraft(d => d.map((x, i) => (i === sel ? { ...x, ...p } : x)));
  const move = (dir: -1 | 1) => {
    const j = sel + dir;
    if (j < 0 || j >= draft.length) return;
    setDraft(d => { const n = [...d]; [n[sel], n[j]] = [n[j], n[sel]]; return n; });
    setSel(j);
  };
  const add = (from?: Preset) => {
    const base: Preset = from
      ? { ...from, id: 0, name: `${from.name} 복사` }
      : { id: 0, name: '새 프리셋', description: '', sortOrder: 0, accessSchedule: 1, accessRoster: 1, accessHandover: 1, accessField: 1,
          accessMaterial: 1, accessOffice: 0, accessMes: 1, readOnlyMenus: '[]', hiddenMenus: '[]' };
    setDraft(d => [...d, base]);
    setSel(draft.length);
  };
  const remove = () => {
    if (!cur || draft.length <= 1) return;
    if (!confirm(`프리셋 '${cur.name}' 을(를) 지울까요? (이미 적용한 사람의 권한은 그대로입니다)`)) return;
    setDraft(d => d.filter((_, i) => i !== sel));
    setSel(Math.max(0, sel - 1));
  };
  async function save() {
    setBusy(true); setErr('');
    try {
      const list = await api.put<Preset[]>('/api/users/presets', { items: draft });
      onSaved(list);
      onClose();
    } catch (e) {
      setErr(e instanceof Error ? e.message : '저장하지 못했습니다.');
    } finally { setBusy(false); }
  }
  return (
    <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="modal-box um-pm">
        <h3>프리셋 관리 <small>역할별로 메뉴마다 없음·조회·편집을 정해 둡니다 (사이드바와 같은 묶음)</small></h3>
        <div className="um-pm-body">
          <div className="um-pm-list">
            {draft.map((p, i) => (
              <button key={i} type="button" className={`um-pm-item ${i === sel ? 'on' : ''}`} onClick={() => setSel(i)}>
                <b>{p.name || '(이름 없음)'}</b>{p.id === 0 && <em>새로</em>}
              </button>
            ))}
            <div className="um-pm-tools">
              <button type="button" className="btn btn-ghost" onClick={() => add()}>+ 추가</button>
              <button type="button" className="btn btn-ghost" onClick={() => cur && add(cur)} disabled={!cur}>복제</button>
              <button type="button" className="btn btn-ghost" onClick={() => move(-1)} disabled={sel === 0} title="위로">▲</button>
              <button type="button" className="btn btn-ghost" onClick={() => move(1)} disabled={sel >= draft.length - 1} title="아래로">▼</button>
              <button type="button" className="btn btn-ghost um-pm-del" onClick={remove} disabled={draft.length <= 1}>삭제</button>
            </div>
          </div>
          {cur && (
            <div className="um-pm-edit">
              <label className="um-pm-f"><span>이름</span>
                <input className="input" value={cur.name} maxLength={40} onChange={e => patch({ name: e.target.value })} /></label>
              <label className="um-pm-f"><span>설명</span>
                <input className="input" value={cur.description} maxLength={300} placeholder="버튼에 마우스를 올리면 보이는 설명"
                  onChange={e => patch({ description: e.target.value })} /></label>
              <MenuPermEditor st={permOf(cur)} onChange={st => patch(permFields(st) as Partial<Preset>)} />
            </div>
          )}
        </div>
        {err && <div className="um-err">{err}</div>}
        <div className="modal-actions">
          <span className="um-hint">프리셋을 고쳐도 이미 적용한 사람의 권한은 바뀌지 않습니다 — 다시 적용하세요.</span>
          <button className="btn btn-ghost" onClick={onClose}>취소</button>
          <button className="btn btn-primary" onClick={save} disabled={busy}>{busy ? '저장 중…' : '저장'}</button>
        </div>
      </div>
    </div>
  );
}

/**
 * 메뉴별 권한 편집 — 사이드바와 같은 묶음으로, 메뉴마다 없음/조회/편집. 묶음 제목 옆 버튼은 묶음 전체를 한 번에.
 * 사용자 권한 설정 탭과 프리셋 관리가 같이 쓴다. extra 는 묶음 아래에 덧붙일 내용(MES 세부 권한 등).
 */
function MenuPermEditor({ st, onChange, disabled, extra }: {
  st: PermState; onChange: (st: PermState) => void; disabled?: boolean; extra?: Record<string, React.ReactNode>;
}) {
  const setMany = (items: MenuItemDef[], v: AccessLevel) => onChange(setMenuLevels(st, new Map(items.map(i => [i.to, v]))));
  return (
    <div className="um-mp">
      {MENU_TREE.map(g => {
        const lvs = g.items.map(i => menuLevelOf(st, i));
        const all = lvs.every(v => v === lvs[0]) ? lvs[0] : null;
        return (
          <div key={g.group} className="um-mp-g">
            <div className="um-mp-h">
              <b>{g.group}</b>
              <div className="um-mp-all" title="이 묶음 메뉴 전체를 한 번에">
                {LEVELS.map(l => (
                  <button key={l.v} type="button" disabled={disabled} className={`um-seg lv${l.v} ${all === l.v ? 'on' : ''}`}
                    onClick={() => setMany(g.items, l.v)}>전체 {l.label}</button>
                ))}
              </div>
            </div>
            {g.items.map((it, k) => (
              <div key={it.to} className="um-mp-row">
                <span className={`um-mp-name lv${lvs[k]}`}>{it.label}</span>
                <div className="um-area-seg">
                  {LEVELS.map(l => (
                    <button key={l.v} type="button" disabled={disabled} className={`um-seg lv${l.v} ${lvs[k] === l.v ? 'on' : ''}`}
                      onClick={() => setMany([it], l.v)}>{l.label}</button>
                  ))}
                </div>
              </div>
            ))}
            {extra?.[g.group]}
          </div>
        );
      })}
    </div>
  );
}
