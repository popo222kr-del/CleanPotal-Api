import { useEffect, useMemo, useRef, useState } from 'react';
import { api } from '../../api/client';
import NotConnected from './NotConnected';
import { useLegacyStatus, won } from './mesReport';
import './MesReport.css';
import './MesTrend.css';

// MES 조회 › 매출 트렌드 — 출하 기준(출하실적 기본 규칙: 무상세정·부적합(세정 SKIP+NG) 빼고 케미칼 넣음).
// 위에서 아래로 결론(카드) → 흐름(이번 달 페이스·월·주 추이) → 원인(구분별·물량·단가 증감) → 무엇이(증감 TOP).
// 주 = 월~일, 월말에 걸친 주는 말일에서 끊고 1일부터 새 달 1주. 비교는 늘 "같은 날까지"(이번 달 14일이면 작년 10월 1~14일).

interface Compare { value: number; base: number | null; baseLabel: string; rate: number | null }
interface Card { key: string; title: string; period: string; value: number; compares: Compare[]; note: string | null }
interface GroupRow { name: string; parent: string; subtotal: boolean; mtd: number; lyMtd: number; ytd: number; lyYtd: number }
interface MonthSeries { year: number; total: number[]; byGroup: Record<string, number[]> }
interface WeekPoint { label: string; start: string; end: string; days: number; partial: boolean; amount: number; byGroup: Record<string, number> }
interface DailySeries { label: string; year: number; month: number; days: number; amount: number[]; upTo: number }
interface BridgeStep { label: string; value: number; kind: 'base' | 'delta' | 'total' }
interface Mover { key: string; name: string; sub: string; cur: number; prev: number; curQty: number; prevQty: number; diff: number }
interface MoverSet { dim: string; label: string; up: Mover[]; down: Mover[]; count: number }
interface Report {
  asOf: string; periodLabel: string; lyLabel: string; groups: string[]; cards: Card[]; groupRows: GroupRow[];
  thisYear: MonthSeries; lastYear: MonthSeries; weeks: WeekPoint[]; daily: DailySeries[];
  groupBridge: BridgeStep[]; priceVolume: BridgeStep[]; movers: MoverSet[];
}

/** 구분 색 — 순서대로 고정(구분이 늘면 다음 색). 같은 구분은 모든 그래프에서 같은 색. */
const PALETTE = ['#335BFF', '#0E9F9A', '#E0901A', '#8B6FD6', '#D9548A', '#5F8D3E'];
const UP = '#1E8E4E', DOWN = '#D23F3F';

/** 큰 금액은 억·만 — 표·카드용. 정확한 값은 툴팁에 원 단위로. */
function krw(n: number): string {
  const a = Math.abs(n), s = n < 0 ? '-' : '';
  if (a >= 1e8) return `${s}${(a / 1e8).toFixed(a >= 1e10 ? 0 : 2)}억`;
  if (a >= 1e4) return `${s}${Math.round(a / 1e4).toLocaleString()}만`;
  return `${s}${Math.round(a).toLocaleString()}`;
}
/** 축 눈금 — 억 단위 짧게 */
const axisKrw = (n: number) => (n === 0 ? '0' : n >= 1e8 ? `${+(n / 1e8).toFixed(1)}억` : `${Math.round(n / 1e4).toLocaleString()}만`);
const pct = (r: number | null) => (r == null ? '—' : `${r >= 0 ? '+' : ''}${(r * 100).toFixed(1)}%`);
const today = () => { const d = new Date(); return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 10); };

function Delta({ rate, small }: { rate: number | null; small?: boolean }) {
  if (rate == null) return <span className={`tr-delta none${small ? ' sm' : ''}`}>비교 없음</span>;
  const up = rate >= 0;
  return <span className={`tr-delta ${up ? 'up' : 'down'}${small ? ' sm' : ''}`}>{up ? '▲' : '▼'} {Math.abs(rate * 100).toFixed(1)}%</span>;
}

