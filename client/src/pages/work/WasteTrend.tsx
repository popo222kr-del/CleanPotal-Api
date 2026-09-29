import { useMemo, useState } from 'react';
import type { WasteTrendPoint } from '../../api/types';
import { useIsMobile } from '../../hooks/useIsMobile';

// KOH·폐액 월별 추이. 두 값은 크기가 달라(KOH 수천, 폐액 수십) 한 그림에 축 두 개로 겹치지 않고 그림을 나눈다.
// 막대 하나 = 한 달. 마우스를 올리면 그 달 값을, 아래 표는 해마다 합계를 보여 준다(그림 대신 숫자로 볼 때).

const nf = (v: number) => Number(v.toFixed(1)).toLocaleString('ko-KR');

function niceMax(v: number) {
  if (v <= 0) return 1;
  const p = Math.pow(10, Math.floor(Math.log10(v)));
  const m = v / p;
  return (m <= 1 ? 1 : m <= 2 ? 2 : m <= 2.5 ? 2.5 : m <= 5 ? 5 : 10) * p;
}

function BarChart({ title, unit, points, value }: {
  title: string; unit: string; points: WasteTrendPoint[]; value: (p: WasteTrendPoint) => number;
}) {
  const [hover, setHover] = useState<number | null>(null);
  // 폰은 그림 폭(좌표)을 줄여 축 글자가 너무 작아지지 않게
  const isMobile = useIsMobile();
  const W = isMobile ? 460 : 1000, H = isMobile ? 240 : 220, L = 46, R = 8, T = 10, B = 26;
  const pw = W - L - R, ph = H - T - B;
  const max = niceMax(Math.max(0, ...points.map(value)));
  const step = pw / Math.max(points.length, 1);
  const bw = Math.max(1, step - 2);                 // 막대 사이 2px
  const y = (v: number) => T + ph - (Math.max(0, v) / max) * ph;
  const ticks = [0, 0.25, 0.5, 0.75, 1].map(f => f * max);
  const hp = hover === null ? null : points[hover];

  return (
    <figure className="wf-chart">
      <figcaption><b>{title}</b><span>월 합계{unit ? ` · ${unit}` : ''}</span></figcaption>
      <div className="wf-chart-box">
        <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" role="img" aria-label={`${title} 월별 막대그래프`}
          onMouseLeave={() => setHover(null)}
          onMouseMove={e => {
            const r = (e.currentTarget as SVGSVGElement).getBoundingClientRect();
            const x = ((e.clientX - r.left) / r.width) * W - L;
            const i = Math.floor(x / step);
            setHover(i >= 0 && i < points.length ? i : null);
          }}>
          {ticks.map(t => (
            <g key={t}>
              <line x1={L} x2={W - R} y1={y(t)} y2={y(t)} className="wf-grid-line" />
              <text x={L - 6} y={y(t) + 4} textAnchor="end" className="wf-axis">{nf(t)}</text>
            </g>
          ))}
          {points.map((p, i) => (p.month === 1 ? (
            <g key={`y${p.year}`}>
              <line x1={L + i * step} x2={L + i * step} y1={T} y2={T + ph} className="wf-year-line" />
              <text x={L + i * step + 3} y={H - 8} className="wf-axis">{p.year}</text>
            </g>
          ) : null))}
          {points.map((p, i) => {
            const v = value(p); const top = y(v);
            return <rect key={i} x={L + i * step + 1} y={top} width={bw} height={Math.max(0, T + ph - top)} rx={Math.min(2, bw / 2)}
              className={`wf-bar ${hover === i ? 'on' : ''}`} />;
          })}
        </svg>
        {hp && hover !== null && (
          <div className="wf-tip" style={{ left: `${((L + (hover + 0.5) * step) / W) * 100}%` }}>
            <b>{hp.year}년 {hp.month}월</b>
            <span>{title} <b>{nf(value(hp))}</b></span>
            <span>기록 {hp.days}일 · 약액 교체 {hp.changes}회</span>
          </div>
        )}
      </div>
    </figure>
  );
}

export default function WasteTrend({ points }: { points: WasteTrendPoint[] | null }) {
  // 빈 달도 0 으로 채워 시간 간격이 그대로 보이게
  const filled = useMemo(() => {
    if (!points || points.length === 0) return [];
    const out: WasteTrendPoint[] = [];
    const map = new Map(points.map(p => [`${p.year}-${p.month}`, p]));
    let y = points[0].year, m = points[0].month;
    const last = points[points.length - 1];
    while (y < last.year || (y === last.year && m <= last.month)) {
      out.push(map.get(`${y}-${m}`) ?? { year: y, month: m, causticUsed: 0, wasteIncrease: 0, days: 0, changes: 0, causticRefill: 0, wasteRemoved: 0 });
      m++; if (m > 12) { m = 1; y++; }
    }
    return out;
  }, [points]);
  const years = useMemo(() => {
    const g = new Map<number, { caustic: number; refill: number; waste: number; removed: number; days: number; changes: number }>();
    for (const p of filled) {
      const a = g.get(p.year) ?? { caustic: 0, refill: 0, waste: 0, removed: 0, days: 0, changes: 0 };
      a.caustic += p.causticUsed; a.refill += p.causticRefill; a.waste += p.wasteIncrease; a.removed += p.wasteRemoved;
      a.days += p.days; a.changes += p.changes;
      g.set(p.year, a);
    }
    return [...g.entries()].sort((a, b) => b[0] - a[0]);
  }, [filled]);

  if (!points) return <div className="wf-empty">불러오는 중…</div>;
  if (filled.length === 0) return <div className="wf-empty">기록이 없습니다. 엑셀 가져오기로 2019년부터의 자료를 넣으세요.</div>;
  return (
    <div className="wf-trend">
      <BarChart title="폐액 증가량" unit="" points={filled} value={p => p.wasteIncrease} />
      <BarChart title="KOH(가성소다) 사용량" unit="" points={filled} value={p => p.causticUsed} />
      <div className="wf-yscroll"><table className="wf-ytable">
        <thead><tr><th>연도</th><th>폐액 증가</th><th>폐액 수거</th><th>KOH 사용</th><th>KOH 보충</th><th>약액 교체</th><th>기록한 날</th></tr></thead>
        <tbody>
          {years.map(([yr, a]) => (
            <tr key={yr}><td>{yr}년</td><td>{nf(a.waste)}</td><td>{nf(a.removed)}</td><td>{nf(a.caustic)}</td><td>{nf(a.refill)}</td><td>{a.changes}회</td><td>{a.days}일</td></tr>
          ))}
        </tbody>
      </table></div>
    </div>
  );
}
