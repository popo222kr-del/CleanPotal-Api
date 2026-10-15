import { Fragment, useEffect, useMemo, useState } from 'react';
import { api } from '../../api/client';
import NotConnected from './NotConnected';
import { downloadXlsx, qty, useLegacyStatus, won } from './mesReport';
import './MesReport.css';

// MES 조회 › 출하실적 현황 — 기존 MES '출하실적 현황(차트)'처럼 구분(MEMORY·FOUNDRY / 그 밖 고객사) → 라인 → 월(실제 출하 시각).
// 줄마다 수량(위)·금액(아래), 옆에 지금 창고(동탄·천안) 재고. 금액 = 입고일에 적용 중인 단가(단가표 CMATPRICE, 없으면 품목 마스터) × 수량 — 기존 MES 와 같음. 숫자를 누르면 그 품목 목록 — 위에 품목별 수량, 아래 LOT 목록.
// 수량 = LOT 안의 개수(QTY_1 — 삼성 LOT 1, 노즐·분석 의뢰 등은 여러 개), 금액 = 단가 × 수량(기존 MES 와 같음). 세트 0.25 표시는 따르지 않음. [그래프] 탭은 월별 수량·금액을 따로 그린다.

interface ShipLine { line: string; qty: number[]; amount: number[]; qtyTotal: number; amountTotal: number; stock: number[] }
interface ShipGroup { key: string; name: string; lines: ShipLine[]; qty: number[]; amount: number[]; qtyTotal: number; amountTotal: number; stock: number[] }
interface Ship { year: number; groups: ShipGroup[]; qty: number[]; amount: number[]; qtyTotal: number; amountTotal: number; stock: number[]; noDate: number; truncated: boolean }
interface DetailRow { lotId: string; inDay: string | null; outDay: string | null; line: string; inLine: string; pmEq: string; circle: string; procName: string; matType: string; matDesc: string; matId: string; sn: string; qty: number; amount: number; customer: string; unitPrice: number; paid: string }
/** 누른 칸 — key 구분, line 라인('' = 구분 전체), month 1~12(0 = 합계), stock 창고 */
interface Pick { key: string; line: string; month: number; stock: '' | 'dongtan' | 'cheonan'; label: string }

const MONTHS = Array.from({ length: 12 }, (_, i) => `${i + 1}월`);
const STOCKS: ['dongtan' | 'cheonan', string, number][] = [['dongtan', '동탄창고', 0], ['cheonan', '천안창고', 2]];

