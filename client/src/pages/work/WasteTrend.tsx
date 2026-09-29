import { useEffect, useMemo, useState } from 'react';
import { api } from '../../api/client';
import type { WasteLog, WasteMonth, WasteTrendPoint } from '../../api/types';
import { useIsMobile } from '../../hooks/useIsMobile';

// KOH·폐액 추이 — "어디서 늘고 줄었는지, 원인이 무엇인지" 찾는 화면.
// 1) 항목 하나(폐액 증가·수거, KOH 사용·보충)를 골라 크게 본다 — 크기가 다른 두 값을 한 그림·두 축에 겹치지 않는다.
// 2) 기간(전체·최근 12개월·연도)을 고르면 그 기간의 평균선과, 평균보다 크게 튄 달(평균 + 1.5×표준편차)을 주황으로 표시한다.
// 3) 달 막대(또는 '튄 달' 목록)를 누르면 그달을 날짜별로 풀어 보여 준다 — 날마다 주·야 값과 교체 설비·비고,
//    그 달 평균 대비·전년 같은 달 대비를 같이 적어 원인을 바로 옆에서 확인한다.

type Metric = 'inc' | 'removed' | 'used' | 'refill';
const METRICS: { key: Metric; label: string; pick: (p: WasteTrendPoint) => number }[] = [
  { key: 'inc', label: '폐액 증가', pick: p => p.wasteIncrease },
  { key: 'removed', label: '폐액 수거', pick: p => p.wasteRemoved },
  { key: 'used', label: 'KOH 사용', pick: p => p.causticUsed },
  { key: 'refill', label: 'KOH 보충', pick: p => p.causticRefill },
];
/** 하루 한 교대 값 — 月 합계와 같은 규칙(사용·증가는 양수만, 보충·수거는 반대 방향만) */
const rowValue = (r: WasteLog, m: Metric) => {
  const v = m === 'inc' || m === 'removed' ? r.wasteIncrease : r.causticUsed;
  if (v === null) return 0;
  return m === 'inc' || m === 'used' ? Math.max(0, v) : Math.max(0, -v);
};

const nf = (v: number) => Number(v.toFixed(1)).toLocaleString('ko-KR');
const pct = (a: number, b: number) => (b === 0 ? null : Math.round(((a - b) / b) * 100));
const pad = (n: number) => String(n).padStart(2, '0');
const DOW = ['일', '월', '화', '수', '목', '금', '토'];

function niceMax(v: number) {
  if (v <= 0) return 1;
  const p = Math.pow(10, Math.floor(Math.log10(v)));
  const m = v / p;
  return (m <= 1 ? 1 : m <= 2 ? 2 : m <= 2.5 ? 2.5 : m <= 5 ? 5 : 10) * p;
}

interface Bar { key: string; value: number; flag: boolean; tick?: string; tip: string[] }

