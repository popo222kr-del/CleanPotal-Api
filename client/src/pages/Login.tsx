import { useState, useMemo } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { CoolOtter } from '../components/Layout';
import EnvBadge from '../components/EnvBadge';
import { useAbout } from '../hooks/useAbout';
import './Login.css';

// ── 배경: 웨이퍼 다이 맵 (정적 SVG) ──
// 큰 웨이퍼 한 장과 다이 격자. 점검된 다이(파랑) 몇 개와 불량(빨강) 두어 개만 칠한다. 그라데이션·움직임 없음.
function WaferMap() {
  const dies = useMemo(() => {
    const cx = 610, cy = 700, r = 430, d = 34;
    let seed = 7;
    const rand = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
    const out: { x: number; y: number; k: 'n' | 'ok' | 'ng' }[] = [];
    for (let y = cy - r; y < cy + r; y += d) for (let x = cx - r; x < cx + r; x += d) {
      if (Math.hypot(x + d / 2 - cx, y + d / 2 - cy) > r - 22) continue;
      const t = rand();
      out.push({ x: x + 3, y: y + 3, k: t < 0.02 ? 'ng' : t < 0.17 ? 'ok' : 'n' });
    }
    return out;
  }, []);
  return (
    <svg className="lg-wafer" viewBox="0 0 800 900" preserveAspectRatio="xMidYMid slice" aria-hidden>
      <circle cx="610" cy="700" r="430" className="w-disc" />
      <circle cx="610" cy="700" r="416" className="w-ring" />
      <rect x="592" y="1124" width="36" height="12" rx="6" className="w-notch" />
      {dies.map((d, i) => <rect key={i} x={d.x} y={d.y} width="28" height="28" rx="2" className={`w-die ${d.k}`} />)}
      <rect x="180" y="560" width="860" height="2" className="w-scan" />
    </svg>
  );
}

// 왼쪽 소개 칸 — 로그인 전이라 실제 숫자는 보여 주지 않고 무엇을 하는 곳인지만 적는다.
const FEATURES: { t: string; d: string; icon: React.ReactNode }[] = [
  { t: 'QR 체크시트', d: '구역별 매일 · 주 1회 점검', icon: <><rect x="4" y="4" width="6" height="6" rx="1" /><rect x="14" y="4" width="6" height="6" rx="1" /><rect x="4" y="14" width="6" height="6" rx="1" /><path d="M14 14h2v2h-2zM18 18h2v2h-2zM14 18h2M18 14h2" /></> },
  { t: '세정 현황', d: '기타 · 주간세정 입출고', icon: <><path d="M4 7h16M4 12h16M4 17h10" /></> },
  { t: '온 · 습도', d: '창고 센서 실시간 기록', icon: <><path d="M10 14.5V5a2 2 0 1 1 4 0v9.5a4 4 0 1 1-4 0z" /><path d="M12 11v6" /></> },
];

// ── 아이디 저장 ──
// 예전에는 비밀번호까지 base64 로 저장했다 — base64 는 암호가 아니라 공용 PC 에서 누구나 되읽을 수 있었다.
// 이제 아이디만 남기고, 예전 형식이 남아 있으면 읽는 즉시 비밀번호를 지운다.
// 비밀번호 기억은 브라우저의 비밀번호 저장 기능(autocomplete=current-password)에 맡긴다.
const SAVE_KEY = 'cp_saved_login';
function loadSaved(): { u: string } | null {
  try {
    const raw = localStorage.getItem(SAVE_KEY);
    if (!raw) return null;
    const o = JSON.parse(atob(raw));
    if (typeof o?.u !== 'string') return null;
    if ('p' in o) localStorage.setItem(SAVE_KEY, btoa(JSON.stringify({ u: o.u })));
    return { u: o.u };
  } catch { return null; }
}

