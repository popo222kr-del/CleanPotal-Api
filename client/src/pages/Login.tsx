import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';

export default function Login() {
  const { login } = useAuth();
  const nav = useNavigate();
  const [username, setUsername] = useState('1004');
  const [password, setPassword] = useState('1234');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      await login(username, password);
      // 모바일은 앱 홈(인수인계 현황)으로, PC는 기존 근무표로 진입
      nav(window.matchMedia('(max-width: 768px)').matches ? '/handover' : '/roster');
    } catch (err) {
      setError(err instanceof Error ? err.message : '로그인 실패');
    } finally {
      setLoading(false);
    }
  }

  return (
    <div style={{
      minHeight: '100vh', display: 'flex', alignItems: 'center', justifyContent: 'center',
      background: 'linear-gradient(135deg, #0F172A, #4478AE)',
    }}>
      <form onSubmit={submit} style={{
        background: '#fff', borderRadius: 16, padding: '40px 32px', width: 360,
        boxShadow: '0 12px 40px rgba(0,0,0,0.2)',
      }}>
        <div style={{ textAlign: 'center', marginBottom: 24 }}>
          <h1 style={{ fontSize: 22, fontWeight: 800 }}>CleanPotal</h1>
          <p style={{ fontSize: 12, color: 'var(--text-mid)', marginTop: 4 }}>세정팀 업무 통합 관리</p>
        </div>
        {error && (
          <div style={{ background: '#FBF2F1', color: '#C0453E', padding: '8px 12px',
            borderRadius: 8, fontSize: 12, marginBottom: 14 }}>{error}</div>
        )}
        <div style={{ marginBottom: 14 }}>
          <label style={{ fontSize: 12, fontWeight: 700, color: 'var(--text-mid)', display: 'block', marginBottom: 5 }}>아이디</label>
          <input className="input" value={username} onChange={e => setUsername(e.target.value)} />
        </div>
        <div style={{ marginBottom: 18 }}>
          <label style={{ fontSize: 12, fontWeight: 700, color: 'var(--text-mid)', display: 'block', marginBottom: 5 }}>비밀번호</label>
          <input className="input" type="password" value={password} onChange={e => setPassword(e.target.value)} />
        </div>
        <button className="btn btn-primary" type="submit" disabled={loading}
          style={{ width: '100%', height: 44, fontSize: 15 }}>
          {loading ? '로그인 중...' : '로그인'}
        </button>
      </form>
    </div>
  );
}