/** 막대 그림 하나(한 항목). 평균선(점선) + 튄 막대 주황 + 고른 막대 진하게. 막대보다 넓은 칸을 눌러도 고른다. */
function Bars({ bars, avg, picked, onPick, height }: {
  bars: Bar[]; avg: number | null; picked: string | null; onPick?: (key: string) => void; height: number;
}) {
  const isMobile = useIsMobile();
  const [hover, setHover] = useState<number | null>(null);
  const W = isMobile ? 460 : 1000, H = height, L = 46, R = 8, T = 16, B = 24;
  const pw = W - L - R, ph = H - T - B;
  const max = niceMax(Math.max(0, ...bars.map(b => b.value)));
  const step = pw / Math.max(bars.length, 1);
  const gap = step > 6 ? 2 : 1;
  const bw = Math.max(1, step - gap);
  const y = (v: number) => T + ph - (Math.max(0, v) / max) * ph;
  const ticks = [0, 0.5, 1].map(f => f * max);
  const hb = hover === null ? null : bars[hover];
  return (
    <div className="wf-chart-box">
      <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" role="img"
        onMouseLeave={() => setHover(null)}
        onMouseMove={e => {
          const r = (e.currentTarget as SVGSVGElement).getBoundingClientRect();
          const i = Math.floor((((e.clientX - r.left) / r.width) * W - L) / step);
          setHover(i >= 0 && i < bars.length ? i : null);
        }}
        onClick={() => { if (hover !== null && onPick) onPick(bars[hover].key); }}
        style={{ height, cursor: onPick ? 'pointer' : 'default' }}>
        {ticks.map(t => (
          <g key={t}>
            <line x1={L} x2={W - R} y1={y(t)} y2={y(t)} className="wf-grid-line" />
            <text x={L - 6} y={y(t) + 4} textAnchor="end" className="wf-axis">{nf(t)}</text>
          </g>
        ))}
        {bars.map((b, i) => (b.tick ? (
          <g key={`t${b.key}`}>
            <line x1={L + i * step} x2={L + i * step} y1={T} y2={T + ph} className="wf-year-line" />
            <text x={L + i * step + 3} y={H - 7} className="wf-axis">{b.tick}</text>
          </g>
        ) : null))}
        {bars.map((b, i) => {
          const top = y(b.value);
          return <rect key={b.key} x={L + i * step + gap / 2} y={top} width={bw} height={Math.max(0, T + ph - top)} rx={Math.min(3, bw / 2)}
            className={`wf-bar ${b.flag ? 'flag' : ''} ${picked === b.key ? 'picked' : ''} ${hover === i ? 'on' : ''}`} />;
        })}
        {avg !== null && avg > 0 && (
          <g>
            <line x1={L} x2={W - R} y1={y(avg)} y2={y(avg)} className="wf-avg-line" />
            <text x={W - R - 4} y={y(avg) - 4} textAnchor="end" className="wf-axis wf-avg-txt">평균 {nf(avg)}</text>
          </g>
        )}
      </svg>
      {hb && hover !== null && (
        <div className="wf-tip" style={{ left: `${Math.min(88, Math.max(12, ((L + (hover + 0.5) * step) / W) * 100))}%` }}>
          {hb.tip.map((t, i) => (i === 0 ? <b key={i}>{t}</b> : <span key={i}>{t}</span>))}
        </div>
      )}
    </div>
  );
}

