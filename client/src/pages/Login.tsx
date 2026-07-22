import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { CoolOtter } from '../components/Layout';
import './Login.css';

// 반도체 회로 배선 (맨해튼 라우팅 + 45° 코너). 같은 경로를 trace(고정)와 pulse(광 흐름)로 겹쳐 그린다.
// 중앙 칩 다이(카드 뒤)로 모여드는 구도.
const TRACES = [
  'M -20 120 H 260 L 320 180 H 560 V 290',
  'M 1220 90 H 900 L 840 150 V 290',
  'M 100 820 V 600 L 160 540 H 380 V 460 H 450',
  'M 1220 700 H 1000 L 940 640 V 510 H 750',
  'M -20 500 H 180 V 380 L 240 320 H 450',
  'M 620 -20 V 140 L 680 200 V 290',
  'M 1100 820 V 640 L 1040 580 H 860 V 510',
  'M -20 680 H 240 L 300 620 H 480 V 510',
  'M 380 -20 V 90 L 440 150 V 290 H 500',
  'M 1220 320 H 1040 L 980 380 H 750',
  'M 200 -20 V 60 L 140 120 V 260 H 300 L 360 320 H 450',
  'M 1220 560 H 1120 L 1060 500 V 380',
  'M 520 820 V 700 L 580 640 V 510',
  'M 900 -20 V 100 L 960 160 V 240 H 780 V 290',
  'M -20 260 H 120 L 180 200 H 300',
  'M 700 820 V 720 L 760 660 V 510',
];
const PULSES = TRACES.map((_, i) => ({
  dur: 6 + (i % 5) * 1.4,
  delay: -(i * 1.7),
  cls: i % 3 === 1 ? 'c2' : i % 3 === 2 ? 'c3' : '',
}));
const PADS: [number, number, string][] = [
  [560, 290, ''], [840, 290, 'd1'], [450, 460, 'd2'], [750, 510, 'd3'],
  [450, 320, 'd1'], [680, 290, ''], [860, 510, 'd2'], [480, 510, 'd3'],
  [500, 290, 'd2'], [750, 380, 'd1'], [450, 320, ''], [1060, 380, 'd3'],
  [580, 510, 'd1'], [780, 290, 'd2'], [300, 200, 'd3'], [760, 510, ''],
];

// ── 아이디/비밀번호 저장 (사내 도구 편의 기능 — base64 난독화 저장) ──
const SAVE_KEY = 'cp_saved_login';
function loadSaved(): { u: string; p: string } | null {
  try {
    const raw = localStorage.getItem(SAVE_KEY);
    if (!raw) return null;
    const o = JSON.parse(atob(raw));
    return typeof o?.u === 'string' && typeof o?.p === 'string' ? o : null;
  } catch { return null; }
}

export default function Login() {
  const { login } = useAuth();
  const nav = useNavigate();
  const saved = loadSaved();
  const [username, setUsername] = useState(saved?.u ?? '');
  const [password, setPassword] = useState(saved?.p ?? '');
  const [remember, setRemember] = useState(saved != null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [exiting, setExiting] = useState(false);   // 로그인 성공 → 수달 등장 연출

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      await login(username, password);
      if (remember) localStorage.setItem(SAVE_KEY, btoa(JSON.stringify({ u: username, p: password })));
      else localStorage.removeItem(SAVE_KEY);
      // 수달이 튀어나와 선글라스를 벗는 전환 연출 후 진입
      setExiting(true);
      // 로그인 후 첫 화면은 현장 업무 인수인계
      window.setTimeout(() => nav('/handover'), 1550);
    } catch (err) {
      setError(err instanceof Error ? err.message : '로그인 실패');
      setLoading(false);
    }
  }

  return (
    <div className="lg-page">
      {/* 오로라 글로우 */}
      <div className="lg-orb o1" />
      <div className="lg-orb o2" />
      <div className="lg-orb o3" />
      <div className="lg-grid" />
      <svg className="lg-circuit" viewBox="0 0 1200 800" preserveAspectRatio="xMidYMid slice" aria-hidden>
        {TRACES.map((d, i) => <path key={`t${i}`} className="cir-trace" d={d} />)}
        {TRACES.map((d, i) => (
          <path key={`p${i}`} className={`cir-pulse ${PULSES[i].cls}`} d={d}
            style={{ animationDuration: `${PULSES[i].dur}s`, animationDelay: `${PULSES[i].delay}s` }} />
        ))}
        {PADS.map(([x, y, cls], i) => (
          <g key={`n${i}`}>
            <circle className="cir-pad" cx={x} cy={y} r={5} />
            <circle className={`cir-core ${cls}`} cx={x} cy={y} r={2.2} />
          </g>
        ))}
      </svg>

      {/* 칩 프레임을 카드에 직접 부착 — 화면 비율과 무관하게 항상 정렬 */}
      <div className="lg-chipwrap">
        <div className="lg-ring" aria-hidden />
      <form onSubmit={submit} className="lg-card">
        <div className="lg-head">
          <CoolOtter className="lg-logo" />
          <h1>세정팀 업무 통합 관리</h1>
        </div>
        {error && <div className="lg-err">{error}</div>}
        <div className="lg-field">
          <label>아이디</label>
          <input className="input" autoComplete="username" autoFocus={!saved}
            value={username} onChange={e => setUsername(e.target.value)} />
        </div>
        <div className="lg-field pw">
          <label>비밀번호</label>
          <input className="input" type="password" autoComplete="current-password"
            value={password} onChange={e => setPassword(e.target.value)} />
        </div>
        <label className="lg-remember">
          <input type="checkbox" checked={remember} onChange={e => setRemember(e.target.checked)} />
          아이디 · 비밀번호 저장
        </label>
        <button className="btn btn-primary lg-submit" type="submit" disabled={loading}>
          {loading ? '로그인 중...' : '로그인'}
        </button>
      </form>
      </div>

      {/* 로그인 성공 연출 — 수달이 튀어나오며 선글라스를 벗는다 */}
      {exiting && (
        <div className="lg-exit">
          <CoolOtter className="lg-exit-otter" glassesClass="lg-exit-glasses" />
        </div>
      )}
    </div>
  );
}