export default function MesTrend() {
  const status = useLegacyStatus();
  const [date, setDate] = useState(today());
  const [data, setData] = useState<Report | null>(null);
  const [err, setErr] = useState('');
  const [loading, setLoading] = useState(false);

  async function load(d = date) {
    setLoading(true); setErr('');
    try { setData(await api.get<Report>(`/api/mes-report/trend?date=${d}`)); }
    catch (e) { setErr(e instanceof Error ? e.message : '불러오지 못했습니다.'); }
    finally { setLoading(false); }
  }
  useEffect(() => { if (status?.configured) void load(); }, [status?.configured]);   // eslint-disable-line react-hooks/exhaustive-deps

  if (status === undefined) return <div className="page-loading">불러오는 중…</div>;
  if (!status?.configured) return <NotConnected title="매출 트렌드" />;
  const color = (g: string) => PALETTE[Math.max(0, data?.groups.indexOf(g) ?? 0) % PALETTE.length];

  return (
    <div className="mr-page tr-page">
      <header className="pg-header">
        <div><h2>매출 트렌드</h2><p>출하 기준 · 무상세정·부적합(세정 SKIP + 출하검사 NG) 제외, 케미칼(SD-1) 포함 · 금액 = 입고일 단가 × 수량 · 비교는 같은 날짜까지 · 주는 월~일(월말에서 끊음)</p></div>
      </header>
      <div className="pg-body">
        <div className="mr-filters">
          <label className="tr-date">기준일 <input type="date" className="input" value={date} max={today()} onChange={e => setDate(e.target.value)} /></label>
          <button type="button" className="btn btn-primary" onClick={() => void load()} disabled={loading}>{loading ? '조회 중…' : '조회'}</button>
          {date !== today() && <button type="button" className="btn btn-ghost" onClick={() => { const t = today(); setDate(t); void load(t); }}>오늘</button>}
          {data && <span className="mr-dim">이번 기간 {data.periodLabel} · 비교 {data.lyLabel}</span>}
        </div>
        {err && <div className="mr-err">{err}</div>}
        {!data && loading && <div className="page-loading">올해·작년 출하를 모으는 중…</div>}

        {data && (
          <>
            <div className="tr-cards">
              {data.cards.map(c => (
                <div key={c.key} className={`tr-card${c.key === 'mtd' ? ' main' : ''}`}>
                  <div className="tr-card-t">{c.title}<span>{c.period}</span></div>
                  <div className="tr-card-v" title={`${won(c.value)}원`}>{krw(c.value)}<small>원</small></div>
                  {c.compares.map((x, i) => (
                    <div key={i} className="tr-card-c">
                      <Delta rate={x.rate} small={i > 0} />
                      <span className="tr-card-b" title={`${x.baseLabel}${x.base == null ? '' : ` ${won(x.base)}원`}`}>{x.baseLabel} {x.base == null ? '' : krw(x.base)}</span>
                    </div>
                  ))}
                  {c.note && <div className="tr-card-n">{c.note}</div>}
                </div>
              ))}
            </div>

            <div className="tr-row two">
              <Pace daily={data.daily} />
              <GroupTable rows={data.groupRows} color={color} />
            </div>

            <div className="tr-row two">
              <MonthChart data={data} color={color} />
              <WeekChart weeks={data.weeks} groups={data.groups} color={color} />
            </div>

            <div className="tr-row two">
              <Waterfall title="작년 같은 기간 대비 — 어느 구분에서 늘고 줄었나" steps={data.groupBridge} />
              <Waterfall title="작년 같은 기간 대비 — 물량 때문인가, 단가 때문인가" steps={data.priceVolume}
                hint="품목마다: 물량 = (올해 수량 − 작년 수량) × 작년 평균단가, 단가 = (올해 평균단가 − 작년 평균단가) × 올해 수량. 한쪽 기간에만 있는 품목은 새 품목·끊긴 품목." />
            </div>

            <Movers sets={data.movers} />
          </>
        )}
      </div>
    </div>
  );
}

// ── 공통: 차트 틀 + 툴팁 ─────────────────────────────

function ChartCard({ title, legend, children, hint }: { title: string; legend?: React.ReactNode; children: React.ReactNode; hint?: string }) {
  return (
    <div className="mr-card tr-chart">
      <div className="mr-card-h"><b>{title}</b>{legend && <div className="tr-legend">{legend}</div>}</div>
      <div className="tr-chart-b">{children}</div>
      {hint && <div className="tr-hint">{hint}</div>}
    </div>
  );
}
const Key = ({ c, label, line, dash }: { c: string; label: string; line?: boolean; dash?: boolean }) => (
  <span className="tr-key"><i className={line ? `ln${dash ? ' dash' : ''}` : ''} style={{ background: line ? undefined : c, borderColor: c }} />{label}</span>
);