export default function Login() {
  const { login } = useAuth();
  const nav = useNavigate();
  const loc = useLocation();
  // 바깥 주소로 튀지 않게 이 사이트 안의 경로만 받는다.
  const from = (loc.state as { from?: string } | null)?.from;
  const returnTo = from && from.startsWith('/') && !from.startsWith('//') && !from.startsWith('/login') ? from : '/dashboard';
  const saved = loadSaved();
  const [username, setUsername] = useState(saved?.u ?? '');
  const [password, setPassword] = useState('');
  const [remember, setRemember] = useState(saved != null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [exiting, setExiting] = useState(false);   // 로그인 성공 → 수달 등장 연출
  const about = useAbout();   // 서버가 응답하면 '서버 연결됨' 표시

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      await login(username, password);
      if (remember) localStorage.setItem(SAVE_KEY, btoa(JSON.stringify({ u: username })));
      else localStorage.removeItem(SAVE_KEY);
      // 수달이 튀어나와 선글라스를 벗는 전환 연출 후 진입
      setExiting(true);
      // 로그인 후 첫 화면은 대시보드. QR 등으로 특정 화면에 들어오려다 왔으면 그 화면으로 돌려보낸다.
      window.setTimeout(() => nav(returnTo), 1550);
    } catch (err) {
      setError(err instanceof Error ? err.message : '로그인 실패');
      setLoading(false);
    }
  }

  return (
    <div className="lg-page">
      {/* 왼쪽 — 브랜드 + 웨이퍼 배경 */}
      <section className="lg-hero">
        <WaferMap />
        <div className="lg-top">
          <CoolOtter className="lg-logo" />
          <b>CleanPotal</b>
          {about && <span className="lg-status"><i />서버 연결됨</span>}
        </div>
        <h1 className="lg-headline">현장의 모든 일을<br /><em>한 화면에서.</em></h1>
        <p className="lg-lead">체크시트 · 세정 현황 · 근무표 · 온습도까지<br />세정팀 업무를 하나로 잇습니다.</p>
        <div className="lg-tiles">
          {FEATURES.map(f => (
            <div key={f.t} className="lg-tile">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden>{f.icon}</svg>
              <b>{f.t}</b>
              <small>{f.d}</small>
            </div>
          ))}
        </div>
      </section>

      {/* 오른쪽 — 로그인 */}
      <section className="lg-side">
        <form onSubmit={submit} className="lg-form">
          <div className="lg-title"><h2>다시 오신 걸 환영해요</h2><EnvBadge /></div>
          <p className="lg-sub">사번 아이디로 로그인하세요.</p>
          {error && <div className="lg-err">{error}</div>}
          <div className="lg-field">
            <label htmlFor="lg-id">아이디</label>
            <input id="lg-id" className="input" autoComplete="username" autoFocus={!saved}
              value={username} onChange={e => setUsername(e.target.value)} />
          </div>
          <div className="lg-field">
            <label htmlFor="lg-pw">비밀번호</label>
            <input id="lg-pw" className="input" type="password" autoComplete="current-password" autoFocus={!!saved}
              value={password} onChange={e => setPassword(e.target.value)} />
          </div>
          <label className="lg-remember">
            <input type="checkbox" checked={remember} onChange={e => setRemember(e.target.checked)} />
            아이디 저장
          </label>
          <button className="lg-submit" type="submit" disabled={loading}>
            {loading ? '로그인 중...' : <>로그인 <span aria-hidden>→</span></>}
          </button>
          <div className="lg-meta"><span>세정팀 업무 통합 관리</span><span>비밀번호 분실 시 관리자에게 문의</span></div>
        </form>
      </section>

      {/* 로그인 성공 연출 — 수달이 튀어나오며 선글라스를 벗는다 */}
      {exiting && (
        <div className="lg-exit">
          <CoolOtter className="lg-exit-otter" glassesClass="lg-exit-glasses" />
        </div>
      )}
    </div>
  );
}
