import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import './Login.css';

// 반도체 회로 배선 (맨해튼 라우팅 + 45° 코너). 같은 경로를 trace(고정)와 pulse(광 흐름)로 겹쳐 그린다.
const TRACES = [
  'M -20 120 H 260 L 320 180 H 560 V 320 H 760',
  'M 1220 90 H 900 L 840 150 V 300 H 640',
  'M 100 820 V 600 L 160 540 H 380 V 420 H 520',
  'M 1220 700 H 1000 L 940 640 V 480 H 780 L 720 420 V 260',
  'M -20 500 H 180 V 380 L 240 320 H 420',
  'M 620 -20 V 140 L 680 200 H 900 V 380',
  'M 1100 820 V 640 L 1040 580 H 860 V 460',
  'M -20 680 H 240 L 300 620 H 480 V 700 H 700',
];
// 각 펄스의 주기/지연 (서로 어긋나게 — 동시에 흐르지 않도록)
const PULSES = [
  { dur: 7.5, delay: 0 }, { dur: 9, delay: -3 }, { dur: 8, delay: -5.5 }, { dur: 10.5, delay: -1.5 },
  { dur: 8.5, delay: -6.5 }, { dur: 7, delay: -2.5 }, { dur: 9.5, delay: -4 }, { dur: 11, delay: -7 },
];
// 접점 패드 (배선 끝/분기점)
const PADS: [number, number, string][] = [
  [760, 320, ''], [640, 300, 'd1'], [520, 420, 'd2'], [720, 260, 'd3'],
  [420, 320, 'd1'], [900, 380, ''], [860, 460, 'd2'], [700, 700, 'd3'],
  [320, 180, 'd2'], [940, 640, 'd1'],
];

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
    <div className="lg-page">
      <div className="lg-grid" />
      <svg className="lg-circuit" viewBox="0 0 1200 800" preserveAspectRatio="xMidYMid slice" aria-hidden>
        {TRACES.map((d, i) => <path key={`t${i}`} className="cir-trace" d={d} />)}
        {TRACES.map((d, i) => (
          <path key={`p${i}`} className="cir-pulse" d={d}
            style={{ animationDuration: `${PULSES[i].dur}s`, animationDelay: `${PULSES[i].delay}s` }} />
        ))}
        {PADS.map(([x, y, cls], i) => (
          <g key={`n${i}`}>
            <circle className="cir-pad" cx={x} cy={y} r={5} />
            <circle className={`cir-core ${cls}`} cx={x} cy={y} r={2.2} />
          </g>
        ))}
      </svg>

      <form onSubmit={submit} className="lg-card">
        <div className="lg-head">
          <h1>CleanPotal</h1>
          <p>세정팀 업무 통합 관리</p>
        </div>
        {error && <div className="lg-err">{error}</div>}
        <div className="lg-field">
          <label>아이디</label>
          <input className="input" value={username} onChange={e => setUsername(e.target.value)} />
        </div>
        <div className="lg-field pw">
          <label>비밀번호</label>
          <input className="input" type="password" value={password} onChange={e => setPassword(e.target.value)} />
        </div>
        <button className="btn btn-primary lg-submit" type="submit" disabled={loading}>
          {loading ? '로그인 중...' : '로그인'}
        </button>
      </form>
    </div>
  );
}