/** 마우스 위치에 뜨는 값 상자 — 칸(index) 단위로 붙는다 */
function useHover() {
  const [h, setH] = useState<{ i: number; x: number; y: number } | null>(null);
  const ref = useRef<HTMLDivElement>(null);
  const on = (i: number) => (e: React.MouseEvent) => {
    const r = ref.current?.getBoundingClientRect();
    if (r) setH({ i, x: e.clientX - r.left, y: e.clientY - r.top });
  };
  return { h, ref, on, off: () => setH(null) };
}
function Tip({ h, w, children }: { h: { x: number; y: number }; w: number; children: React.ReactNode }) {
  const left = h.x > w - 200 ? h.x - 190 : h.x + 14;
  return <div className="tr-tip" style={{ left, top: Math.max(4, h.y - 10) }}>{children}</div>;
}
const niceMax = (v: number) => { if (v <= 0) return 1; const p = 10 ** Math.floor(Math.log10(v)); const m = v / p; return (m <= 1 ? 1 : m <= 2 ? 2 : m <= 2.5 ? 2.5 : m <= 5 ? 5 : 10) * p; };

// ── 이번 달 누적 페이스 ────────────────────────────

function Pace({ daily }: { daily: DailySeries[] }) {
  const { h, ref, on, off } = useHover();
  const W = 640, H = 260, L = 56, R = 14, T = 14, B = 26;
  const cum = daily.map(s => { let t = 0; return s.amount.map((v, i) => (i < s.upTo ? (t += v) : null)); });
  const max = niceMax(Math.max(1, ...cum.flat().map(v => v ?? 0)));
  const days = Math.max(...daily.map(s => s.days));
  const x = (d: number) => L + ((W - L - R) * d) / (days - 1 || 1);
  const y = (v: number) => T + (H - T - B) * (1 - v / max);
  const styles = [{ c: PALETTE[0], w: 2.5, dash: '' }, { c: '#8E8E93', w: 2, dash: '' }, { c: '#B8BCC6', w: 2, dash: '5 4' }];
  const cur = daily[0];
  return (
    <ChartCard title={`이번 달 누적 페이스 — ${cur.label}`}
      legend={daily.map((s, i) => <Key key={s.label} c={styles[i].c} label={s.label} line dash={!!styles[i].dash} />)}>
      <div ref={ref} className="tr-svg-wrap" onMouseLeave={off}>
        <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label="이번 달 누적 매출과 작년 같은 달·지난달 비교">
          {[0, 0.25, 0.5, 0.75, 1].map(f => (
            <g key={f}><line x1={L} x2={W - R} y1={y(f * max)} y2={y(f * max)} className="grid" /><text x={L - 6} y={y(f * max) + 4} textAnchor="end" className="tick">{axisKrw(f * max)}</text></g>
          ))}
          {[1, 5, 10, 15, 20, 25, days].map(d => <text key={d} x={x(d - 1)} y={H - 8} textAnchor="middle" className="tick">{d}일</text>)}
          {cum.map((s, si) => {
            const pts = s.map((v, i) => (v == null ? null : `${x(i)},${y(v)}`)).filter(Boolean);
            return pts.length > 0 && <polyline key={si} points={pts.join(' ')} fill="none" stroke={styles[si].c} strokeWidth={styles[si].w} strokeDasharray={styles[si].dash} strokeLinejoin="round" strokeLinecap="round" />;
          }).reverse()}
          {cur.upTo > 0 && cum[0][cur.upTo - 1] != null && (
            <g><circle cx={x(cur.upTo - 1)} cy={y(cum[0][cur.upTo - 1]!)} r={4.5} fill={PALETTE[0]} stroke="#fff" strokeWidth={2} />
              <text x={x(cur.upTo - 1)} y={y(cum[0][cur.upTo - 1]!) - 10} textAnchor={cur.upTo > days * 0.8 ? 'end' : 'middle'} className="tr-lbl">{krw(cum[0][cur.upTo - 1]!)}</text></g>
          )}
          {h && <line x1={x(h.i)} x2={x(h.i)} y1={T} y2={H - B} className="tr-cross" />}
          {Array.from({ length: days }, (_, i) => (
            <rect key={i} x={x(i) - (W - L - R) / (days - 1) / 2} y={T} width={(W - L - R) / (days - 1)} height={H - T - B} fill="transparent" onMouseMove={on(i)} />
          ))}
        </svg>
        {h && (
          <Tip h={h} w={ref.current?.clientWidth ?? W}>
            <b>{h.i + 1}일까지 누적</b>
            {daily.map((s, si) => <div key={s.label}><i style={{ background: styles[si].c }} />{s.label} <span>{cum[si][h.i] == null ? '—' : `${won(cum[si][h.i]!)}원`}</span></div>)}
          </Tip>
        )}
      </div>
    </ChartCard>
  );
}

