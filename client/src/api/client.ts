// API 클라이언트 — JWT 토큰을 자동으로 헤더에 실어 보낸다.
// 프론트/백엔드가 같은 IIS 사이트(같은 포트)면 비워두면 되고, 백엔드가
// 다른 포트/서버에 있으면 빌드 시 VITE_API_BASE=http://host:port 로 지정한다.
const API_BASE = (import.meta.env.VITE_API_BASE ?? '').replace(/\/$/, '');
const TOKEN_KEY = 'cp_token';

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY);
}
export function setToken(t: string) {
  localStorage.setItem(TOKEN_KEY, t);
}
export function clearToken() {
  localStorage.removeItem(TOKEN_KEY);
}

export class ApiError extends Error {
  status: number;
  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = { 'Content-Type': 'application/json' };
  const token = getToken();
  if (token) headers['Authorization'] = `Bearer ${token}`;

  const res = await fetch(API_BASE + path, {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });

  if (res.status === 401) {
    clearToken();
    if (location.pathname !== '/login') location.href = '/login';
    throw new ApiError(401, '인증이 필요합니다.');
  }
  if (!res.ok) {
    let msg = `요청 실패 (${res.status})`;
    try {
      const errBody = await res.json();
      // 표준 봉투 { success, data, error }
      if (errBody?.error) msg = errBody.error;
    } catch { /* ignore */ }
    throw new ApiError(res.status, msg);
  }
  if (res.status === 204) return undefined as T;

  const payload = await res.json();
  // 표준 봉투면 data 를 꺼내고, 아니면 본문 그대로
  if (payload && typeof payload === 'object' && 'success' in payload && 'data' in payload) {
    return payload.data as T;
  }
  return payload as T;
}

export const api = {
  get: <T>(path: string) => request<T>('GET', path),
  post: <T>(path: string, body?: unknown) => request<T>('POST', path, body),
  put: <T>(path: string, body?: unknown) => request<T>('PUT', path, body),
  patch: <T>(path: string, body?: unknown) => request<T>('PATCH', path, body),
  del: <T>(path: string) => request<T>('DELETE', path),
};
