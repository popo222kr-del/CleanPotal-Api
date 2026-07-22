import { useState, useEffect } from 'react';
import { NavLink, Outlet, useNavigate, useLocation } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { useAccess } from '../auth/useAccess';
import { api } from '../api/client';
import type { LoginResponse } from '../api/types';
import './Layout.css';

type Item = { to: string; label: string; soon?: boolean };
type Group = { key: string; icon: string; label: string; items: Item[] };
type Section = { title: string; adminOnly?: boolean; single?: Item & { icon: string }; groups?: Group[] };

// SF Symbols 풍 단색 라인 아이콘 (1.7px 스트로크, currentColor 상속)
const ICONS: Record<string, React.ReactElement> = {
  doc: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
      <path d="M13.5 3.5H7A1.8 1.8 0 0 0 5.2 5.3v13.4A1.8 1.8 0 0 0 7 20.5h10a1.8 1.8 0 0 0 1.8-1.8V8.8z" />
      <path d="M13.5 3.5v5.3h5.3" />
    </svg>
  ),
  chart: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round">
      <path d="M5.5 19.5v-6M12 19.5V5.5M18.5 19.5v-9" />
    </svg>
  ),
  calendar: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
      <rect x="4" y="5.5" width="16" height="15" rx="2" />
      <path d="M4 10h16M8.5 3.5v3.5M15.5 3.5v3.5" />
    </svg>
  ),
  box: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
      <path d="M12 3.5 20 8v8l-8 4.5L4 16V8z" />
      <path d="M4 8l8 4.5L20 8M12 12.5v8" />
    </svg>
  ),
  check: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
      <path d="M4.5 6.5h9M4.5 12h9M4.5 17.5h5" />
      <path d="m14.5 15.5 2.3 2.3 4-4.3" />
    </svg>
  ),
  case: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
      <rect x="3.5" y="7.5" width="17" height="12.5" rx="2" />
      <path d="M9 7.5V6a2 2 0 0 1 2-2h2a2 2 0 0 1 2 2v1.5M3.5 12.5h17" />
    </svg>
  ),
  gear: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round">
      <path d="M4 7.5h9M17.5 7.5H20M4 16.5h2.5M11 16.5h9" />
      <circle cx="15" cy="7.5" r="2.2" />
      <circle cx="8.5" cy="16.5" r="2.2" />
    </svg>
  ),
};

// WPF(CleanPotal) MainWindow 메뉴 구조 그대로
const MENU: Section[] = [
  {
    title: 'MAIN',
    single: { to: '/portal', icon: 'doc', label: '업무 파일 통합 관리' },
    groups: [
      { key: 'statusboard', icon: 'chart', label: '세정 업무 현황판', items: [
        { to: '/status/material', label: '자재물류 일정 현황' },
        { to: '/status/production', label: '생산 현황판', soon: true },
        { to: '/status/dongtan', label: '동탄 물류 현황판', soon: true },
      ]},
    ],
  },
  {
    title: 'WORKSPACE',
    groups: [
      { key: 'schedule', icon: 'calendar', label: '일정관리', items: [
        { to: '/calendar', label: '세정팀 일정 달력' },
        { to: '/memo', label: '개인 메모장', soon: true },
      ]},
      // WPF와 동일: 배차/공지는 하위 메뉴가 아니라 인수인계 화면 내 버튼으로 접근
      { key: 'handover', icon: 'box', label: '현장 인수인계', items: [
        { to: '/handover', label: '인수인계 현황' },
        { to: '/weekly', label: '주간세정 현황' },
        { to: '/meeting', label: '생산미팅' },
        { to: '/prodreq', label: '생산팀 요청사항' },
        { to: '/schedule-board', label: '스케줄 보드' },
      ]},
      { key: 'field', icon: 'check', label: '현장 점검', items: [
        { to: '/inventory', label: '재고관리' },
        { to: '/icpms', label: '설비 ICP-MS' },
        { to: '/checklist', label: '체크시트' },
      ]},
      { key: 'office', icon: 'case', label: 'OFFICE 업무', items: [
        // 업체 견적서 안에 '품목 단가표', 인수인계 현황 안에 '업체 정보'로 접근 (WPF 구조)
        { to: '/quotation', label: '업체 견적서' },
        { to: '/weekly-report', label: '주간보고' },
        { to: '/broken', label: 'BROKEN 관리' },
        { to: '/edu-dashboard', label: '교육 현황 대시보드' },
        { to: '/work-assignment', label: '개인별 업무 분장표' },
      ]},
    ],
  },
  // 'TOOLS(기타)' 그룹 제거 — 성적서 자동 변환·반출등록 성적서 생성·문서 검색은
  // 로컬/NAS 파일 접근·엑셀·인쇄에 의존해 브라우저(웹앱)에서 구현 불가.
  // 추후 서버측 기능(업로드→변환/색인→다운로드)으로 붙일 때 다시 노출.
  {
    title: 'ADMIN', adminOnly: true,
    groups: [
      { key: 'admin', icon: 'gear', label: '관리자 영역', items: [
        { to: '/users', label: '사용자 계정 관리' },
      ]},
    ],
  },
];