// ── 구분별 표 ─────────────────────────────────────

function GroupTable({ rows, color }: { rows: GroupRow[]; color: (g: string) => string }) {
  const all = useMemo(() => {
    const leaf = rows.filter(r => !r.subtotal);
    const s = (k: keyof GroupRow) => leaf.reduce((a, r) => a + (r[k] as number), 0);
    return { mtd: s('mtd'), lyMtd: s('lyMtd'), ytd: s('ytd'), lyYtd: s('lyYtd') };
  }, [rows]);
  const rate = (a: number, b: number) => (b > 0 ? (a - b) / b : null);
  return (
    <div className="mr-card tr-chart">
      <div className="mr-card-h"><b>구분별 — 이번 달 누계 · 올해 누계</b></div>
      <div className="mr-scroll">
        <table className="mr-table tr-gt">
          <thead><tr><th>구분</th><th>이번 달</th><th>작년 같은 기간</th><th>증감</th><th>비중</th><th>올해 누계</th><th>작년 대비</th></tr></thead>
          <tbody>
            {rows.map(r => (
              <tr key={r.name} className={r.subtotal ? 'sub' : ''}>
                <td className="nm">{!r.subtotal && <i style={{ background: color(r.name) }} />}{r.name}</td>
                <td title={`${won(r.mtd)}원`}>{krw(r.mtd)}</td>
                <td title={`${won(r.lyMtd)}원`} className="dim">{krw(r.lyMtd)}</td>
                <td><Delta rate={rate(r.mtd, r.lyMtd)} small /></td>
                <td className="dim">{all.mtd > 0 ? `${((r.mtd / all.mtd) * 100).toFixed(1)}%` : ''}</td>
                <td title={`${won(r.ytd)}원`}>{krw(r.ytd)}</td>
                <td><Delta rate={rate(r.ytd, r.lyYtd)} small /></td>
              </tr>
            ))}
            <tr className="tot">
              <td className="nm">합계</td><td>{krw(all.mtd)}</td><td className="dim">{krw(all.lyMtd)}</td><td><Delta rate={rate(all.mtd, all.lyMtd)} small /></td>
              <td className="dim">100%</td><td>{krw(all.ytd)}</td><td><Delta rate={rate(all.ytd, all.lyYtd)} small /></td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  );
}

// ── 월별 추이: 올해 구분별 쌓은 막대 + 작년 선 ─────────

