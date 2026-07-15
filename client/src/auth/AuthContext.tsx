import { createContext, useContext, useState, type ReactNode } from 'react';
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

  return <AuthContext.Provider value={{ user, login, applyAuth, logout }}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  return useContext(AuthContext);
}
