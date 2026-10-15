import { useEffect, useMemo, useRef, useState } from 'react';
import { api } from '../../api/client';
import NotConnected from './NotConnected';
import { useLegacyStatus, won } from './mesReport';
import './MesReport.css';
import './MesTrend.css';

// MES 조회 › 매출 트렌드 — 출하 기준(출하실적 기본 규칙: 무상세정·삼성 세정 SKIP 빼고 케미칼 넣음, 기타세정은 SKIP 이어도 넣음).
// 위에서 아래로 결론(카드) → 흐름(이번 달 페이스·월·주 추이) → 원인(구분별·물량·단가 증감) → 무엇이(증감 TOP).
// 주 = 월~일, 월말에 걸친 주는 말일에서 끊고 1일부터 새 달 1주. 비교는 늘 "같은 날까지"(이번 달 14일이면 작년 10월 1~14일).

interface Compare { value: number; base: number | null; baseLabel: string; rate: number | null }
interface CardLine { label: string; value: number }
interface Card { key: string; title: string; period: string; value: number; compares: Compare[]; note: string | null; lines: CardLine[] }
interface GroupRow { name: string; parent: string; subtotal: boolean; cur: number; ly: number; ytd: number; lyYtd: number }
interface MonthSeries { year: number; total: number[]; byGroup: Record<string, number[]> }
interface WeekPoint { label: string; start: string; end: string; days: number; partial: boolean; amount: number; byGroup: Record<string, number> }
interface DailySeries { label: string; start: string; days: number; amount: number[]; upTo: number }
interface DayPoint { date: string; dow: number; counted: boolean; amount: number; byGroup: Record<string, number>; ly: number }
interface BridgeStep { label: string; value: number; kind: 'base' | 'delta' | 'total' }
interface Mover { key: string; name: string; sub: string; cur: number; prev: number; curQty: number; prevQty: number; diff: number }
interface MoverSet { dim: string; label: string; up: Mover[]; down: Mover[]; count: number }
interface OptionItem { key: string; name: string; amount: number }
interface Report {
  kind: 'month' | 'quarter' | 'range'; from: string; end: string; to: string; partial: boolean;
  periodLabel: string; curLabel: string; lyLabel: string; groups: string[]; cards: Card[]; groupRows: GroupRow[];
  thisYear: MonthSeries; lastYear: MonthSeries; weeks: WeekPoint[]; daily: DailySeries[]; days: DayPoint[];
  groupBridge: BridgeStep[]; priceVolume: BridgeStep[]; movers: MoverSet[];
  /** 업체·라인 고르기 목록(필터 전 자료, 금액 큰 순) · 필터가 걸렸는지 */
  options?: { customers: OptionItem[]; lines: OptionItem[] } | null; filtered?: boolean;
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
const md = (iso: string) => `${Number(iso.slice(5, 7))}/${Number(iso.slice(8, 10))}`;
const addDays = (iso: string, n: number) => { const d = new Date(`${iso}T00:00:00Z`); d.setUTCDate(d.getUTCDate() + n); return d.toISOString().slice(0, 10); };

type Kind = 'month' | 'quarter' | 'range';
interface Sel { kind: Kind; month: string; year: number; q: number; from: string; to: string }
const initSel = (): Sel => {
  const t = today(), y = Number(t.slice(0, 4)), m = Number(t.slice(5, 7));
  return { kind: 'month', month: t.slice(0, 7), year: y, q: Math.floor((m - 1) / 3) + 1, from: `${t.slice(0, 7)}-01`, to: t };
};
const query = (s: Sel) => s.kind === 'quarter' ? `kind=quarter&year=${s.year}&q=${s.q}`
  : s.kind === 'range' ? `kind=range&from=${s.from}&to=${s.to}` : `kind=month&month=${s.month}`;

function Delta({ rate, small }: { rate: number | null; small?: boolean }) {
  if (rate == null) return <span className={`tr-delta none${small ? ' sm' : ''}`}>비교 없음</span>;
  const up = rate >= 0;
  return <span className={`tr-delta ${up ? 'up' : 'down'}${small ? ' sm' : ''}`}>{up ? '▲' : '▼'} {Math.abs(rate * 100).toFixed(1)}%</span>;
}

export default function MesTrend() {
  const status = useLegacyStatus();
  const [sel, setSel] = useState<Sel>(initSel);
  const [data, setData] = useState<Report | null>(null);
  const [err, setErr] = useState('');
  const [loading, setLoading] = useState(false);
  const set = (p: Partial<Sel>) => setSel(s => ({ ...s, ...p }));
  // 업체·라인 필터 — 여러 개 고를 수 있다. 비면 전체.
  const [custSel, setCustSel] = useState<string[]>([]);
  const [lineSel, setLineSel] = useState<string[]>([]);
  // 고르기 목록의 이름(필터를 바꿔도 이름이 사라지지 않게 기억)
  const [names, setNames] = useState<Record<string, string>>({});

  async function load(s = sel, cust = custSel, lines = lineSel) {
    if (s.kind === 'range' && (!s.from || !s.to || s.from > s.to)) { setErr('시작일과 끝일을 확인해 주세요.'); return; }
    setLoading(true); setErr('');
    const f = [...cust.map(c => `customer=${encodeURIComponent(c)}`), ...lines.map(l => `line=${encodeURIComponent(l)}`)].join('&');
    try {
      const r = await api.get<Report>(`/api/mes-report/trend?${query(s)}${f ? `&${f}` : ''}`);
      setData(r);
      if (r.options) setNames(n => ({ ...n, ...Object.fromEntries(r.options!.customers.map(o => [o.key, o.name])) }));
    }
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
        <div><h2>매출 트렌드</h2><p>출하 기준 · 무상세정·삼성 세정 SKIP 제외(기타세정은 SKIP 이어도 포함), 케미칼(SD-1) 포함 · 금액 = 입고일 단가 × 수량 · 비교는 같은 날짜까지 · 주는 월~일(월말에서 끊음) · 진행 중인 기간은 오늘까지 세고 기간 말을 예상</p></div>
      </header>
      <div className="pg-body">
        <div className="mr-filters">
          <MultiPick label="업체" options={data?.options?.customers ?? []} value={custSel} names={names}
            onApply={v => { setCustSel(v); setLineSel([]); void load(sel, v, []); }} />
          <MultiPick label="라인" options={data?.options?.lines ?? []} value={lineSel}
            onApply={v => { setLineSel(v); void load(sel, custSel, v); }} />
          {(custSel.length > 0 || lineSel.length > 0) && (
            <button type="button" className="btn btn-ghost" onClick={() => { setCustSel([]); setLineSel([]); void load(sel, [], []); }}>필터 해제</button>
          )}
          <span className="tr-vsep" />
          <div className="tr-seg" role="radiogroup" aria-label="기간 종류">
            {([['month', '월'], ['quarter', '분기'], ['range', '기간 직접']] as [Kind, string][]).map(([k, n]) => (
              <button key={k} type="button" role="radio" aria-checked={sel.kind === k} className={sel.kind === k ? 'on' : ''} onClick={() => set({ kind: k })}>{n}</button>
            ))}
          </div>
          {sel.kind === 'month' && <input type="month" className="input" aria-label="월" value={sel.month} max={today().slice(0, 7)} onChange={e => set({ month: e.target.value })} />}
          {sel.kind === 'quarter' && (
            <>
              <select className="input" aria-label="연도" value={sel.year} onChange={e => set({ year: Number(e.target.value) })}>
                {Array.from({ length: 6 }, (_, i) => Number(today().slice(0, 4)) - i).map(y => <option key={y} value={y}>{y}년</option>)}
              </select>
              <select className="input" aria-label="분기" value={sel.q} onChange={e => set({ q: Number(e.target.value) })}>
                {[1, 2, 3, 4].map(n => <option key={n} value={n}>{n}분기</option>)}
              </select>
            </>
          )}
          {sel.kind === 'range' && (
            <span className="tr-range">
              <input type="date" className="input" aria-label="시작일" value={sel.from} max={today()} onChange={e => set({ from: e.target.value })} />
              <span>~</span>
              <input type="date" className="input" aria-label="끝일" value={sel.to} min={sel.from} onChange={e => set({ to: e.target.value })} />
            </span>
          )}
          <button type="button" className="btn btn-primary" onClick={() => void load()} disabled={loading}>{loading ? '조회 중…' : '조회'}</button>
          <button type="button" className="btn btn-ghost" onClick={() => { const s = initSel(); setSel(s); void load(s); }}>이번 달</button>
          {data && <span className="mr-dim">조회 {data.curLabel} · 비교 {data.lyLabel}{data.partial ? ' · 진행 중' : ''}</span>}
        </div>
        {(custSel.length > 0 || lineSel.length > 0) && (
          <div className="tr-active">
            필터 적용 중 —
            {custSel.length > 0 && <span>업체 <b>{custSel.map(c => names[c] ?? c).join(', ')}</b></span>}
            {lineSel.length > 0 && <span>라인 <b>{lineSel.join(', ')}</b></span>}
            <small>아래 카드·그래프·표는 모두 이 조건의 매출입니다</small>
          </div>
        )}
        {err && <div className="mr-err">{err}</div>}
        {!data && loading && <div className="page-loading">올해·작년 출하를 모으는 중…</div>}

        {data && (
          <>
            <div className="tr-cards">
              {data.cards.map(c => (
                <div key={c.key} className={`tr-card${c.key === 'cur' ? ' main' : ''}${c.key === 'forecast' && data.partial ? ' fc' : ''}`}>
                  <div className="tr-card-t">{c.title}<span>{c.period}</span></div>
                  <div className="tr-card-v" title={`${won(c.value)}원`}>{krw(c.value)}<small>원</small></div>
                  {c.compares.map((x, i) => (
                    <div key={i} className="tr-card-c">
                      <Delta rate={x.rate} small={i > 0} />
                      <span className="tr-card-b" title={`${x.baseLabel}${x.base == null ? '' : ` ${won(x.base)}원`}`}>{x.baseLabel} {x.base == null ? '' : krw(x.base)}</span>
                    </div>
                  ))}
                  {c.lines.map(l => <div key={l.label} className="tr-card-l" title={`${won(l.value)}원`}><span>{l.label}</span><b>{krw(l.value)}</b></div>)}
                  {c.note && <div className="tr-card-n">{c.note}</div>}
                </div>
              ))}
            </div>

            <div className="tr-row two">
              <Pace daily={data.daily} kind={data.kind} />
              <GroupTable rows={data.groupRows} color={color} label={data.curLabel} />
            </div>

            <DayChart days={data.days} groups={data.groups} color={color} label={data.periodLabel} />

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

// ── 여러 개 고르기(검색 + 체크) ───────────────────────

/** 업체·라인 고르기 — 단추를 누르면 검색창과 체크 목록. [적용]을 눌러야 다시 조회한다. */
function MultiPick({ label, options, value, names, onApply }: {
  label: string; options: OptionItem[]; value: string[]; names?: Record<string, string>; onApply: (v: string[]) => void;
}) {
  const [open, setOpen] = useState(false);
  const [q, setQ] = useState('');
  const [draft, setDraft] = useState<string[]>(value);
  const box = useRef<HTMLDivElement>(null);
  useEffect(() => { if (open) { setDraft(value); setQ(''); } }, [open]);   // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => {
    if (!open) return;
    const close = (e: MouseEvent) => { if (box.current && !box.current.contains(e.target as Node)) setOpen(false); };
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, [open]);
  const nameOf = (k: string) => options.find(o => o.key === k)?.name ?? names?.[k] ?? k;
  // 이미 고른 것은 목록에 없어도(다른 기간 등) 맨 위에
  const list = useMemo(() => {
    const extra = value.filter(v => !options.some(o => o.key === v)).map(v => ({ key: v, name: nameOf(v), amount: 0 }));
    const t = q.trim().toLowerCase();
    return [...extra, ...options].filter(o => !t || o.name.toLowerCase().includes(t) || o.key.toLowerCase().includes(t));
  }, [options, value, q]);   // eslint-disable-line react-hooks/exhaustive-deps
  const toggle = (k: string) => setDraft(d => (d.includes(k) ? d.filter(x => x !== k) : [...d, k]));
  const text = value.length === 0 ? '전체' : value.length === 1 ? nameOf(value[0]) : `${nameOf(value[0])} 외 ${value.length - 1}`;
  return (
    <div className="tr-pick" ref={box}>
      <button type="button" className={`tr-pick-btn${value.length ? ' on' : ''}`} onClick={() => setOpen(o => !o)} aria-expanded={open}>
        <span className="k">{label}</span><span className="v">{text}</span><span className="c">▾</span>
      </button>
      {open && (
        <div className="tr-pick-pop" role="dialog" aria-label={`${label} 고르기`}>
          <input className="input" autoFocus placeholder={`${label} 검색`} value={q} onChange={e => setQ(e.target.value)} />
          <div className="tr-pick-tools">
            <button type="button" className="btn btn-ghost" onClick={() => setDraft(d => [...new Set([...d, ...list.map(o => o.key)])])}>보이는 것 모두</button>
            <button type="button" className="btn btn-ghost" onClick={() => setDraft([])}>모두 해제</button>
            <span className="mr-dim">{draft.length ? `${draft.length}개 선택` : '전체'}</span>
          </div>
          <div className="tr-pick-list">
            {list.length === 0 && <div className="mr-dim tr-pick-empty">없음</div>}
            {list.map(o => (
              <label key={o.key} className={draft.includes(o.key) ? 'on' : ''}>
                <input type="checkbox" checked={draft.includes(o.key)} onChange={() => toggle(o.key)} />
                <span className="n">{o.name}</span>
                {o.amount > 0 && <span className="a">{krw(o.amount)}</span>}
              </label>
            ))}
          </div>
          <div className="tr-pick-foot">
            <button type="button" className="btn btn-ghost" onClick={() => setOpen(false)}>취소</button>
            <button type="button" className="btn btn-primary" onClick={() => { onApply(draft); setOpen(false); }}>적용</button>
          </div>
        </div>
      )}
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

function Pace({ daily, kind }: { daily: DailySeries[]; kind: Kind }) {
  const { h, ref, on, off } = useHover();
  const W = 640, H = 260, L = 56, R = 14, T = 14, B = 26;
  const cum = daily.map(s => { let t = 0; return s.amount.map((v, i) => (i < s.upTo ? (t += v) : null)); });
  const max = niceMax(Math.max(1, ...cum.flat().map(v => v ?? 0)));
  const days = Math.max(...daily.map(s => s.days));
  const x = (d: number) => L + ((W - L - R) * d) / (days - 1 || 1);
  // 달이면 '며칠', 분기·직접 기간이면 이번 기간의 날짜
  const dayLabel = (i: number) => (kind === 'month' ? `${i + 1}일` : md(addDays(daily[0].start, i)));
  const tickDays = kind === 'month' ? [1, 5, 10, 15, 20, 25, days] : Array.from(new Set([0, 0.2, 0.4, 0.6, 0.8, 1].map(f => Math.round(f * (days - 1)) + 1)));
  const y = (v: number) => T + (H - T - B) * (1 - v / max);
  const styles = [{ c: PALETTE[0], w: 2.5, dash: '' }, { c: '#8E8E93', w: 2, dash: '' }, { c: '#B8BCC6', w: 2, dash: '5 4' }];
  const cur = daily[0];
  return (
    <ChartCard title={`누적 페이스 — ${cur.label}${cur.upTo < cur.days ? ` (${cur.upTo}/${cur.days}일)` : ''}`}
      legend={daily.map((s, i) => <Key key={s.label} c={styles[i].c} label={s.label} line dash={!!styles[i].dash} />)}>
      <div ref={ref} className="tr-svg-wrap" onMouseLeave={off}>
        <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label="이번 기간 누적 매출과 작년 같은 기간·직전 기간 비교">
          {[0, 0.25, 0.5, 0.75, 1].map(f => (
            <g key={f}><line x1={L} x2={W - R} y1={y(f * max)} y2={y(f * max)} className="grid" /><text x={L - 6} y={y(f * max) + 4} textAnchor="end" className="tick">{axisKrw(f * max)}</text></g>
          ))}
          {tickDays.map(d => <text key={d} x={x(d - 1)} y={H - 8} textAnchor="middle" className="tick">{dayLabel(d - 1)}</text>)}
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
            <b>{kind === 'month' ? `${h.i + 1}일` : `${h.i + 1}일째 (${dayLabel(h.i)})`}까지 누적</b>
            {daily.map((s, si) => <div key={s.label}><i style={{ background: styles[si].c }} />{s.label} <span>{cum[si][h.i] == null ? '—' : `${won(cum[si][h.i]!)}원`}</span></div>)}
          </Tip>
        )}
      </div>
    </ChartCard>
  );
}

// ── 구분별 표 ─────────────────────────────────────

function GroupTable({ rows, color, label }: { rows: GroupRow[]; color: (g: string) => string; label: string }) {
  const all = useMemo(() => {
    const leaf = rows.filter(r => !r.subtotal);
    const s = (k: keyof GroupRow) => leaf.reduce((a, r) => a + (r[k] as number), 0);
    return { cur: s('cur'), ly: s('ly'), ytd: s('ytd'), lyYtd: s('lyYtd') };
  }, [rows]);
  const rate = (a: number, b: number) => (b > 0 ? (a - b) / b : null);
  return (
    <div className="mr-card tr-chart">
      <div className="mr-card-h"><b>구분별 — {label} · 올해 누계</b></div>
      <div className="mr-scroll">
        <table className="mr-table tr-gt">
          <thead><tr><th>구분</th><th>조회 기간</th><th>작년 같은 기간</th><th>증감</th><th>비중</th><th>올해 누계</th><th>작년 대비</th></tr></thead>
          <tbody>
            {rows.map(r => (
              <tr key={r.name} className={r.subtotal ? 'sub' : ''}>
                <td className="nm">{!r.subtotal && <i style={{ background: color(r.name) }} />}{r.name}</td>
                <td title={`${won(r.cur)}원`}>{krw(r.cur)}</td>
                <td title={`${won(r.ly)}원`} className="dim">{krw(r.ly)}</td>
                <td><Delta rate={rate(r.cur, r.ly)} small /></td>
                <td className="dim">{all.cur > 0 ? `${((r.cur / all.cur) * 100).toFixed(1)}%` : ''}</td>
                <td title={`${won(r.ytd)}원`}>{krw(r.ytd)}</td>
                <td><Delta rate={rate(r.ytd, r.lyYtd)} small /></td>
              </tr>
            ))}
            <tr className="tot">
              <td className="nm">합계</td><td>{krw(all.cur)}</td><td className="dim">{krw(all.ly)}</td><td><Delta rate={rate(all.cur, all.ly)} small /></td>
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
  const nowMonth = Number(data.to.slice(5, 7));
  const toYear = Number(data.to.slice(0, 4));
  // 조회 기간에 든 달(끝 해 기준) — 눈금을 굵게
  const inPeriod = (m: number) => { const a = `${toYear}-${String(m).padStart(2, '0')}`; return a >= data.from.slice(0, 7) && a <= data.to.slice(0, 7); };
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
                {m + 1 === nowMonth && data.partial && base > 0 && <text x={x0 + w / 2} y={y(base) - 6} textAnchor="middle" className="tr-lbl sm">누계</text>}
                <text x={L + bw * m + bw / 2} y={H - 8} textAnchor="middle" className={`tick${inPeriod(m + 1) ? ' now' : ''}`}>{m + 1}월</text>
              </g>
            );
          })}
          <polyline points={ly} fill="none" stroke="#8E8E93" strokeWidth={2} strokeLinejoin="round" />
          {data.lastYear.total.map((v, i) => <circle key={i} cx={L + bw * i + bw / 2} cy={y(v)} r={3} fill="#fff" stroke="#8E8E93" strokeWidth={1.5} />)}
          {Array.from({ length: 12 }, (_, m) => <rect key={m} x={L + bw * m} y={T} width={bw} height={H - T - B} fill="transparent" onMouseMove={on(m)} />)}
        </svg>
        {h && (
          <Tip h={h} w={ref.current?.clientWidth ?? W}>
            <b>{h.i + 1}월{h.i + 1 === nowMonth ? ` (${Number(data.to.slice(8))}일까지)` : ''}</b>
            {data.groups.map(g => <div key={g}><i style={{ background: color(g) }} />{g} <span>{won(data.thisYear.byGroup[g]?.[h.i] ?? 0)}</span></div>)}
            <div className="sum">{data.thisYear.year}년 합계 <span>{won(data.thisYear.total[h.i])}원</span></div>
            <div className="ly">{data.lastYear.year}년 <span>{won(data.lastYear.total[h.i])}원</span></div>
            {data.lastYear.total[h.i] > 0 && (h.i + 1 < nowMonth || (h.i + 1 === nowMonth && !data.partial)) && <div className="ly">작년 대비 <span>{pct((data.thisYear.total[h.i] - data.lastYear.total[h.i]) / data.lastYear.total[h.i])}</span></div>}
          </Tip>
        )}
      </div>
    </ChartCard>
  );
}

