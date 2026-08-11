import { useState, useRef, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { CoolOtter } from '../components/Layout';
import './Login.css';

// ── 칩 다이(die) 배경 (canvas) ──
// 로그인 카드를 중앙 다이로 두고, 사방에 본딩 패드 행 + 다이 프레임 + 코너 정합 마크,
// 패드에서 화면 밖으로 뻗는 라우팅 배선을 따라 전자 신호가 흐르는 구성.
type Pt = { x: number; y: number };
type Trace = { pts: Pt[]; cum: number[]; len: number };
type Pad = { x: number; y: number; nx: number; ny: number };

function ChipDie() {
  const ref = useRef<HTMLCanvasElement>(null);
  useEffect(() => {
    const canvas = ref.current;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    let w = 0, h = 0, raf = 0, t = 0;
    let die = { x: 0, y: 0, w: 0, h: 0, r: 26 };
    let pads: Pad[] = [];
    let traces: Trace[] = [];
    type Electron = { ti: number; len: number; dir: number; spd: number; hue: number };
    let electrons: Electron[] = [];

    const clamp = (v: number, a: number, b: number) => Math.max(a, Math.min(b, v));
    function polyOf(pts: Pt[]): Trace {
      const cum = [0];
      let total = 0;
      for (let i = 1; i < pts.length; i++) {
        total += Math.hypot(pts[i].x - pts[i - 1].x, pts[i].y - pts[i - 1].y);
        cum.push(total);
      }
      return { pts, cum, len: total };
    }
    function posAt(tr: Trace, len: number): Pt {
      const L = clamp(len, 0, tr.len);
      let i = 1;
      while (i < tr.cum.length && tr.cum[i] < L) i++;
      const a = tr.pts[i - 1], b = tr.pts[i] ?? a;
      const seg = tr.cum[i] - tr.cum[i - 1] || 1;
      const f = (L - tr.cum[i - 1]) / seg;
      return { x: a.x + (b.x - a.x) * f, y: a.y + (b.y - a.y) * f };
    }

    function build() {
      w = canvas!.clientWidth || window.innerWidth;
      h = canvas!.clientHeight || window.innerHeight;
      canvas!.width = w * dpr; canvas!.height = h * dpr;
      ctx!.setTransform(dpr, 0, 0, dpr, 0, 0);

      // 중앙 다이(카드보다 크게) — 카드를 감싸는 프레임
      const dw = clamp(w * 0.42, 460, 640);
      const dh = clamp(h * 0.72, 470, 660);
      die = { x: (w - dw) / 2, y: (h - dh) / 2, w: dw, h: dh, r: 28 };

      // 본딩 패드 — 다이 4변에 균등 배치, 각 패드에서 배선 라우팅
      pads = [];
      traces = [];
      const inset = 42, step = 30;
      const addSide = (from: Pt, to: Pt, nx: number, ny: number) => {
        const dist = Math.hypot(to.x - from.x, to.y - from.y);
        const n = Math.max(2, Math.floor(dist / step));
        for (let i = 0; i <= n; i++) {
          const f = i / n;
          const x = from.x + (to.x - from.x) * f;
          const y = from.y + (to.y - from.y) * f;
          pads.push({ x, y, nx, ny });
        }
      };
      const L = die.x, R = die.x + die.w, T = die.y, B = die.y + die.h;
      addSide({ x: L + inset, y: T }, { x: R - inset, y: T }, 0, -1);   // top
      addSide({ x: L + inset, y: B }, { x: R - inset, y: B }, 0, 1);    // bottom
      addSide({ x: L, y: T + inset }, { x: L, y: B - inset }, -1, 0);   // left
      addSide({ x: R, y: T + inset }, { x: R, y: B - inset }, 1, 0);    // right

      // 패드마다 절반 정도만 바깥으로 라우팅(맨해튼 + 45° 코너)
      for (let i = 0; i < pads.length; i++) {
        if (i % 2 === 1) continue;
        const p = pads[i];
        const nx = p.nx, ny = p.ny;
        const px = -ny, py = nx;             // 수직 방향
        const L1 = 34 + ((i * 37) % 80);
        const D = 24 + ((i * 53) % 50);
        const sign = i % 2 === 0 ? 1 : -1;
        const p0 = { x: p.x, y: p.y };
        const p1 = { x: p.x + nx * L1, y: p.y + ny * L1 };
        const p2 = { x: p1.x + (nx + px * sign) * D, y: p1.y + (ny + py * sign) * D };
        let p3 = { x: p2.x, y: p2.y };
        if (nx > 0) p3 = { x: w + 12, y: p2.y };
        else if (nx < 0) p3 = { x: -12, y: p2.y };
        else if (ny > 0) p3 = { x: p2.x, y: h + 12 };
        else p3 = { x: p2.x, y: -12 };
        traces.push(polyOf([p0, p1, p2, p3]));
      }

      // 전자 신호 풀
      const count = Math.min(30, Math.max(14, Math.round(traces.length * 0.55)));
      electrons = Array.from({ length: count }, () => spawn());
    }
    function spawn(): Electron {
      const ti = Math.floor(Math.random() * Math.max(1, traces.length));
      const dir = Math.random() < 0.5 ? 1 : -1;
      const tr = traces[ti];
      return { ti, dir, len: dir > 0 ? 0 : (tr ? tr.len : 0), spd: 1.5 + Math.random() * 2, hue: Math.random() };
    }

    build();
    window.addEventListener('resize', build);

    function roundRectPath(x: number, y: number, ww: number, hh: number, r: number) {
      ctx!.beginPath();
      ctx!.moveTo(x + r, y);
      ctx!.arcTo(x + ww, y, x + ww, y + hh, r);
      ctx!.arcTo(x + ww, y + hh, x, y + hh, r);
      ctx!.arcTo(x, y + hh, x, y, r);
      ctx!.arcTo(x, y, x + ww, y, r);
      ctx!.closePath();
    }

    function frame() {
      t += 1;
      ctx!.clearRect(0, 0, w, h);

      // 다이 내부 미세 로직 그리드 (카드 주변으로 은은히 비침)
      ctx!.save();
      roundRectPath(die.x, die.y, die.w, die.h, die.r);
      ctx!.clip();
      ctx!.strokeStyle = 'rgba(96, 140, 230, 0.06)';
      ctx!.lineWidth = 1;
      for (let gx = die.x; gx <= die.x + die.w; gx += 22) { ctx!.beginPath(); ctx!.moveTo(gx, die.y); ctx!.lineTo(gx, die.y + die.h); ctx!.stroke(); }
      for (let gy = die.y; gy <= die.y + die.h; gy += 22) { ctx!.beginPath(); ctx!.moveTo(die.x, gy); ctx!.lineTo(die.x + die.w, gy); ctx!.stroke(); }
      ctx!.restore();

      // 라우팅 배선 (기본 어둡게)
      ctx!.strokeStyle = 'rgba(88, 122, 205, 0.17)';
      ctx!.lineWidth = 1.2;
      ctx!.lineJoin = 'round';
      for (const tr of traces) {
        ctx!.beginPath();
        ctx!.moveTo(tr.pts[0].x, tr.pts[0].y);
        for (let i = 1; i < tr.pts.length; i++) ctx!.lineTo(tr.pts[i].x, tr.pts[i].y);
        ctx!.stroke();
      }

      // 다이 프레임
      roundRectPath(die.x, die.y, die.w, die.h, die.r);
      ctx!.strokeStyle = 'rgba(120, 160, 255, 0.32)';
      ctx!.lineWidth = 1.6;
      ctx!.shadowColor = 'rgba(64, 120, 255, 0.5)';
      ctx!.shadowBlur = 18;
      ctx!.stroke();
      ctx!.shadowBlur = 0;

      // 코너 정합 마크(L 브래킷)
      const cm = 26;
      const corners: [number, number, number, number][] = [
        [die.x, die.y, 1, 1], [die.x + die.w, die.y, -1, 1],
        [die.x, die.y + die.h, 1, -1], [die.x + die.w, die.y + die.h, -1, -1],
      ];
      ctx!.strokeStyle = 'rgba(150, 190, 255, 0.7)';
      ctx!.lineWidth = 2;
      for (const [cx, cy, sx, sy] of corners) {
        ctx!.beginPath();
        ctx!.moveTo(cx + sx * cm, cy); ctx!.lineTo(cx, cy); ctx!.lineTo(cx, cy + sy * cm);
        ctx!.stroke();
      }

      // 본딩 패드
      for (let i = 0; i < pads.length; i++) {
        const p = pads[i];
        const blink = 0.28 + 0.28 * Math.sin(t * 0.05 + i * 0.9);
        ctx!.fillStyle = `rgba(110, 150, 235, ${0.35 + blink * 0.4})`;
        const s = 4;
        // 패드를 변 바깥쪽으로 살짝 물려 그린다
        ctx!.fillRect(p.x - s / 2 + p.nx * 3, p.y - s / 2 + p.ny * 3, s, s);
      }

      // 전자 신호 흐름 (배선 위를 따라 이동)
      for (const e of electrons) {
        const tr = traces[e.ti];
        if (!tr) { Object.assign(e, spawn()); continue; }
        e.len += e.spd * e.dir;
        if (e.len < -6 || e.len > tr.len + 6) { Object.assign(e, spawn()); continue; }
        const col = e.hue < 0.5 ? '120, 190, 255' : e.hue < 0.8 ? '90, 230, 245' : '160, 150, 255';
        // 트레일
        for (let k = 5; k >= 0; k--) {
          const pos = posAt(tr, e.len - e.dir * k * 8);
          const a = (1 - k / 6) * 0.5;
          const r = 2.4 - k * 0.3;
          ctx!.fillStyle = `rgba(${col}, ${a})`;
          ctx!.beginPath(); ctx!.arc(pos.x, pos.y, Math.max(0.5, r), 0, Math.PI * 2); ctx!.fill();
        }
        // 헤드 글로우
        const head = posAt(tr, e.len);
        const g = ctx!.createRadialGradient(head.x, head.y, 0, head.x, head.y, 10);
        g.addColorStop(0, `rgba(${col}, 0.9)`);
        g.addColorStop(1, `rgba(${col}, 0)`);
        ctx!.fillStyle = g;
        ctx!.beginPath(); ctx!.arc(head.x, head.y, 10, 0, Math.PI * 2); ctx!.fill();
        ctx!.fillStyle = `rgba(235, 245, 255, 0.95)`;
        ctx!.beginPath(); ctx!.arc(head.x, head.y, 1.6, 0, Math.PI * 2); ctx!.fill();
      }

      raf = requestAnimationFrame(frame);
    }
    frame();
    if (reduce) cancelAnimationFrame(raf);
    return () => { cancelAnimationFrame(raf); window.removeEventListener('resize', build); };
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
      <div className="lg-grid" />
      <ChipDie />
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
