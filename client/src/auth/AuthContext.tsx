import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { api, setToken, clearToken, getToken, setSessionExpiredHandler } from '../api/client';
import SessionExpired from './SessionExpired';
import type { LoginResponse, UserDto } from '../api/types';

interface AuthState {
  user: UserDto | null;
  login: (username: string, password: string) => Promise<void>;
  applyAuth: (res: LoginResponse) => void;
  logout: () => void;
}

const AuthContext = createContext<AuthState>(null!);
const USER_KEY = 'cp_user';

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserDto | null>(() => {
    const raw = localStorage.getItem(USER_KEY);
    return raw && getToken() ? JSON.parse(raw) : null;
  });
  // 로그인 만료 — 화면은 그대로 두고 그 위에 다시 로그인 창을 띄운다(작성 중이던 내용이 남는다).
  const [expired, setExpired] = useState(false);
  useEffect(() => {
    setSessionExpiredHandler(() => {
      if (localStorage.getItem(USER_KEY)) setExpired(true);
      else if (location.pathname !== '/login') location.href = '/login';
    });
    return () => setSessionExpiredHandler(null);
  }, []);

  function applyAuth(res: LoginResponse) {
    setToken(res.token);
    localStorage.setItem(USER_KEY, JSON.stringify(res.user));
    setUser(res.user);
  }

  async function login(username: string, password: string) {
    const res = await api.post<LoginResponse>('/api/auth/login', { username, password });
    applyAuth(res);
  }

  function logout() {
    // MES 쿠키도 지운다 — 공용 PC 에서 포털만 로그아웃하면 다음 사람이 MES 를 앞 사람 이름으로 쓰게 됐다.
    // 결과를 기다리지 않는다(MES 가 꺼져 있어도 포털 로그아웃은 막히면 안 된다).
    void fetch(`${window.location.origin}/mes-runtime/auth/logout`, { method: 'POST', credentials: 'include' }).catch(() => {});
    clearToken();
    localStorage.removeItem(USER_KEY);
    setUser(null);
  }

  // 권한 즉시 반영: 60초 주기 + 창 포커스 시 내 정보를 DB 기준으로 재조회
  // (서버 정책도 DB 기준이라 보안은 즉시 적용되고, 이건 메뉴/버튼 표시를 맞추는 용도)
  useEffect(() => {
    if (!user) return;
    let alive = true;
    const refresh = () => api.get<UserDto>('/api/auth/me')
      .then(me => {
        if (!alive) return;
        localStorage.setItem(USER_KEY, JSON.stringify(me));
        setUser(prev => JSON.stringify(prev) === JSON.stringify(me) ? prev : me);
      })
      .catch(() => {});
    refresh();   // 접속 즉시 1회 (구버전 캐시에 권한 필드가 없을 때 바로 채움)
    const t = setInterval(refresh, 60000);
    const onFocus = () => refresh();
    window.addEventListener('focus', onFocus);
    return () => { alive = false; clearInterval(t); window.removeEventListener('focus', onFocus); };
  }, [user !== null]);   // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <AuthContext.Provider value={{ user, login, applyAuth, logout }}>
      {children}
      {expired && user && (
        <SessionExpired username={user.username} realName={user.realName}
          onLogin={res => { applyAuth(res); setExpired(false); }}
          onLeave={() => { setExpired(false); logout(); }} />
      )}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  return useContext(AuthContext);
}
