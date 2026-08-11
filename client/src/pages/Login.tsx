import { useState, useRef, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { CoolOtter } from '../components/Layout';
import './Login.css';

// ── 살아 움직이는 데이터 네트워크 배경 (canvas) ──
// 떠다니는 노드 + 근접 시 연결선 + 대각선 스윕 하이라이트로 "칩 내부 데이터 흐름" 표현.
function NeuralField() {
  const ref = useRef<HTMLCanvasElement>(null);
  useEffect(() => {
    const canvas = ref.current;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    let w = 0, h = 0, raf = 0, t = 0;
    type Node = { x: number; y: number; vx: number; vy: number; r: number; glow: boolean; col: string };
    let nodes: Node[] = [];
    // 화려한 다색 팔레트 (시안 · 블루 · 바이올렛 · 핑크 · 민트)
    const PALETTE = ['90, 235, 250', '95, 160, 255', '175, 130, 255', '245, 130, 220', '90, 245, 195'];

    function resize() {
      w = canvas!.clientWidth || window.innerWidth;
      h = canvas!.clientHeight || window.innerHeight;
      canvas!.width = w * dpr; canvas!.height = h * dpr;
      ctx!.setTransform(dpr, 0, 0, dpr, 0, 0);
      const count = Math.max(36, Math.min(96, Math.round((w * h) / 17000)));
      nodes = Array.from({ length: count }, () => ({
        x: Math.random() * w, y: Math.random() * h,
        vx: (Math.random() - 0.5) * 0.16, vy: (Math.random() - 0.5) * 0.16,
        r: Math.random() * 1.6 + 0.7, glow: Math.random() < 0.24,
        col: PALETTE[(Math.random() * PALETTE.length) | 0],
      }));
    }
    resize();
    window.addEventListener('resize', resize);

    const MAXD = 152;
    function frame() {
      t += 0.005;
      ctx!.clearRect(0, 0, w, h);
      ctx!.globalCompositeOperation = 'lighter';   // 겹칠수록 밝아지는 화려한 발광
      for (const n of nodes) {
        n.x += n.vx; n.y += n.vy;
        if (n.x < -20) n.x = w + 20; else if (n.x > w + 20) n.x = -20;
        if (n.y < -20) n.y = h + 20; else if (n.y > h + 20) n.y = -20;
      }
      // 대각선으로 지나가는 밝힘 밴드 (0..1.3 반복)
      const sweep = ((t * 0.12) % 1.3);
      for (let i = 0; i < nodes.length; i++) {
        const a = nodes[i];
        for (let j = i + 1; j < nodes.length; j++) {
          const b = nodes[j];
          const dx = a.x - b.x, dy = a.y - b.y;
          const d = Math.hypot(dx, dy);
          if (d < MAXD) {
            const base = (1 - d / MAXD) * 0.55;
            const mx = ((a.x + b.x) / 2 / w + (a.y + b.y) / 2 / h) / 2;
            const near = Math.max(0, 1 - Math.abs(mx - sweep) / 0.1);
            ctx!.strokeStyle = `rgba(${a.col}, ${base + near * 0.55})`;
            ctx!.lineWidth = 0.7 + near * 1.5;
            ctx!.beginPath(); ctx!.moveTo(a.x, a.y); ctx!.lineTo(b.x, b.y); ctx!.stroke();
            if (near > 0.4) {   // 스윕이 지날 때 흰 코어로 반짝
              ctx!.strokeStyle = `rgba(255, 255, 255, ${(near - 0.4) * 0.7})`;
              ctx!.lineWidth = 0.6;
              ctx!.stroke();
            }
          }
        }
      }
      for (const n of nodes) {
        if (n.glow) {
          const g = ctx!.createRadialGradient(n.x, n.y, 0, n.x, n.y, 12);
          g.addColorStop(0, `rgba(${n.col}, 0.65)`);
          g.addColorStop(1, `rgba(${n.col}, 0)`);
          ctx!.fillStyle = g;
          ctx!.beginPath(); ctx!.arc(n.x, n.y, 12, 0, Math.PI * 2); ctx!.fill();
        }
        ctx!.beginPath(); ctx!.arc(n.x, n.y, n.r, 0, Math.PI * 2);
        ctx!.fillStyle = `rgba(${n.col}, 1)`; ctx!.fill();
      }
      ctx!.globalCompositeOperation = 'source-over';
      raf = requestAnimationFrame(frame);
    }
    frame();
    if (reduce) cancelAnimationFrame(raf);
    return () => { cancelAnimationFrame(raf); window.removeEventListener('resize', resize); };
  }, []);
  return <canvas ref={ref} className="lg-net" aria-hidden />;
}

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
      {/* 배경 레이어 */}
      <div className="lg-orb o1" />
      <div className="lg-orb o2" />
      <div className="lg-orb o3" />
      <div className="lg-orb o4" />
      <div className="lg-orb o5" />
      <div className="lg-grid" />
      <NeuralField />
      <div className="lg-scan" />
      <div className="lg-vignette" />

      {/* 로그인 카드 — HUD 코너 + 회전 광 테두리 */}
      <form onSubmit={submit} className="lg-card">
        <i className="lg-corner tl" /><i className="lg-corner tr" />
        <i className="lg-corner bl" /><i className="lg-corner br" />
        <div className="lg-head">
          <span className="lg-logowrap"><CoolOtter className="lg-logo" /></span>
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

      {/* 로그인 성공 연출 — 수달이 튀어나오며 선글라스를 벗는다 */}
      {exiting && (
        <div className="lg-exit">
          <CoolOtter className="lg-exit-otter" glassesClass="lg-exit-glasses" />
        </div>
      )}
    </div>
  );
}