export default function MesShipments() {
  const status = useLegacyStatus();
  const thisYear = new Date().getFullYear();
  const [year, setYear] = useState(thisYear);
  const [group, setGroup] = useState('');
  const [type, setType] = useState('');
  // 기존 MES 와 같게: 무상세정은 빼고, 케미칼(SD-1 소분) 출고는 넣는다
  const [free, setFree] = useState(false);
  const [chem, setChem] = useState(true);
  const [skip, setSkip] = useState(false);
  const [tab, setTab] = useState<'data' | 'chart'>('data');
  const [data, setData] = useState<Ship | null>(null);
  const [err, setErr] = useState('');
  const [loading, setLoading] = useState(false);
  const [pick, setPick] = useState<Pick | null>(null);

  const params = () => {
    const p = new URLSearchParams({ year: String(year), free: String(free), chem: String(chem), skip: String(skip) });
    if (group) p.set('group', group); if (type.trim()) p.set('type', type.trim());
    return p;
  };
  async function load() {
    setLoading(true); setErr('');
    try { setData(await api.get<Ship>(`/api/mes-report/shipments?${params()}`)); }
    catch (e) { setErr(e instanceof Error ? e.message : '불러오지 못했습니다.'); }
    finally { setLoading(false); }
  }
  useEffect(() => { if (status?.configured) void load(); }, [status?.configured, year, group, free, chem, skip]);   // eslint-disable-line react-hooks/exhaustive-deps

  if (status === undefined) return <div className="page-loading">불러오는 중…</div>;
  if (!status?.configured) return <NotConnected title="출하실적 현황" />;

  function exportXlsx() {
    if (!data) return;
    const rows: (string | number | null)[][] = [];
    const add = (g: string, l: string, x: { qty: number[]; amount: number[]; qtyTotal: number; amountTotal: number; stock: number[] }) => {
      rows.push([g, l, '수량', ...x.qty, x.qtyTotal, x.stock[0], x.stock[2]]);
      rows.push([g, l, '금액', ...x.amount, x.amountTotal, x.stock[1], x.stock[3]]);
    };
    data.groups.forEach(g => { g.lines.forEach(l => add(g.name, l.line, l)); add(g.name, '소계', g); });
    add('합계', '', data);
    // 피벗용 — 소계·합계 없이 구분·라인·월마다 한 줄(수량·금액 칸). 그대로 피벗 테이블을 만들면 된다.
    const flat: (string | number | null)[][] = [];
    data.groups.forEach(g => g.lines.forEach(l => MONTHS.forEach((m, i) => {
      if (l.qty[i] || l.amount[i]) flat.push([g.name, l.line, i + 1, m, l.qty[i], l.amount[i]]);
    })));
    void downloadXlsx(`출하실적_${data.year}.xlsx`, `${data.year}`, ['구분', '라인', '항목', ...MONTHS, '합계', '동탄창고', '천안창고'], rows,
      [14, 14, 6, ...MONTHS.map(() => 13), 15, 13, 13],
      [{ name: '피벗용', head: ['구분', '라인', '월', '월이름', '수량', '금액'], rows: flat, widths: [16, 16, 6, 8, 10, 16] }]);
  }

  const groups = (status.groups ?? ['MEMORY', 'FOUNDRY', '기타세정']);
  return (
    <div className="mr-page">
      <header className="pg-header">
        <div><h2>출하실적 현황</h2><p>기존 MES 기준 · 구분 → 라인 → 월(실제 출하일) · 무상세정 제외·케미칼(SD-1) 포함이 기본(케미칼은 맨 아래 따로 묶음) · 위 수량(LOT 안의 개수) / 아래 금액(입고일 단가 × 수량) · 창고는 지금 재고 · 숫자를 누르면 품목 목록 · 읽기 전용</p></div>
      </header>
      <div className="pg-body">
        <div className="mr-filters">
          <select className="input" value={year} onChange={e => setYear(Number(e.target.value))} aria-label="조회 연도">
            {Array.from({ length: 6 }, (_, i) => thisYear - i).map(y => <option key={y} value={y}>{y}년</option>)}
          </select>
          <select className="input" value={group} onChange={e => setGroup(e.target.value)} aria-label="고객사 그룹">
            <option value="">고객사 그룹 전체</option>
            {groups.map(g => <option key={g} value={g}>{g}</option>)}
            <option value="chem">케미칼(SD-1)만</option>
          </select>
          <input className="input" placeholder="제품 TYPE·품명(예: Dummy Wafer)" value={type} onChange={e => setType(e.target.value)}
            onKeyDown={e => { if (e.key === 'Enter') void load(); }} />
          <label className="mr-chk" title="무상세정 LOT(청구 안 함)을 수량에 넣습니다. 금액은 0원이라 매출은 그대로입니다. 기본은 기존 MES 처럼 뺍니다."><input type="checkbox" checked={free} onChange={e => setFree(e.target.checked)} /> 무상세정 포함</label>
          <label className="mr-chk" title="[소분관리 › 케미칼 출고등록]의 SD-1 소분 판매분(기존 MES 출하실적에 들어 있음)"><input type="checkbox" checked={chem || group === 'chem'} disabled={group === 'chem'} onChange={e => setChem(e.target.checked)} /> 케미칼(SD-1) 포함</label>
          <label className="mr-chk" title="삼성(MEMORY·FOUNDRY) LOT 중 세정을 SKIP 한 것 — 세정 없이 돌려준 것이라 기존 MES 출하실적에서 빠집니다. 기타세정 고객사는 SKIP 이어도 무상이 아니면 매출에 들어갑니다."><input type="checkbox" checked={skip} onChange={e => setSkip(e.target.checked)} /> 삼성 세정 SKIP 포함</label>
          <button type="button" className="btn btn-primary" onClick={() => void load()} disabled={loading}>{loading ? '조회 중…' : '조회'}</button>
          {data && <button type="button" className="btn btn-ghost" onClick={exportXlsx}>엑셀</button>}
          {data && data.noDate > 0 && <span className="mr-dim">출고일을 읽지 못한 LOT {data.noDate}건 제외</span>}
          {data?.truncated && <span className="mr-err">한 해 출하가 너무 많아 일부만 셌습니다 — 관리자에게 알려 주세요.</span>}
        </div>
        {err && <div className="mr-err">{err}</div>}

        <div className="mr-tabs" role="tablist">
          <button type="button" role="tab" aria-selected={tab === 'data'} className={tab === 'data' ? 'on' : ''} onClick={() => setTab('data')}>데이터</button>
          <button type="button" role="tab" aria-selected={tab === 'chart'} className={tab === 'chart' ? 'on' : ''} onClick={() => setTab('chart')}>그래프</button>
        </div>

        {data && tab === 'data' && (
          <div className="mr-card">
            <div className="mr-scroll mr-ship-scroll">
              <table className="mr-table mr-ship">
                <thead>
                  <tr><th>고객사</th><th>라인</th>{MONTHS.map(m => <th key={m}>{m}</th>)}<th className="tot">합계</th>{STOCKS.map(([k, n]) => <th key={k} className="stk">{n}</th>)}</tr>
                </thead>
                <tbody>
                  {data.groups.map(g => <Group key={g.key} g={g} onPick={setPick} />)}
                  <Rows label="합계" x={data} pick={{ key: '', line: '', label: '전체' }} onPick={setPick} cls="mr-grand" first />
                  {data.groups.length === 0 && <tr><td colSpan={17} className="mr-empty">그 해 출하 기록이 없습니다.</td></tr>}
                </tbody>
              </table>
            </div>
          </div>
        )}

        {data && tab === 'chart' && (
          <div className="mr-charts">
            <Bars title={`${data.year}년 월별 출하 수량`} values={data.qty} fmt={qty} />
            <Bars title={`${data.year}년 월별 출하 금액 (원)`} values={data.amount} fmt={won} />
          </div>
        )}
      </div>
      {pick && data && <DetailModal year={data.year} params={params()} pick={pick} onClose={() => setPick(null)} />}
    </div>
  );
}