function MonthChart({ data, color }: { data: Report; color: (g: string) => string }) {
  const { h, ref, on, off } = useHover();
  const W = 640, H = 270, L = 56, R = 12, T = 14, B = 26;
  const nowMonth = Number(data.asOf.slice(5, 7));
  const max = niceMax(Math.max(1, ...data.thisYear.total, ...data.lastYear.total));
  const bw = (W - L - R) / 12;
  const y = (v: number) => T + (H - T - B) * (1 - v / max);
  const ly = data.lastYear.total.map((v, i) => `${L + bw * i + bw / 2},${y(v)}`).join(' ');
  return (
    <ChartCard title={`월별 매출 — ${data.thisYear.year}년(구분별) vs ${data.lastYear.year}년`}
      legend={<>{data.groups.map(g => <Key key={g} c={color(g)} label={g} />)}<Key c="#8E8E93" label={`${data.lastYear.year}년`} line /></>}>
      <div ref={ref} className="tr-svg-wrap" onMouseLeave={off}>
        <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label="월별 매출 추이">
          {[0, 0.25, 0.5, 0.75, 1].map(f => (
            <g key={f}><line x1={L} x2={W - R} y1={y(f * max)} y2={y(f * max)} className="grid" /><text x={L - 6} y={y(f * max) + 4} textAnchor="end" className="tick">{axisKrw(f * max)}</text></g>
          ))}
          {Array.from({ length: 12 }, (_, m) => {
            let base = 0;
            const x0 = L + bw * m + bw * 0.2, w = bw * 0.6;
            return (
              <g key={m} opacity={h && h.i !== m ? 0.55 : 1}>
                {data.groups.map(g => {
                  const v = data.thisYear.byGroup[g]?.[m] ?? 0;
                  if (v <= 0) return null;
                  const top = y(base + v), bottom = y(base);
                  base += v;
                  return <rect key={g} x={x0} y={top} width={w} height={Math.max(0, bottom - top - 1)} fill={color(g)} rx={1.5} />;
                })}
                {m + 1 === nowMonth && base > 0 && <text x={x0 + w / 2} y={y(base) - 6} textAnchor="middle" className="tr-lbl sm">누계</text>}
                <text x={L + bw * m + bw / 2} y={H - 8} textAnchor="middle" className={`tick${m + 1 === nowMonth ? ' now' : ''}`}>{m + 1}월</text>
              </g>
            );
          })}
          <polyline points={ly} fill="none" stroke="#8E8E93" strokeWidth={2} strokeLinejoin="round" />
          {data.lastYear.total.map((v, i) => <circle key={i} cx={L + bw * i + bw / 2} cy={y(v)} r={3} fill="#fff" stroke="#8E8E93" strokeWidth={1.5} />)}
          {Array.from({ length: 12 }, (_, m) => <rect key={m} x={L + bw * m} y={T} width={bw} height={H - T - B} fill="transparent" onMouseMove={on(m)} />)}
        </svg>
        {h && (
          <Tip h={h} w={ref.current?.clientWidth ?? W}>
            <b>{h.i + 1}월{h.i + 1 === nowMonth ? ` (${Number(data.asOf.slice(8))}일까지)` : ''}</b>
            {data.groups.map(g => <div key={g}><i style={{ background: color(g) }} />{g} <span>{won(data.thisYear.byGroup[g]?.[h.i] ?? 0)}</span></div>)}
            <div className="sum">{data.thisYear.year}년 합계 <span>{won(data.thisYear.total[h.i])}원</span></div>
            <div className="ly">{data.lastYear.year}년 <span>{won(data.lastYear.total[h.i])}원</span></div>
            {data.lastYear.total[h.i] > 0 && h.i + 1 < nowMonth && <div className="ly">작년 대비 <span>{pct((data.thisYear.total[h.i] - data.lastYear.total[h.i]) / data.lastYear.total[h.i])}</span></div>}
          </Tip>
        )}
      </div>
    </ChartCard>
  );
}

// ── 주간 추이(최근 12주) ───────────────────────────

