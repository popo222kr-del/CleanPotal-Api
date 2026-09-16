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

  // 로그인 요청 자체의 401(아이디/비밀번호 오류)은 세션 만료가 아니므로
  // 서버가 보낸 실제 메시지를 그대로 보여줘야 한다 (아래 공통 에러 처리로 넘김).
  if (res.status === 401 && path !== '/api/auth/login') handleUnauthorized();
  if (!res.ok) {
    let msg = `요청 실패 (${res.status})`;
    let gotJson = false;
    try {
      const errBody = await res.json();
      // 표준 봉투 { success, data, error }
      if (errBody?.error) { msg = errBody.error; gotJson = true; }
    } catch { /* 본문이 JSON이 아님 — 아래에서 판단 */ }

    // 서버가 JSON 대신 index.html 을 돌려주고 404/405 가 나면, 그 API 자체가 없는 것이다.
    // 대개 화면(wwwroot)만 새로 올리고 백엔드(DLL)는 옛 버전인 경우다.
    // IIS 는 실행 중인 DLL 을 잠그기 때문에, 사이트를 멈추지 않고 복사하면 조용히 실패한다.
    if (!gotJson && (res.status === 404 || res.status === 405)) {
      msg = `서버에 없는 기능입니다 (${res.status}). 백엔드가 최신 버전으로 배포됐는지 확인하세요. `
          + `화면 파일(wwwroot)만 바뀌고 서버 프로그램이 옛 버전이면 이 오류가 납니다.`;
    }
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

/** 401(세션 만료) 공통 처리 — 토큰을 버리고 로그인으로 보낸다. */
function handleUnauthorized(): never {
  clearToken();
  if (location.pathname !== '/login') location.href = '/login';
  throw new ApiError(401, '인증이 필요합니다.');
}

/**
 * 파일 올리기(FormData). JSON 요청과 달리 Content-Type 을 직접 정하면 안 된다 —
 * multipart 경계 문자열은 브라우저가 붙인다.
 *
 * request() 를 쓰지 못해 같은 일을 여기서 다시 한다. 빠뜨리기 쉬운 두 가지를 같이 챙긴다:
 * 주소 앞의 API_BASE(백엔드가 다른 포트에 있는 빌드)와 401 처리(세션이 끊겼는데
 * "올리지 못했습니다" 만 뜨면 왜 안 되는지 알 수 없다).
 */
export async function upload<T>(path: string, form: FormData): Promise<T> {
  const token = getToken();
  const res = await fetch(API_BASE + path, {
    method: 'POST',
    headers: token ? { Authorization: `Bearer ${token}` } : {},
    body: form,
  });
  if (res.status === 401) handleUnauthorized();
  if (!res.ok) {
    // 413 은 서버가 본문을 읽기도 전에 끊는 자리라 JSON 사유가 없다. 숫자만 보여 주면
    // 무엇이 잘못됐는지 알 수 없어, 크기 때문이라는 것을 여기서 말해 준다.
    let msg = res.status === 413
      ? '파일이 너무 큽니다. 크기를 줄이거나 나눠서 올려 주세요.'
      : `올리지 못했습니다 (${res.status}).`;
    try {
      const body = await res.json();
      if (body?.error) msg = body.error;
      else if (body?.data?.error) msg = body.data.error;
    } catch { /* JSON 이 아님 — 위 문구를 그대로 쓴다 */ }
    throw new ApiError(res.status, msg);
  }
  const payload = await res.json();
  if (payload && typeof payload === 'object' && 'success' in payload && 'data' in payload) {
    return payload.data as T;
  }
  return payload as T;
}

/**
 * 인증이 필요한 파일 받기. &lt;a href&gt; 로는 Authorization 헤더를 실을 수 없어서
 * 보통 요청처럼 받아 브라우저에 넘긴다. 서버가 JSON 으로 사유를 주면 그 사유를 던진다.
 */
export async function download(path: string): Promise<Response> {
  const token = getToken();
  const res = await fetch(API_BASE + path, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });
  if (res.status === 401) handleUnauthorized();
  if (!res.ok) {
    let msg = `받지 못했습니다 (${res.status}).`;
    try {
      const body = await res.json();
      if (body?.error) msg = body.error;
      else if (body?.data?.error) msg = body.data.error;
    } catch { /* JSON 이 아니면 기본 문구 */ }
    throw new ApiError(res.status, msg);
  }
  return res;
}

export const api = {
  get: <T>(path: string) => request<T>('GET', path),
  post: <T>(path: string, body?: unknown) => request<T>('POST', path, body),
  put: <T>(path: string, body?: unknown) => request<T>('PUT', path, body),
  patch: <T>(path: string, body?: unknown) => request<T>('PATCH', path, body),
  del: <T>(path: string) => request<T>('DELETE', path),
};