export default function Layout() {
  const { user, logout } = useAuth();
  const acc = useAccess();
  const nav = useNavigate();
  const loc = useLocation();

  // 모바일 사이드바(드로어) 열림 상태 — 경로가 바뀌면 자동으로 닫는다
  const [mobileOpen, setMobileOpen] = useState(false);
  const [acctOpen, setAcctOpen] = useState(false);   // 계정 설정 모달
  const [collapsed, setCollapsed] = useState(false);  // 데스크톱 사이드바 접기
  useEffect(() => { setMobileOpen(false); }, [loc.pathname]);

  // 현재 경로가 속한 그룹은 자동으로 펼침
  const activeGroup = MENU.flatMap(s => s.groups ?? []).find(g => g.items.some(i => i.to === loc.pathname));
  const [open, setOpen] = useState<Record<string, boolean>>(
    activeGroup ? { [activeGroup.key]: true } : {}
  );
  const toggle = (k: string) => setOpen(o => ({ ...o, [k]: !o[k] }));

  // 생산팀 요청사항 미확인 뱃지 (WPF 빨간 뱃지) — 60초 주기 갱신
  const [prUnread, setPrUnread] = useState(0);
  useEffect(() => {
    let alive = true;
    const tick = () => api.get<{ count: number }>('/api/prodreq/unread-count')
      // 요청사항 페이지를 보고 있는 동안엔 뱃지를 켜지 않음 (읽음 처리와의 타이밍 경합 방지)
      .then(r => { if (alive) setPrUnread(window.location.pathname === '/prodreq' ? 0 : r.count); })
      .catch(() => {});
    tick();
    const t = setInterval(tick, 60000);
    return () => { alive = false; clearInterval(t); };
  }, []);
  useEffect(() => { if (loc.pathname === '/prodreq') setPrUnread(0); }, [loc.pathname]);
  const prBadge = prUnread > 99 ? '99+' : String(prUnread);

  const Burger = (
    <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round">
      <path d="M4 6h16M4 12h16M4 18h16" />
    </svg>
  );

  return (
    <div className={`app-layout ${collapsed ? 'collapsed' : ''}`}>
      {/* 모바일 상단바 — iOS 네비게이션 바 (반투명 유리) */}
      <div className="mobile-topbar">
        <BrandLogo className="brand-logo" />
        <span className="mt-logo">세정 업무 통합 관리</span>
      </div>
      {/* 드로어 백드롭 — 터치하면 닫힘 */}
      {mobileOpen && <div className="sb-backdrop" onClick={() => setMobileOpen(false)} />}
      <nav className={`sidebar ${mobileOpen ? 'open' : ''}`}>
        <div className="sb-header">
          <button className="sb-burger" onClick={() => setCollapsed(c => !c)} title="사이드바 접기">{Burger}</button>
          <BrandLogo className="brand-logo" />
          <span className="sb-logo">세정 업무 통합 관리</span>
        </div>
        <div className="sb-menu">
          {MENU.filter(s => !s.adminOnly || user?.isAdmin).map(sec => {
            // 영역 등급 0(없음)이면 해당 메뉴 그룹 숨김
            const groupAllowed = (key: string) =>
              key === 'schedule' ? acc.schedule >= 1 :
              key === 'handover' ? acc.handover >= 1 :
              key === 'field' ? acc.field >= 1 :
              key === 'office' ? acc.office >= 1 : true;
            return (
            <div key={sec.title}>
              <div className="sb-section">{sec.title}</div>
              {sec.single && acc.office >= 1 && (
                <NavLink to={sec.single.to} title={sec.single.label} className={({ isActive }) => `sb-item single ${isActive ? 'active' : ''}`}>
                  <span className="sb-icon">{ICONS[sec.single.icon]}</span> <span className="sb-label">{sec.single.label}</span>
                </NavLink>
              )}
              {(sec.groups ?? []).filter(g => groupAllowed(g.key)).map(g => {
                const isOpen = open[g.key];
                return (
                  <div key={g.key}>
                    <button className={`sb-group ${isOpen ? 'open' : ''}`} title={g.label}
                      onClick={() => { if (collapsed) { setCollapsed(false); setOpen(o => ({ ...o, [g.key]: true })); } else toggle(g.key); }}>
                      <span className="sb-icon">{ICONS[g.icon]}</span> <span className="sb-label">{g.label}</span>
                      {g.key === 'handover' && prUnread > 0 && !isOpen && <span className="sb-badge">{prBadge}</span>}
                      <span className="sb-chev">›</span>
                    </button>
                    {isOpen && (
                      <div className="sb-sub">
                        {g.items.map(it => it.soon ? (
                          <span key={it.to} className="sb-subitem soon" title="준비 중">{it.label}<span className="soon-tag">준비중</span></span>
                        ) : (
                          <NavLink key={it.to} to={it.to} className={({ isActive }) => `sb-subitem ${isActive ? 'active' : ''}`}>
                            {it.label}
                            {it.to === '/prodreq' && prUnread > 0 && <span className="sb-badge">{prBadge}</span>}
                          </NavLink>
                        ))}
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          );})}
        </div>
        <div className="sb-user">
          <div className="sb-avatar">{user?.realName?.[0] ?? '?'}</div>
          <div className="sb-uinfo">
            <div className="sb-uname">{user?.realName}</div>
            <div className="sb-urole">{user?.teamName} · {user?.jobTitle}</div>
          </div>
          <button className="sb-gear" title="계정 설정 (아이디·비밀번호 변경)" onClick={() => setAcctOpen(true)}>
            <svg viewBox="0 0 24 24" width="17" height="17" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round">
              <circle cx="12" cy="12" r="3" />
              <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z" />
            </svg>
          </button>
          <button className="sb-logout" onClick={() => { logout(); nav('/login'); }}>Logout</button>
        </div>
      </nav>

      {acctOpen && <AccountModal onClose={() => setAcctOpen(false)} />}
      <main className="main-content"><Outlet /></main>

      {/* 모바일 하단 탭바 — iOS 스타일 (PC에선 CSS로 숨김) */}
      <nav className="mobile-tabbar">
        {acc.handover >= 1 && <NavLink to="/handover" className={({ isActive }) => `mt-tab ${isActive ? 'active' : ''}`}>
          <span className="mt-ico">{TabIcon.home}</span><span className="mt-lbl">홈</span>
        </NavLink>}
        {acc.schedule >= 1 && <NavLink to="/calendar" className={({ isActive }) => `mt-tab ${isActive ? 'active' : ''}`}>
          <span className="mt-ico">{TabIcon.calendar}</span><span className="mt-lbl">일정</span>
        </NavLink>}
        {acc.handover >= 1 && <NavLink to="/prodreq" className={({ isActive }) => `mt-tab ${isActive ? 'active' : ''}`}>
          <span className="mt-ico">{TabIcon.requests}</span><span className="mt-lbl">요청사항</span>
          {prUnread > 0 && <span className="mt-dot" />}
        </NavLink>}
        {acc.roster >= 1 && <NavLink to="/roster" className={({ isActive }) => `mt-tab ${isActive ? 'active' : ''}`}>
          <span className="mt-ico">{TabIcon.roster}</span><span className="mt-lbl">근무표</span>
        </NavLink>}
        <button className="mt-tab" onClick={() => setMobileOpen(true)}>
          <span className="mt-ico">{TabIcon.more}</span><span className="mt-lbl">더보기</span>
        </button>
      </nav>
    </div>
  );
}

// 브랜드 로고 (수달 얼굴) — 외부 이미지 대신 인라인 SVG로 자체 포함 (로그인 화면에서도 사용)
export function BrandLogo({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 100 100" role="img" aria-label="CleanPotal 로고">
      <circle cx="50" cy="50" r="47" fill="#83BCEB" stroke="#111" strokeWidth="2.5" />
      <circle cx="33" cy="43" r="3.4" fill="#111" />
      <circle cx="67" cy="43" r="3.4" fill="#111" />
      <ellipse cx="40.5" cy="66" rx="12" ry="10.5" fill="#fff" stroke="#111" strokeWidth="1.4" />
      <ellipse cx="59.5" cy="66" rx="12" ry="10.5" fill="#fff" stroke="#111" strokeWidth="1.4" />
      <circle cx="50" cy="55" r="5.6" fill="#111" />
      <g stroke="#111" strokeWidth="1.2" strokeLinecap="round">
        <path d="M28 62l-13-3M29 68l-14 1M30 74l-12 5" />
        <path d="M72 62l13-3M71 68l14 1M70 74l12 5" />
      </g>
    </svg>
  );
}

// 계정 설정 모달 — 본인 아이디/비밀번호 변경
function AccountModal({ onClose }: { onClose: () => void }) {
  const { user, applyAuth } = useAuth();
  const [curPw, setCurPw] = useState('');
  const [newId, setNewId] = useState(user?.username ?? '');
  const [newPw, setNewPw] = useState('');
  const [newPw2, setNewPw2] = useState('');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState('');

  async function submit() {
    setErr('');
    if (!curPw) { setErr('현재 비밀번호를 입력하세요.'); return; }
    if (newPw && newPw !== newPw2) { setErr('새 비밀번호가 서로 다릅니다.'); return; }
    const idChanged = newId.trim() && newId.trim() !== user?.username;
    if (!idChanged && !newPw) { setErr('변경할 아이디 또는 비밀번호를 입력하세요.'); return; }
    setBusy(true);
    try {
      const res = await api.post<LoginResponse>('/api/auth/change-credentials', {
        currentPassword: curPw,
        newUsername: idChanged ? newId.trim() : null,
        newPassword: newPw || null,
      });
      applyAuth(res);
      alert('계정 정보가 변경되었습니다.');
      onClose();
    } catch (e) {
      setErr(e instanceof Error ? e.message : '변경 실패');
    } finally { setBusy(false); }
  }

  return (
    <div className="acct-bg" onClick={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="acct-box">
        <div className="acct-head"><h3>계정 설정</h3><button className="acct-x" onClick={onClose}>✕</button></div>
        <p className="acct-hint">아이디와 비밀번호를 변경할 수 있습니다. 변경하지 않을 항목은 비워 두세요.</p>
        <label className="acct-lbl">현재 비밀번호 <span className="acct-req">*</span></label>
        <input className="acct-in" type="password" value={curPw} autoFocus onChange={e => setCurPw(e.target.value)} />
        <label className="acct-lbl">아이디</label>
        <input className="acct-in" value={newId} onChange={e => setNewId(e.target.value)} />
        <label className="acct-lbl">새 비밀번호</label>
        <input className="acct-in" type="password" placeholder="변경 시에만 입력" value={newPw} onChange={e => setNewPw(e.target.value)} />
        <label className="acct-lbl">새 비밀번호 확인</label>
        <input className="acct-in" type="password" placeholder="변경 시에만 입력" value={newPw2} onChange={e => setNewPw2(e.target.value)}
          onKeyDown={e => { if (e.key === 'Enter') submit(); }} />
        {err && <div className="acct-err">{err}</div>}
        <div className="acct-actions">
          <button className="acct-btn ghost" onClick={onClose} disabled={busy}>취소</button>
          <button className="acct-btn primary" onClick={submit} disabled={busy}>{busy ? '변경 중…' : '변경'}</button>
        </div>
      </div>
    </div>
  );
}

// iOS 느낌의 모노톤 라인 아이콘 (색상은 CSS currentColor 상속)
const TabIcon = {
  home: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
      <path d="M3 10.6 12 3.2l9 7.4" /><path d="M5.3 9.2V20a1 1 0 0 0 1 1H10v-5.6h4V21h3.7a1 1 0 0 0 1-1V9.2" />
    </svg>
  ),
  calendar: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
      <rect x="3.5" y="5" width="17" height="15.5" rx="2.6" /><path d="M3.5 9.6h17M8 3.2v3.4M16 3.2v3.4" />
    </svg>
  ),
  requests: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
      <rect x="5" y="4.6" width="14" height="16.4" rx="2.6" /><path d="M9 4.6V3.6a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v1" /><path d="M8.6 11.2h6.8M8.6 15.2h4.4" />
    </svg>
  ),
  roster: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
      <rect x="3.4" y="4.6" width="17.2" height="14.8" rx="2.2" /><path d="M3.4 9.4h17.2M9.2 9.4v10M14.8 9.4v10" />
    </svg>
  ),
  more: (
    <svg viewBox="0 0 24 24" fill="currentColor" stroke="none">
      <circle cx="5.5" cy="12" r="1.55" /><circle cx="12" cy="12" r="1.55" /><circle cx="18.5" cy="12" r="1.55" />
    </svg>
  ),
};
