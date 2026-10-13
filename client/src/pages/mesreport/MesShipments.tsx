import { useEffect, useState } from 'react';
import { api } from '../../api/client';
import NotConnected from './NotConnected';
import { downloadXlsx, qty, useLegacyStatus, won } from './mesReport';
import './MesReport.css';

// MES 조회 › 출하실적 현황 — 기존 MES DB 에서 그 해 출하된 LOT 를 고객사 → 반출라인 → 월(AETS 출고일)로 묶는다.
// 수량 = LOT 수, 금액 = 품목 단가(MAT_CMF_4) 합. [그래프] 탭은 월별 수량·금액을 따로 그린다(축 하나씩).

interface ShipLine { line: string; qty: number[]; amount: number[]; qtyTotal: number; amountTotal: number }
interface ShipCustomer { code: string; name: string; lines: ShipLine[]; qty: number[]; amount: number[]; qtyTotal: number; amountTotal: number }
interface Ship { year: number; customers: ShipCustomer[]; qty: number[]; amount: number[]; qtyTotal: number; amountTotal: number; noDate: number }

const MONTHS = Array.from({ length: 12 }, (_, i) => `${i + 1}월`);

export default function MesShipments() {
  const status = useLegacyStatus();
  const thisYear = new Date().getFullYear();
  const [year, setYear] = useState(thisYear);
  const [customer, setCustomer] = useState('');
  const [type, setType] = useState('');
  const [tab, setTab] = useState<'data' | 'chart'>('data');
  const [data, setData] = useState<Ship | null>(null);
  const [err, setErr] = useState('');
  const [loading, setLoading] = useState(false);

  async function load() {
    setLoading(true); setErr('');
    try {
      const p = new URLSearchParams({ year: String(year) });
      if (customer) p.set('customer', customer);
      if (type.trim()) p.set('type', type.trim());
      setData(await api.get<Ship>(`/api/mes-report/shipments?${p}`));
    } catch (e) { setErr(e instanceof Error ? e.message : '불러오지 못했습니다.'); }
    finally { setLoading(false); }
  }
  useEffect(() => { if (status?.configured) void load(); }, [status?.configured, year, customer]);   // eslint-disable-line react-hooks/exhaustive-deps

  if (status === undefined) return <div className="page-loading">불러오는 중…</div>;
  if (!status?.configured) return <NotConnected title="출하실적 현황" />;

  function exportXlsx() {
    if (!data) return;
    const rows: (string | number | null)[][] = [];
    for (const c of data.customers) {
      for (const l of c.lines) {
        rows.push([c.name, l.line, '수량', ...l.qty, l.qtyTotal]);
        rows.push([c.name, l.line, '금액', ...l.amount, l.amountTotal]);
      }
      rows.push([c.name, '소계', '수량', ...c.qty, c.qtyTotal]);
      rows.push([c.name, '소계', '금액', ...c.amount, c.amountTotal]);
    }
    rows.push(['합계', '', '수량', ...data.qty, data.qtyTotal]);
    rows.push(['합계', '', '금액', ...data.amount, data.amountTotal]);
    void downloadXlsx(`출하실적_${data.year}.xlsx`, `${data.year}`, ['고객사', '라인', '구분', ...MONTHS, '합계'], rows, [12, 14, 6, ...MONTHS.map(() => 13), 15]);
  }

  return (
    <div className="mr-page">
      <header className="pg-header">
        <div><h2>출하실적 현황</h2><p>기존 MES 기준 · 고객사 → 반출라인 → 월(AETS 출고일) · 수량은 LOT 수, 금액은 품목 단가 합 · 읽기 전용</p></div>
      </header>
      <div className="pg-body">
        <div className="mr-filters">
          <select className="input" value={year} onChange={e => setYear(Number(e.target.value))} aria-label="조회 연도">
            {Array.from({ length: 6 }, (_, i) => thisYear - i).map(y => <option key={y} value={y}>{y}년</option>)}
          </select>
          <select className="input" value={customer} onChange={e => setCustomer(e.target.value)} aria-label="고객사">
            <option value="">고객사 전체</option>
            {Object.entries(status.customerNames).map(([c, n]) => <option key={c} value={c}>{n}</option>)}
          </select>
          <input className="input" placeholder="제품 TYPE(예: Dummy Wafer)" value={type} onChange={e => setType(e.target.value)}
            onKeyDown={e => { if (e.key === 'Enter') void load(); }} />
          <button type="button" className="btn btn-primary" onClick={() => void load()} disabled={loading}>{loading ? '조회 중…' : '조회'}</button>
          {data && <button type="button" className="btn btn-ghost" onClick={exportXlsx}>엑셀</button>}
          {data && data.noDate > 0 && <span className="mr-dim">출고일을 읽지 못한 LOT {data.noDate}건 제외</span>}
        </div>
        {err && <div className="mr-err">{err}</div>}

        <div className="mr-tabs" role="tablist">
          <button type="button" role="tab" aria-selected={tab === 'data'} className={tab === 'data' ? 'on' : ''} onClick={() => setTab('data')}>데이터</button>
          <button type="button" role="tab" aria-selected={tab === 'chart'} className={tab === 'chart' ? 'on' : ''} onClick={() => setTab('chart')}>그래프</button>
        </div>

        {data && tab === 'data' && (
          <div className="mr-card">
            <div className="mr-scroll">
              <table className="mr-table mr-ship">
                <thead>
                  <tr><th>고객사</th><th>라인</th><th>구분</th>{MONTHS.map(m => <th key={m}>{m}</th>)}<th className="tot">합계</th></tr>
                </thead>
                <tbody>
                  {data.customers.map(c => (
                    <Group key={c.code} c={c} />
                  ))}
                  <tr className="mr-grand"><td colSpan={2} rowSpan={2}>합계</td><td className="k">수량</td>{data.qty.map((v, i) => <td key={i} className="n">{qty(v)}</td>)}<td className="n tot">{qty(data.qtyTotal)}</td></tr>
                  <tr className="mr-grand amt"><td className="k">금액</td>{data.amount.map((v, i) => <td key={i} className="n">{v ? won(v) : ''}</td>)}<td className="n tot">{won(data.amountTotal)}</td></tr>
                  {data.customers.length === 0 && <tr><td colSpan={16} className="mr-empty">그 해 출하 기록이 없습니다.</td></tr>}
                </tbody>
              </table>
            </div>
          </div>
        )}

        {data && tab === 'chart' && (
          <div className="mr-charts">
            <Bars title={`${data.year}년 월별 출하 수량 (LOT)`} values={data.qty} fmt={qty} />
            <Bars title={`${data.year}년 월별 출하 금액 (원)`} values={data.amount} fmt={won} />
          </div>
        )}
      </div>
    </div>
  );
}