export default function WasteTrend({ points }: { points: WasteTrendPoint[] | null }) {
  const [metric, setMetric] = useState<Metric>('inc');
  const [period, setPeriod] = useState<string>('12m');   // 'all' | '12m' | 연도
  const [picked, setPicked] = useState<string | null>(null);   // 'YYYY-MM'
  const M = METRICS.find(m => m.key === metric)!;

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
  const years = useMemo(() => [...new Set(filled.map(p => p.year))].sort((a, b) => b - a), [filled]);
  const shown = useMemo(() => (period === 'all' ? filled : period === '12m' ? filled.slice(-12) : filled.filter(p => String(p.year) === period)), [filled, period]);
  const byKey = useMemo(() => new Map(filled.map(p => [`${p.year}-${pad(p.month)}`, p])), [filled]);

  // 기간 평균·표준편차 — 기록이 있는 달만(빈 달 0 이 평균을 끌어내리지 않게)
  const stat = useMemo(() => {
    const vals = shown.filter(p => p.days > 0).map(M.pick);
    if (vals.length === 0) return { avg: null as number | null, limit: Infinity };
    const avg = vals.reduce((s, v) => s + v, 0) / vals.length;
    const sd = Math.sqrt(vals.reduce((s, v) => s + (v - avg) ** 2, 0) / vals.length);
    return { avg, limit: avg + 1.5 * sd };
  }, [shown, M]);
  const bars: Bar[] = shown.map((p, i) => {
    const v = M.pick(p);
    const ly = byKey.get(`${p.year - 1}-${pad(p.month)}`);
    const yoy = ly && ly.days > 0 ? pct(v, M.pick(ly)) : null;
    return {
      key: `${p.year}-${pad(p.month)}`, value: v, flag: p.days > 0 && v > stat.limit,
      tick: p.month === 1 || i === 0 ? (shown.length > 14 ? String(p.year) : `${p.year % 100}.${p.month}`) : (shown.length <= 14 ? String(p.month) : undefined),
      tip: [`${p.year}년 ${p.month}월`, `${M.label} ${nf(v)}`,
        stat.avg ? `평균 대비 ${pct(v, stat.avg)! >= 0 ? '+' : ''}${pct(v, stat.avg)}%` : '',
        yoy !== null ? `전년 같은 달 대비 ${yoy >= 0 ? '+' : ''}${yoy}%` : '',
        `기록 ${p.days}일 · 약액 교체 ${p.changes}회`].filter(Boolean),
    };
  });
  const top = [...bars].filter(b => b.value > 0).sort((a, b) => b.value - a.value).slice(0, 5);
  const yearRows = useMemo(() => {
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
      <div className="wf-trend-bar">
        <div className="wf-seggroup">
          {METRICS.map(m => <button key={m.key} className={`wf-seg ${metric === m.key ? 'on' : ''}`} onClick={() => setMetric(m.key)}>{m.label}</button>)}
        </div>
        <select className="input wf-bsel" value={period} onChange={e => { setPeriod(e.target.value); setPicked(null); }}>
          <option value="12m">최근 12개월</option>
          <option value="all">전체({filled[0].year}~)</option>
          {years.map(y => <option key={y} value={String(y)}>{y}년</option>)}
        </select>
      </div>

      <figure className="wf-chart">
        <figcaption>
          <b>{M.label}</b><span>월 합계 · 막대를 누르면 그달을 날짜별로 봅니다 · 주황 = 평균보다 크게 튄 달</span>
        </figcaption>
        <Bars bars={bars} avg={stat.avg} picked={picked} onPick={setPicked} height={240} />
      </figure>

      <div className="wf-tops">
        <span className="wf-tops-h">{M.label} 많은 달</span>
        {top.map(b => (
          <button key={b.key} className={`wf-top ${b.flag ? 'flag' : ''} ${picked === b.key ? 'on' : ''}`} onClick={() => setPicked(b.key)}>
            {b.key.replace('-', '.')} <b>{nf(b.value)}</b>
            {stat.avg ? <small>평균 {pct(b.value, stat.avg)! >= 0 ? '+' : ''}{pct(b.value, stat.avg)}%</small> : null}
          </button>
        ))}
      </div>

      {picked && <MonthDetail ym={picked} metric={metric} monthAvg={stat.avg} prevYear={byKey.get(`${Number(picked.slice(0, 4)) - 1}-${picked.slice(5)}`)} onClose={() => setPicked(null)} />}

      <div className="wf-yscroll">
        <table className="wf-ytable">
          <thead><tr><th>연도</th><th>폐액 증가</th><th>폐액 수거</th><th>KOH 사용</th><th>KOH 보충</th><th>약액 교체</th><th>기록한 날</th></tr></thead>
          <tbody>
            {yearRows.map(([yr, a]) => (
              <tr key={yr} className={period === String(yr) ? 'on' : ''} onClick={() => { setPeriod(String(yr)); setPicked(null); }} title="이 해만 보기">
                <td>{yr}년</td><td>{nf(a.waste)}</td><td>{nf(a.removed)}</td><td>{nf(a.caustic)}</td><td>{nf(a.refill)}</td><td>{a.changes}회</td><td>{a.days}일</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

/** 한 달을 날짜별로 — 날마다 주·야 값, 교체 설비, 비고. 그달에서 크게 튄 날을 주황으로. */
function MonthDetail({ ym, metric, monthAvg, prevYear, onClose }: {
  ym: string; metric: Metric; monthAvg: number | null; prevYear?: WasteTrendPoint; onClose: () => void;
}) {
  const [data, setData] = useState<WasteMonth | null>(null);
  const M = METRICS.find(m => m.key === metric)!;
  const y = Number(ym.slice(0, 4)), m = Number(ym.slice(5));
  const last = new Date(y, m, 0).getDate();
  useEffect(() => {
    let alive = true;
    setData(null);
    api.get<WasteMonth>(`/api/worklog/waste/range?from=${ym}-01&to=${ym}-${pad(last)}`)
      .then(r => { if (alive) setData(r); }).catch(() => { if (alive) setData({ year: y, month: m, rows: [], prevCausticAfter: null, prevWasteAfter: null, chemicalByDate: {} }); });
    return () => { alive = false; };
  }, [ym, last, y, m]);

  const days = useMemo(() => Array.from({ length: last }, (_, i) => {
    const date = `${ym}-${pad(i + 1)}`;
    const rs = (data?.rows ?? []).filter(r => r.date === date);
    const day = rs.find(r => r.shift === '주'); const night = rs.find(r => r.shift === '야');
    const dv = day ? rowValue(day, metric) : 0, nv = night ? rowValue(night, metric) : 0;
    return {
      date, d: i + 1, dow: new Date(y, m - 1, i + 1).getDay(), has: rs.length > 0, day: dv, night: nv, total: dv + nv,
      eq: [...new Set(rs.flatMap(r => [r.dipEquipment, r.sprayEquipment]).filter(Boolean))].join(', '),
      note: rs.map(r => r.note).filter(Boolean).join(' / '),
    };
  }), [data, ym, last, y, m, metric]);
  const withData = days.filter(d => d.has);
  const total = withData.reduce((s, d) => s + d.total, 0);
  const davg = withData.length ? total / withData.length : 0;
  const dsd = withData.length ? Math.sqrt(withData.reduce((s, d) => s + (d.total - davg) ** 2, 0) / withData.length) : 0;
  const limit = davg + 1.5 * dsd;
  const bars: Bar[] = days.map(d => ({
    key: d.date, value: d.total, flag: d.has && d.total > 0 && d.total > limit,
    tick: d.d === 1 || d.d % 5 === 0 ? String(d.d) : undefined,
    tip: [`${m}/${d.d} (${DOW[d.dow]})`, `${M.label} ${nf(d.total)} (주 ${nf(d.day)} · 야 ${nf(d.night)})`, d.eq ? `교체 ${d.eq}` : '', d.note].filter(Boolean),
  }));
  const vsAvg = monthAvg ? pct(total, monthAvg) : null;
  const yoy = prevYear && prevYear.days > 0 ? pct(total, M.pick(prevYear)) : null;
  // 표: 값이 있거나 교체·비고가 있는 날만(아무 일 없는 날은 줄인다)
  const listed = days.filter(d => d.total > 0 || d.eq || d.note);

  return (
    <section className="wf-bblock wf-mdetail">
      <div className="wf-bhead">
        <b>{y}년 {m}월 · {M.label} {nf(total)}</b>
        {vsAvg !== null && <span className={`wf-cmpchip ${vsAvg > 0 ? 'up' : 'down'}`}>기간 평균 대비 {vsAvg >= 0 ? '+' : ''}{vsAvg}%</span>}
        {yoy !== null && <span className={`wf-cmpchip ${yoy > 0 ? 'up' : 'down'}`}>전년 {m}월 대비 {yoy >= 0 ? '+' : ''}{yoy}%</span>}
        <span style={{ flex: 1 }} />
        <button className="btn btn-ghost wf-sm" onClick={onClose}>닫기</button>
      </div>
      {!data ? <div className="wf-empty">불러오는 중…</div> : (<>
        <Bars bars={bars} avg={withData.length ? davg : null} picked={null} height={180} />
        {listed.length === 0 ? <div className="wf-empty">이달 {M.label} 기록이 없습니다.</div> : (
          <div className="wf-gridwrap wf-mdtable">
            <table className="wf-wtable">
              <thead><tr><th>날짜</th><th>주</th><th>야</th><th>합계</th><th>교체 설비</th><th>비고</th></tr></thead>
              <tbody>
                {listed.map(d => (
                  <tr key={d.date} className={d.has && d.total > limit && d.total > 0 ? 'wf-hot' : ''}>
                    <td className={`dow${d.dow}`}>{m}/{d.d} ({DOW[d.dow]})</td>
                    <td className="n">{d.day ? nf(d.day) : ''}</td><td className="n">{d.night ? nf(d.night) : ''}</td>
                    <td className="n"><b>{d.total ? nf(d.total) : ''}</b></td>
                    <td>{d.eq}</td>
                    <td className="wf-mdnote">{d.note}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </>)}
    </section>
  );
}