type OnPick = (p: Pick) => void;

function Group({ g, onPick }: { g: ShipGroup; onPick: OnPick }) {
  const span = (g.lines.length + 1) * 2;
  return (
    <>
      {g.lines.map((l, i) => (
        <Rows key={l.line} label={l.line} x={l} pick={{ key: g.key, line: l.line, label: `${g.name} · ${l.line}` }} onPick={onPick}
          lead={i === 0 ? <td rowSpan={span} className="cust">{g.name}</td> : null} />
      ))}
      <Rows label="소계" x={g} pick={{ key: g.key, line: '', label: `${g.name} 소계` }} onPick={onPick} cls="mr-sub" lead={g.lines.length === 0 ? <td rowSpan={2} className="cust">{g.name}</td> : null} />
    </>
  );
}

/** 한 줄 = 수량 줄 + 금액 줄. 숫자 칸을 누르면 그 조건의 품목 목록. */
function Rows({ label, x, pick, onPick, cls = '', lead, first }: {
  label: string; x: { qty: number[]; amount: number[]; qtyTotal: number; amountTotal: number; stock: number[] };
  pick: { key: string; line: string; label: string }; onPick: OnPick; cls?: string; lead?: React.ReactNode; first?: boolean;
}) {
  const cell = (v: number, fmt: (n: number) => string, month: number, stock: Pick['stock'], extra = '') => (
    <td className={`n${extra}${v ? ' hit' : ''}`} onClick={v ? () => onPick({ ...pick, month, stock, label: `${pick.label} · ${stock ? STOCKS.find(s => s[0] === stock)![1] : month ? `${month}월` : '합계'}` }) : undefined}>
      {v ? fmt(v) : ''}
    </td>
  );
  return (
    <>
      <tr className={cls}>
        {first ? <td colSpan={2} rowSpan={2} className="cust">{label}</td> : <>{lead}<td rowSpan={2} className="ln">{label}</td></>}
        {x.qty.map((v, i) => <Fragment key={i}>{cell(v, qty, i + 1, '')}</Fragment>)}
        {cell(x.qtyTotal, qty, 0, '', ' tot')}
        {STOCKS.map(([k, , i]) => <Fragment key={k}>{cell(x.stock[i], qty, 0, k, ' stk')}</Fragment>)}
      </tr>
      <tr className={`amt ${cls}`}>
        {x.amount.map((v, i) => <Fragment key={i}>{cell(v, won, i + 1, '')}</Fragment>)}
        {cell(x.amountTotal, won, 0, '', ' tot')}
        {STOCKS.map(([k, , i]) => <Fragment key={k}>{cell(x.stock[i + 1], won, 0, k, ' stk')}</Fragment>)}
      </tr>
    </>
  );
}

