import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { api, setToken, clearToken, getToken } from '../api/client';
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

  return <AuthContext.Provider value={{ user, login, applyAuth, logout }}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  return useContext(AuthContext);
}