// ── 일별 매출(조회 기간 날마다) ─────────────────────

const DOW = '일월화수목금토';

function DayChart({ days, groups, color, label }: { days: DayPoint[]; groups: string[]; color: (g: string) => string; label: string }) {
  const { h, ref, on, off } = useHover();
  const W = 1280, H = 250, L = 56, R = 12, T = 16, B = 38;
  const counted = days.filter(d => d.counted);
  const max = niceMax(Math.max(1, ...days.map(d => Math.max(d.amount, d.ly))));
  const bw = (W - L - R) / Math.max(1, days.length);
  const y = (v: number) => T + (H - T - B) * (1 - v / max);
  const avg = counted.length ? counted.reduce((a, d) => a + d.amount, 0) / counted.length : 0;
  // 날짜 눈금 — 많으면 건너뛴다(월 1일·월요일 위주)
  const step = days.length <= 35 ? 1 : days.length <= 70 ? 2 : 7;
  const lyPts = days.map((d, i) => `${L + bw * i + bw / 2},${y(d.ly)}`).join(' ');
  const last = counted.length ? counted[counted.length - 1] : null;
  return (
    <ChartCard title={`일별 매출 — ${label}`}
      legend={<>{groups.map(g => <Key key={g} c={color(g)} label={g} />)}<Key c="#8E8E93" label="작년 같은 날짜" line /><Key c="#E0901A" label={`일평균 ${krw(avg)}`} line dash /></>}
      hint="토·일은 눈금을 회색으로, 아직 오지 않은 날은 비워 둡니다. 막대에 마우스를 올리면 구분별 금액과 작년 같은 날짜가 나옵니다.">
      <div ref={ref} className="tr-svg-wrap" onMouseLeave={off}>
        <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label={`일별 매출 ${label}`}>
          {[0, 0.25, 0.5, 0.75, 1].map(f => (
            <g key={f}><line x1={L} x2={W - R} y1={y(f * max)} y2={y(f * max)} className="grid" /><text x={L - 6} y={y(f * max) + 4} textAnchor="end" className="tick">{axisKrw(f * max)}</text></g>
          ))}
          {days.map((d, i) => {
            let base = 0;
            const x0 = L + bw * i + bw * 0.15, w = Math.max(1, bw * 0.7);
            const weekend = d.dow === 0 || d.dow === 6;
            const first = d.date.endsWith('-01');
            const showTick = i === 0 || first || (step === 1 ? true : step === 7 ? d.dow === 1 : i % step === 0);
            return (
              <g key={d.date} opacity={h && h.i !== i ? 0.55 : 1}>
                {first && i > 0 && <line x1={L + bw * i} x2={L + bw * i} y1={T - 6} y2={H - B + 4} className="tr-sep" />}
                {d.counted && groups.map(g => {
                  const v = d.byGroup[g] ?? 0;
                  if (v <= 0) return null;
                  const top = y(base + v), bottom = y(base);
                  base += v;
                  return <rect key={g} x={x0} y={top} width={w} height={Math.max(0, bottom - top - (bw > 8 ? 1 : 0))} fill={color(g)} rx={bw > 10 ? 1.5 : 0} />;
                })}
                {showTick && <text x={L + bw * i + bw / 2} y={H - 22} textAnchor="middle" className={`tick${weekend ? ' we' : ''}`}>{first || i === 0 ? md(d.date) : Number(d.date.slice(8))}</text>}
                {showTick && step === 1 && <text x={L + bw * i + bw / 2} y={H - 8} textAnchor="middle" className={`tick sm${weekend ? ' we' : ''}`}>{DOW[d.dow]}</text>}
              </g>
            );
          })}
          <polyline points={lyPts} fill="none" stroke="#8E8E93" strokeWidth={1.5} strokeLinejoin="round" opacity={0.85} />
          {avg > 0 && <line x1={L} x2={W - R} y1={y(avg)} y2={y(avg)} stroke="#E0901A" strokeWidth={1.5} strokeDasharray="6 4" />}
          {last && last.amount > 0 && (() => { const i = days.indexOf(last); return <text x={L + bw * i + bw / 2} y={y(last.amount) - 6} textAnchor={i > days.length - 4 ? 'end' : 'middle'} className="tr-lbl sm">{krw(last.amount)}</text>; })()}
          {days.map((_, i) => <rect key={i} x={L + bw * i} y={T} width={bw} height={H - T - B} fill="transparent" onMouseMove={on(i)} />)}
        </svg>
        {h && (() => { const d = days[h.i]; return (
          <Tip h={h} w={ref.current?.clientWidth ?? W}>
            <b>{md(d.date)} ({DOW[d.dow]}){d.counted ? '' : ' · 아직 안 옴'}</b>
            {d.counted && groups.map(g => <div key={g}><i style={{ background: color(g) }} />{g} <span>{won(d.byGroup[g] ?? 0)}</span></div>)}
            {d.counted && <div className="sum">합계 <span>{won(d.amount)}원</span></div>}
            <div className="ly">작년 같은 날짜 <span>{won(d.ly)}원</span></div>
            {d.counted && avg > 0 && <div className="ly">일평균 대비 <span>{pct((d.amount - avg) / avg)}</span></div>}
          </Tip>
        ); })()}
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
