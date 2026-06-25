import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';

const MENU = [
  { to: '/roster', icon: '🗓️', label: '근무표' },
  { to: '/handover', icon: '📦', label: '인수인계' },
  { to: '/portal', icon: '📄', label: '업무 파일' },
];

export default function Layout() {
  const { user, logout } = useAuth();
  const nav = useNavigate();
  const menu = user?.isAdmin ? [...MENU, { to: '/users', icon: '👥', label: '사용자 관리' }] : MENU;

  return (
    <div style={{ display: 'flex', minHeight: '100vh' }}>
      <nav style={{
        width: 220, background: 'linear-gradient(180deg, #0F172A, #1E293B)', color: '#fff',
        display: 'flex', flexDirection: 'column', position: 'fixed', top: 0, bottom: 0, left: 0,
      }}>
        <div style={{ padding: '18px 16px', borderBottom: '1px solid rgba(255,255,255,0.08)' }}>
          <span style={{ fontSize: 16, fontWeight: 800 }}>CleanPotal</span>
        </div>
        <div style={{ flex: 1, padding: '8px 0' }}>
          {menu.map(m => (
            <NavLink key={m.to} to={m.to} style={({ isActive }) => ({
              display: 'flex', alignItems: 'center', gap: 10, padding: '11px 18px',
              fontSize: 13.5, fontWeight: isActive ? 700 : 500,
              color: isActive ? '#fff' : 'rgba(255,255,255,0.65)',
              background: isActive ? 'rgba(37,99,235,0.18)' : 'transparent',
              borderLeft: isActive ? '3px solid #2563EB' : '3px solid transparent',
            })}>
              <span>{m.icon}</span> {m.label}
            </NavLink>
          ))}
        </div>
        <div style={{ padding: 14, borderTop: '1px solid rgba(255,255,255,0.08)',
          display: 'flex', alignItems: 'center', gap: 10 }}>
          <div style={{ width: 34, height: 34, borderRadius: '50%', background: '#2563EB',
            display: 'flex', alignItems: 'center', justifyContent: 'center', fontWeight: 700 }}>
            {user?.realName?.[0] ?? '?'}
          </div>
          <div style={{ flex: 1, minWidth: 0 }}>
            <div style={{ fontSize: 13, fontWeight: 700 }}>{user?.realName}</div>
            <div style={{ fontSize: 10, color: 'rgba(255,255,255,0.45)' }}>{user?.teamName} · {user?.jobTitle}</div>
          </div>
          <button onClick={() => { logout(); nav('/login'); }} style={{
            background: 'rgba(255,255,255,0.08)', border: 'none', color: 'rgba(255,255,255,0.6)',
            borderRadius: 6, padding: '5px 8px', fontSize: 11 }}>나가기</button>
        </div>
      </nav>
      <main style={{ marginLeft: 220, flex: 1, minWidth: 0 }}>
        <Outlet />
      </main>
    </div>
  );
}