function WeekChart({ weeks, groups, color }: { weeks: WeekPoint[]; groups: string[]; color: (g: string) => string }) {
  const { h, ref, on, off } = useHover();
  const W = 640, H = 270, L = 56, R = 12, T = 18, B = 40;
  const max = niceMax(Math.max(1, ...weeks.map(w => w.amount)));
  const bw = (W - L - R) / weeks.length;
  const y = (v: number) => T + (H - T - B) * (1 - v / max);
  const avg = weeks.filter(w => !w.partial).reduce((a, w) => a + w.amount / w.days, 0) / Math.max(1, weeks.filter(w => !w.partial).length);
  return (
    <ChartCard title="주간 매출 — 최근 12주" legend={<>{groups.map(g => <Key key={g} c={color(g)} label={g} />)}<span className="tr-key"><i className="hatch" />진행 중인 주</span></>}
      hint={`주는 월~일, 월말에 걸친 주는 말일에서 끊습니다(그래서 짧은 주가 있습니다). 마감된 주의 하루 평균 ${krw(avg)}원.`}>
      <div ref={ref} className="tr-svg-wrap" onMouseLeave={off}>
        <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label="최근 12주 주간 매출">
          <defs>
            <pattern id="tr-hatch" width="6" height="6" patternUnits="userSpaceOnUse" patternTransform="rotate(45)"><rect width="6" height="6" fill="#fff" fillOpacity="0.0" /><line x1="0" y1="0" x2="0" y2="6" stroke="#fff" strokeWidth="2.2" strokeOpacity="0.75" /></pattern>
          </defs>
          {[0, 0.25, 0.5, 0.75, 1].map(f => (
            <g key={f}><line x1={L} x2={W - R} y1={y(f * max)} y2={y(f * max)} className="grid" /><text x={L - 6} y={y(f * max) + 4} textAnchor="end" className="tick">{axisKrw(f * max)}</text></g>
          ))}
          {weeks.map((w, i) => {
            let base = 0;
            const x0 = L + bw * i + bw * 0.18, bwid = bw * 0.64;
            const newMonth = i > 0 && w.label.split(' ')[0] !== weeks[i - 1].label.split(' ')[0];
            return (
              <g key={w.start} opacity={h && h.i !== i ? 0.55 : 1}>
                {newMonth && <line x1={L + bw * i} x2={L + bw * i} y1={T - 6} y2={H - B + 4} className="tr-sep" />}
                {groups.map(g => {
                  const v = w.byGroup[g] ?? 0;
                  if (v <= 0) return null;
                  const top = y(base + v), bottom = y(base);
                  base += v;
                  return <g key={g}><rect x={x0} y={top} width={bwid} height={Math.max(0, bottom - top - 1)} fill={color(g)} rx={1.5} />
                    {w.partial && <rect x={x0} y={top} width={bwid} height={Math.max(0, bottom - top - 1)} fill="url(#tr-hatch)" />}</g>;
                })}
                <text x={L + bw * i + bw / 2} y={H - 22} textAnchor="middle" className="tick">{w.label.replace(' ', '')}</text>
                <text x={L + bw * i + bw / 2} y={H - 8} textAnchor="middle" className={`tick sm${w.days < 7 ? ' short' : ''}`}>{w.days < 7 ? `${w.days}일` : `${Number(w.start.slice(5, 7))}/${Number(w.start.slice(8))}~`}</text>
              </g>
            );
          })}
          {weeks.map((_, i) => <rect key={i} x={L + bw * i} y={T} width={bw} height={H - T - B} fill="transparent" onMouseMove={on(i)} />)}
        </svg>
        {h && (
          <Tip h={h} w={ref.current?.clientWidth ?? W}>
            <b>{weeks[h.i].label} · {weeks[h.i].start.slice(5)}~{weeks[h.i].end.slice(5)}{weeks[h.i].partial ? ' (진행 중)' : ''}</b>
            {groups.map(g => <div key={g}><i style={{ background: color(g) }} />{g} <span>{won(weeks[h.i].byGroup[g] ?? 0)}</span></div>)}
            <div className="sum">합계 <span>{won(weeks[h.i].amount)}원</span></div>
            <div className="ly">하루 평균 <span>{won(weeks[h.i].amount / weeks[h.i].days)}원</span></div>
          </Tip>
        )}
      </div>
    </ChartCard>
  );
}

// ── 폭포(증감 원인) ────────────────────────────────

