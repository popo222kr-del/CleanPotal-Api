import { useState } from 'react';
import { api } from '../api/client';
import type { LoginResponse } from '../api/types';

/**
 * 로그인 만료 창 — 화면 위에 떠서 그 자리에서 다시 로그인한다.
 * 뒤의 화면(작성 중이던 창·입력·자동저장)은 그대로 있고, 로그인하면 자동저장이 다음 시도에 이어서 저장한다.
 * 아이디는 지금 사람으로 고정 — 다른 사람이 이어받으면 앞 사람 화면에 다른 이름으로 저장된다.
 */
export default function SessionExpired({ username, realName, onLogin, onLeave }: {
  username: string; realName: string;
  onLogin: (res: LoginResponse) => void; onLeave: () => void;
}) {
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (busy || !password) return;
    setBusy(true); setError('');
    try {
      onLogin(await api.post<LoginResponse>('/api/auth/login', { username, password }));
    } catch (err) {
      setError(err instanceof Error ? err.message : '로그인하지 못했습니다.');
      setBusy(false);
    }
  }

  return (
    <div className="sx-bg" role="dialog" aria-modal="true" aria-labelledby="sx-title">
      <form className="sx-box" onSubmit={submit}>
        <h3 id="sx-title">로그인 시간이 지났습니다</h3>
        <p>비밀번호를 다시 넣으면 <b>작성 중이던 내용 그대로</b> 이어서 할 수 있습니다.</p>
        <label className="sx-field">
          <span>아이디</span>
          <input className="input" value={`${username}${realName ? ` (${realName})` : ''}`} readOnly tabIndex={-1} />
        </label>
        <label className="sx-field">
          <span>비밀번호</span>
          <input className="input" type="password" autoComplete="current-password" autoFocus
            value={password} onChange={e => setPassword(e.target.value)} />
        </label>
        {error && <div className="sx-err">{error}</div>}
        <div className="sx-acts">
          <button type="button" className="btn btn-ghost" onClick={onLeave}>다른 계정으로(로그인 화면)</button>
          <button type="submit" className="btn btn-primary" disabled={busy || !password}>{busy ? '확인 중…' : '다시 로그인'}</button>
        </div>
      </form>
    </div>
  );
}
