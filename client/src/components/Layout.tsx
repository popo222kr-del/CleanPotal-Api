import { useState, useEffect } from 'react';
import { NavLink, Outlet, useNavigate, useLocation } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { api } from '../api/client';
import './Layout.css';

type Item = { to: string; label: string; soon?: boolean };
type Group = { key: string; icon: string; label: string; items: Item[] };
type Section = { title: string; adminOnly?: boolean; single?: Item & { icon: string }; groups?: Group[] };

// WPF(CleanPotal) MainWindow 메뉴 구조 그대로
const MENU: Section[] = [
  {
    title: 'MAIN',
    single: { to: '/portal', icon: '📄', label: '업무 파일 통합 관리' },
    groups: [
      { key: 'statusboard', icon: '📊', label: '세정 업무 현황판', items: [
        { to: '/status/material', label: '자재물류 일정 현황' },
        { to: '/status/production', label: '생산 현황판', soon: true },
        { to: '/status/dongtan', label: '동탄 물류 현황판', soon: true },
      ]},
    ],
  },
  {
    title: 'WORKSPACE',
    groups: [
      { key: 'schedule', icon: '📅', label: '일정관리', items: [
        { to: '/calendar', label: '세정팀 일정 달력' },
        { to: '/memo', label: '개인 메모장', soon: true },
      ]},
      // WPF와 동일: 배차/공지는 하위 메뉴가 아니라 인수인계 화면 내 버튼으로 접근
      { key: 'handover', icon: '📦', label: '현장 인수인계', items: [
        { to: '/handover', label: '인수인계 현황' },
        { to: '/weekly', label: '주간세정 현황' },
        { to: '/meeting', label: '생산미팅' },
        { to: '/prodreq', label: '생산팀 요청사항' },
        { to: '/schedule-board', label: '스케줄 보드' },
      ]},
      { key: 'field', icon: '✅', label: '현장 점검', items: [
        { to: '/inventory', label: '재고관리' },
        { to: '/checklist', label: '체크시트' },
      ]},
      { key: 'office', icon: '💼', label: 'OFFICE 업무', items: [
        // 업체 견적서 안에 '품목 단가표', 인수인계 현황 안에 '업체 정보'로 접근 (WPF 구조)
        { to: '/quotation', label: '업체 견적서' },
        { to: '/weekly-report', label: '주간보고' },
        { to: '/broken', label: 'BROKEN 관리' },
        { to: '/edu-dashboard', label: '교육 현황 대시보드' },
        { to: '/work-assignment', label: '개인별 업무 분장표' },
      ]},
    ],
  },
  {
    title: 'TOOLS',
    groups: [
      { key: 'etc', icon: '🛠️', label: '기타', items: [
        { to: '/report', label: '성적서 자동 변환', soon: true },
        { to: '/dispatch-cert', label: '반출등록 성적서 생성', soon: true },
        { to: '/doc-search', label: '문서 검색', soon: true },
      ]},
    ],
  },
  {
    title: 'ADMIN', adminOnly: true,
    groups: [
      { key: 'admin', icon: '🔧', label: '관리자 영역', items: [
        { to: '/users', label: '사용자 계정 관리' },
      ]},
    ],
  },
];

export default function Layout() {
  const { user, logout } = useAuth();
  const nav = useNavigate();
  const loc = useLocation();

  // 모바일 사이드바(드로어) 열림 상태 — 경로가 바뀌면 자동으로 닫는다
  const [mobileOpen, setMobileOpen] = useState(false);
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

  return (
    <div className="app-layout">
      {/* 모바일 상단바 — iOS 네비게이션 바 (반투명 유리) */}
      <div className="mobile-topbar">
        <span className="mt-logo">CleanPotal</span>
      </div>
      {/* 드로어 백드롭 — 터치하면 닫힘 */}
      {mobileOpen && <div className="sb-backdrop" onClick={() => setMobileOpen(false)} />}
      <nav className={`sidebar ${mobileOpen ? 'open' : ''}`}>
        <div className="sb-header"><span className="sb-logo">CleanPotal</span></div>
        <div className="sb-menu">
          {MENU.filter(s => !s.adminOnly || user?.isAdmin).map(sec => (
            <div key={sec.title}>
              <div className="sb-section">{sec.title}</div>
              {sec.single && (
                <NavLink to={sec.single.to} className={({ isActive }) => `sb-item single ${isActive ? 'active' : ''}`}>
                  <span className="sb-icon">{sec.single.icon}</span> {sec.single.label}
                </NavLink>
              )}
              {(sec.groups ?? []).map(g => {
                const isOpen = open[g.key];
                return (
                  <div key={g.key}>
                    <button className={`sb-group ${isOpen ? 'open' : ''}`} onClick={() => toggle(g.key)}>
                      <span className="sb-icon">{g.icon}</span> {g.label}
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
          ))}
        </div>
        <div className="sb-user">
          <div className="sb-avatar">{user?.realName?.[0] ?? '?'}</div>
          <div className="sb-uinfo">
            <div className="sb-uname">{user?.realName}</div>
            <div className="sb-urole">{user?.teamName} · {user?.jobTitle}</div>
          </div>
          <button className="sb-logout" onClick={() => { logout(); nav('/login'); }}>나가기</button>
        </div>
      </nav>
      <main className="main-content"><Outlet /></main>

      {/* 모바일 하단 탭바 — iOS 스타일 (PC에선 CSS로 숨김) */}
      <nav className="mobile-tabbar">
        <NavLink to="/handover" className={({ isActive }) => `mt-tab ${isActive ? 'active' : ''}`}>
          <span className="mt-ico">{TabIcon.home}</span><span className="mt-lbl">홈</span>
        </NavLink>
        <NavLink to="/calendar" className={({ isActive }) => `mt-tab ${isActive ? 'active' : ''}`}>
          <span className="mt-ico">{TabIcon.calendar}</span><span className="mt-lbl">일정</span>
        </NavLink>
        <NavLink to="/prodreq" className={({ isActive }) => `mt-tab ${isActive ? 'active' : ''}`}>
          <span className="mt-ico">{TabIcon.requests}</span><span className="mt-lbl">요청사항</span>
          {prUnread > 0 && <span className="mt-dot" />}
        </NavLink>
        <NavLink to="/roster" className={({ isActive }) => `mt-tab ${isActive ? 'active' : ''}`}>
          <span className="mt-ico">{TabIcon.roster}</span><span className="mt-lbl">근무표</span>
        </NavLink>
        <button className="mt-tab" onClick={() => setMobileOpen(true)}>
          <span className="mt-ico">{TabIcon.more}</span><span className="mt-lbl">더보기</span>
        </button>
      </nav>
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