/** 숫자를 누른 목록 — 위: 품목별 수량·금액, 아래: LOT 목록(기존 MES '생산실적 세부내역' 칸) */
function DetailModal({ year, params, pick, onClose }: { year: number; params: URLSearchParams; pick: Pick; onClose: () => void }) {
  const [rows, setRows] = useState<DetailRow[] | null>(null);
  const [err, setErr] = useState('');
  const [item, setItem] = useState('');
  const [typ, setTyp] = useState<string | null>(null);
  useEffect(() => {
    const p = new URLSearchParams(params);
    p.set('year', String(year)); if (pick.key) p.set('key', pick.key); if (pick.line) p.set('line', pick.line);
    p.set('month', String(pick.month)); if (pick.stock) p.set('stock', pick.stock);
    api.get<DetailRow[]>(`/api/mes-report/shipments/detail?${p}`).then(setRows).catch(e => setErr(e instanceof Error ? e.message : '불러오지 못했습니다.'));
  }, []);   // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => { const k = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); }; window.addEventListener('keydown', k); return () => window.removeEventListener('keydown', k); }, [onClose]);

  // TYPE 별 개수·금액(맨 위) — 누르면 그 TYPE 만
  const types = useMemo(() => {
    const m = new Map<string, { qty: number; amount: number }>();
    (rows ?? []).forEach(r => { const x = m.get(r.matType) ?? { qty: 0, amount: 0 }; x.qty += r.qty; x.amount += r.amount; m.set(r.matType, x); });
    return [...m.entries()].sort((a, b) => b[1].qty - a[1].qty || a[0].localeCompare(b[0]));
  }, [rows]);
  const byType = (rows ?? []).filter(r => typ === null || r.matType === typ);
  const items = useMemo(() => {
    const m = new Map<string, { matId: string; matDesc: string; matType: string; qty: number; amount: number }>();
    byType.forEach(r => {
      const k = `${r.matId}|${r.matDesc}`;
      const x = m.get(k) ?? { matId: r.matId, matDesc: r.matDesc, matType: r.matType, qty: 0, amount: 0 };
      x.qty += r.qty; x.amount += r.amount; m.set(k, x);
    });
    return [...m.values()].sort((a, b) => b.qty - a.qty || a.matDesc.localeCompare(b.matDesc));
  }, [rows, typ]);   // eslint-disable-line react-hooks/exhaustive-deps
  const list = byType.filter(r => !item || `${r.matId}|${r.matDesc}` === item);
  const totQ = (rows ?? []).reduce((s, r) => s + r.qty, 0), totA = (rows ?? []).reduce((s, r) => s + r.amount, 0);
  const COLS: [keyof DetailRow, string, number][] = [['inDay', '입고일', 11], ['outDay', '출고일', 11], ['line', '반출라인', 10], ['inLine', '반입라인', 10], ['pmEq', 'PM설비명', 11],
    ['circle', '분임조', 9], ['procName', '공정', 10], ['paid', '유/무상', 8], ['matType', 'TYPE', 6], ['matDesc', '품명', 30], ['matId', '세정코드', 11], ['sn', 'S/N', 18], ['lotId', 'LOT ID', 15], ['qty', '수량', 7], ['unitPrice', '단가', 11], ['amount', '금액', 11]];
  const cols = pick.stock ? COLS.filter(c => c[0] !== 'outDay') : COLS;
  const exportXlsx = () => void downloadXlsx(`출하실적_${year}_${pick.label.replace(/[\\/:*?"<>|·]/g, '_')}.xlsx`, '세부내역', cols.map(c => c[1]),
    list.map(r => cols.map(([k]) => r[k] as string | number | null)), cols.map(c => c[2]));

  return (
    <div className="mr-modal" onMouseDown={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="mr-modal-box" role="dialog" aria-label="출하실적 세부내역">
        <div className="mr-card-h">
          <b>{pick.label}</b><span className="mr-dim">{year}년{pick.stock ? ' · 지금 재고' : ''}</span>
          {rows && <span className="mr-dim">{rows.length.toLocaleString()} LOT · 수량 {qty(totQ)} · 금액 {won(totA)}</span>}
          {rows && rows.length > 0 && (
            <button type="button" className="btn btn-ghost" onClick={exportXlsx} title="지금 아래 세부 내역에 보이는 LOT 만 내려받습니다">
              {item || typ !== null ? `엑셀(선택한 ${list.length.toLocaleString()}건만)` : '엑셀(전체)'}
            </button>
          )}
          <button type="button" className="mr-x" onClick={onClose} aria-label="닫기">✕</button>
        </div>
        {err && <div className="mr-err">{err}</div>}
        {!rows && !err && <div className="mr-empty">불러오는 중…</div>}
        {rows && (
          <div className="mr-modal-body">
            <div className="mr-types">
              <span className="mr-types-h">TYPE별</span>
              <button type="button" className={`mr-type ${typ === null ? 'on' : ''}`} onClick={() => { setTyp(null); setItem(''); }}>
                <b>전체</b><span>{qty(totQ)}개</span><em>{won(totA)}원</em>
              </button>
              {types.map(([t, v]) => (
                <button type="button" key={t} className={`mr-type ${typ === t ? 'on' : ''}`} onClick={() => { setTyp(typ === t ? null : t); setItem(''); }}>
                  <b>{t || '(없음)'}</b><span>{qty(v.qty)}개</span><em>{won(v.amount)}원</em>
                </button>
              ))}
            </div>
            <div className="mr-items">
              <div className="mr-items-h">품목별 수량{typ !== null && <span className="mr-dim"> — TYPE {typ || '(없음)'}</span>} <span className="mr-dim">{items.length}개 품목 · 누르면 아래 목록을 그 품목만</span></div>
              <table className="mr-table">
                <thead><tr><th>품명</th><th>세정코드</th><th>TYPE</th><th>수량</th><th>금액</th></tr></thead>
                <tbody>
                  {items.map(it => {
                    const k = `${it.matId}|${it.matDesc}`;
                    return (
                      <tr key={k} className={`click${item === k ? ' on' : ''}`} onClick={() => setItem(item === k ? '' : k)}>
                        <td className="w">{it.matDesc}</td><td>{it.matId}</td><td>{it.matType}</td><td className="n"><b>{qty(it.qty)}</b></td><td className="n">{won(it.amount)}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
            <div className="mr-items-h">세부 내역 {item && <><span className="mr-dim">— {items.find(i => `${i.matId}|${i.matDesc}` === item)?.matDesc}</span> <button type="button" className="mr-num" onClick={() => setItem('')}>전체 보기</button></>}</div>
            <div className="mr-scroll mr-detail-list">
              <table className="mr-table mr-lots">
                <thead><tr><th className="no">#</th>{cols.map(([k, l]) => <th key={k}>{l}</th>)}</tr></thead>
                <tbody>
                  {list.map((r, i) => (
                    <tr key={r.lotId}>
                      <td className="no">{i + 1}</td>
                      {cols.map(([k]) => <td key={k} className={k === 'qty' || k === 'amount' || k === 'unitPrice' ? 'n' : k === 'matDesc' ? 'w' : ''}>
                        {k === 'qty' ? qty(r.qty) : k === 'amount' ? won(r.amount) : k === 'unitPrice' ? won(r.unitPrice) : k === 'inLine' && r.inLine !== r.line ? <b className="mr-red">{r.inLine}</b> : (r[k] as string)}
                      </td>)}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </div>
    </div>
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