function Waterfall({ title, steps, hint }: { title: string; steps: BridgeStep[]; hint?: string }) {
  const { h, ref, on, off } = useHover();
  const W = 640, H = 270, L = 56, R = 12, T = 22, B = 40;
  let run = 0;
  const bars = steps.map(s => {
    if (s.kind === 'delta') { const from = run; run += s.value; return { ...s, from, to: run }; }
    run = s.value; return { ...s, from: 0, to: s.value };
  });
  const max = niceMax(Math.max(1, ...bars.flatMap(b => [b.from, b.to])));
  const min = Math.min(0, ...bars.flatMap(b => [b.from, b.to]));
  const y = (v: number) => T + (H - T - B) * (1 - (v - min) / (max - min));
  const bw = (W - L - R) / bars.length;
  const fill = (b: typeof bars[number]) => (b.kind === 'base' ? '#B8BCC6' : b.kind === 'total' ? PALETTE[0] : b.value >= 0 ? UP : DOWN);
  return (
    <ChartCard title={title} hint={hint} legend={<><Key c={UP} label="증가" /><Key c={DOWN} label="감소" /></>}>
      <div ref={ref} className="tr-svg-wrap" onMouseLeave={off}>
        <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label={title}>
          {[0, 0.25, 0.5, 0.75, 1].map(f => { const v = min + f * (max - min); return (
            <g key={f}><line x1={L} x2={W - R} y1={y(v)} y2={y(v)} className="grid" /><text x={L - 6} y={y(v) + 4} textAnchor="end" className="tick">{axisKrw(v)}</text></g>
          ); })}
          {bars.map((b, i) => {
            const top = y(Math.max(b.from, b.to)), bot = y(Math.min(b.from, b.to));
            const x0 = L + bw * i + bw * 0.16, w = bw * 0.68;
            return (
              <g key={b.label} opacity={h && h.i !== i ? 0.6 : 1}>
                {i > 0 && <line x1={L + bw * (i - 1) + bw * 0.84} x2={x0} y1={y(bars[i - 1].to)} y2={y(bars[i - 1].to)} className="tr-conn" />}
                <rect x={x0} y={top} width={w} height={Math.max(1.5, bot - top)} fill={fill(b)} rx={2} />
                <text x={x0 + w / 2} y={top - 6} textAnchor="middle" className={`tr-lbl sm${b.kind === 'delta' ? (b.value >= 0 ? ' up' : ' down') : ''}`}>
                  {b.kind === 'delta' ? `${b.value >= 0 ? '+' : ''}${krw(b.value)}` : krw(b.value)}
                </text>
                <foreignObject x={L + bw * i} y={H - B + 4} width={bw} height={B - 4}><div className="tr-xl">{b.label}</div></foreignObject>
              </g>
            );
          })}
          {bars.map((_, i) => <rect key={i} x={L + bw * i} y={T} width={bw} height={H - T - B} fill="transparent" onMouseMove={on(i)} />)}
        </svg>
        {h && (
          <Tip h={h} w={ref.current?.clientWidth ?? W}>
            <b>{bars[h.i].label}</b>
            <div className="sum">{bars[h.i].kind === 'delta' ? '증감' : '금액'} <span>{bars[h.i].kind === 'delta' && bars[h.i].value >= 0 ? '+' : ''}{won(bars[h.i].value)}원</span></div>
          </Tip>
        )}
      </div>
    </ChartCard>
  );
}

// ── 증감 TOP ─────────────────────────────────────

function Movers({ sets }: { sets: MoverSet[] }) {
  const [dim, setDim] = useState(sets[0]?.dim ?? 'item');
  const set = sets.find(s => s.dim === dim) ?? sets[0];
  if (!set) return null;
  const max = Math.max(1, ...set.up.map(m => m.diff), ...set.down.map(m => -m.diff));
  const List = ({ title, items, up }: { title: string; items: Mover[]; up: boolean }) => (
    <div className="tr-mv-col">
      <div className={`tr-mv-h ${up ? 'up' : 'down'}`}>{title}</div>
      {items.length === 0 && <div className="tr-mv-empty">없음</div>}
      {items.map((m, i) => (
        <div key={m.key} className="tr-mv" title={`올해 ${won(m.cur)}원 (${m.curQty.toLocaleString()}개) · 작년 ${won(m.prev)}원 (${m.prevQty.toLocaleString()}개)`}>
          <span className="rk">{i + 1}</span>
          <div className="nm"><b>{m.name}</b>{m.sub && m.sub !== m.name && <small>{m.sub}</small>}</div>
          <div className="bar"><i style={{ width: `${(Math.abs(m.diff) / max) * 100}%`, background: up ? UP : DOWN }} /></div>
          <div className="v"><b className={up ? 'up' : 'down'}>{up ? '+' : ''}{krw(m.diff)}</b><small>{krw(m.prev)} → {krw(m.cur)}</small></div>
        </div>
      ))}
    </div>
  );
  return (
    <div className="mr-card tr-movers">
      <div className="mr-card-h">
        <b>작년 같은 기간 대비 — 무엇이 늘고 줄었나</b>
        <div className="mr-tabs tr-dims" role="tablist">
          {sets.map(s => <button key={s.dim} type="button" role="tab" aria-selected={s.dim === dim} className={s.dim === dim ? 'on' : ''} onClick={() => setDim(s.dim)}>{s.label}</button>)}
        </div>
      </div>
      <div className="tr-mv-grid">
        <List title={`증가 TOP ${set.up.length}`} items={set.up} up />
        <List title={`감소 TOP ${set.down.length}`} items={set.down} up={false} />
      </div>
    </div>
  );
}