function Group({ c }: { c: ShipCustomer }) {
  const rowsPerLine = 2;
  const span = c.lines.length * rowsPerLine + 2;
  return (
    <>
      {c.lines.map((l, li) => (
        <LineRows key={l.line} first={li === 0} span={span} name={c.name} label={l.line} q={l.qty} a={l.amount} qt={l.qtyTotal} at={l.amountTotal} />
      ))}
      <LineRows first={c.lines.length === 0} span={span} name={c.name} label="소계" q={c.qty} a={c.amount} qt={c.qtyTotal} at={c.amountTotal} sub />
    </>
  );
}

function LineRows({ first, span, name, label, q, a, qt, at, sub }: {
  first: boolean; span: number; name: string; label: string; q: number[]; a: number[]; qt: number; at: number; sub?: boolean;
}) {
  return (
    <>
      <tr className={sub ? 'mr-sub' : ''}>
        {first && <td rowSpan={span} className="cust">{name}</td>}
        <td rowSpan={2} className="ln">{label}</td>
        <td className="k">수량</td>
        {q.map((v, i) => <td key={i} className="n">{qty(v)}</td>)}
        <td className="n tot">{qty(qt)}</td>
      </tr>
      <tr className={`amt${sub ? ' mr-sub' : ''}`}>
        <td className="k">금액</td>
        {a.map((v, i) => <td key={i} className="n">{v ? won(v) : ''}</td>)}
        <td className="n tot">{won(at)}</td>
      </tr>
    </>
  );
}

/** 월별 막대 하나(축 하나) — 막대에 마우스를 올리면 값 */
function Bars({ title, values, fmt }: { title: string; values: number[]; fmt: (n: number) => string }) {
  const W = 720, H = 240, L = 64, B = 28, T = 16;
  const max = Math.max(1, ...values);
  const nice = (() => { const p = 10 ** Math.floor(Math.log10(max)); return Math.ceil(max / p) * p; })();
  const ticks = [0, 0.25, 0.5, 0.75, 1].map(f => f * nice);
  const bw = (W - L - 12) / 12;
  const y = (v: number) => T + (H - T - B) * (1 - v / nice);
  return (
    <div className="mr-card mr-chart">
      <div className="mr-card-h"><b>{title}</b></div>
      <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label={title}>
        {ticks.map(t => (
          <g key={t}>
            <line x1={L} x2={W - 8} y1={y(t)} y2={y(t)} className="grid" />
            <text x={L - 8} y={y(t) + 4} textAnchor="end" className="tick">{fmt(t) || '0'}</text>
          </g>
        ))}
        {values.map((v, i) => {
          const x = L + i * bw + bw * 0.22, w = bw * 0.56, top = y(v);
          return (
            <g key={i} className="bar-g">
              <rect x={L + i * bw} y={T} width={bw} height={H - T - B} className="hit"><title>{`${i + 1}월: ${fmt(v) || 0}`}</title></rect>
              {v > 0 && <path d={`M${x},${y(0)} V${top + 4} q0,-4 4,-4 h${w - 8} q4,0 4,4 V${y(0)} Z`} className="bar"><title>{`${i + 1}월: ${fmt(v)}`}</title></path>}
              <text x={L + i * bw + bw / 2} y={H - 8} textAnchor="middle" className="tick">{i + 1}월</text>
            </g>
          );
        })}
      </svg>
    </div>
  );
}
