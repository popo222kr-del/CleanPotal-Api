import { useCallback, useEffect, useMemo, useState } from 'react';
import { api } from '../../api/client';
import LotTable from './LotTable';
import NotConnected from './NotConnected';
import { downloadLots, useLegacyStatus, type LotRow } from './mesReport';
import './MesReport.css';

// MES 조회 › 입출고 현황 — 기존 MES DB 의 재공(지금 공장 안 LOT)을 품목 × 공정 · 반출라인으로 센다.
// 숫자를 누르면 아래에 그 LOT 목록. [LOT 조회] 탭은 입고일 기간으로 LOT 를 찾는다(예전 조회 쿼리와 같은 칸).

interface WipRow { matType: string; matId: string; matDesc: string; opers: Record<string, number>; total: number; lines: Record<string, number> }
interface Wip { columns: { oper: string; label: string }[]; lines: string[]; rows: WipRow[]; operTotals: Record<string, number>; total: number; lineTotals: Record<string, number> }
interface Pick { oper?: string; matId?: string; line?: string; label: string }

const ymd = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

export default function MesWip() {
  const status = useLegacyStatus();
  const [tab, setTab] = useState<'wip' | 'lots'>('wip');
  if (status === undefined) return <div className="page-loading">불러오는 중…</div>;
  if (!status?.configured) return <NotConnected title="입출고 현황" />;
  return (
    <div className="mr-page">
      <header className="pg-header">
        <div><h2>입출고 현황</h2><p>기존 MES 기준 재공(지금 공장 안 LOT)과 LOT 조회 · 읽기 전용</p></div>
      </header>
      <div className="pg-body">
        <div className="mr-tabs" role="tablist">
          <button type="button" role="tab" aria-selected={tab === 'wip'} className={tab === 'wip' ? 'on' : ''} onClick={() => setTab('wip')}>재공 현황</button>
          <button type="button" role="tab" aria-selected={tab === 'lots'} className={tab === 'lots' ? 'on' : ''} onClick={() => setTab('lots')}>LOT 조회 (입고일 기간)</button>
        </div>
        {tab === 'wip' ? <WipTab names={status.customerNames} /> : <LotsTab />}
      </div>
    </div>
  );
}

function WipTab({ names }: { names: Record<string, string> }) {
  const [q, setQ] = useState('');
  const [customer, setCustomer] = useState('');
  const [line, setLine] = useState('');
  const [data, setData] = useState<Wip | null>(null);
  const [err, setErr] = useState('');
  const [loading, setLoading] = useState(false);
  const [pick, setPick] = useState<Pick | null>(null);
  const [lots, setLots] = useState<LotRow[] | null>(null);

  const params = useCallback((extra: Record<string, string | undefined> = {}) => {
    const p = new URLSearchParams();
    if (q.trim()) p.set('q', q.trim());
    if (customer) p.set('customer', customer);
    if (line) p.set('line', line);
    Object.entries(extra).forEach(([k, v]) => { if (v) p.set(k, v); });
    return p.toString();
  }, [q, customer, line]);

  const load = useCallback(async () => {
    setLoading(true); setErr('');
    try { setData(await api.get<Wip>(`/api/mes-report/wip?${params()}`)); }
    catch (e) { setErr(e instanceof Error ? e.message : '불러오지 못했습니다.'); }
    finally { setLoading(false); }
  }, [params]);
  useEffect(() => { void load(); }, [customer, line]);   // eslint-disable-line react-hooks/exhaustive-deps

  async function open(p: Pick) {
    setPick(p); setLots(null);
    try { setLots(await api.get<LotRow[]>(`/api/mes-report/wip/lots?${params({ oper: p.oper, matId: p.matId, line: p.line })}`)); }
    catch (e) { setErr(e instanceof Error ? e.message : 'LOT 목록을 불러오지 못했습니다.'); setLots([]); }
  }

  // 같은 TYPE 은 첫 줄에만 이름을 쓴다
  const rows = data?.rows ?? [];
  const firstOfType = useMemo(() => rows.map((r, i) => i === 0 || rows[i - 1].matType !== r.matType), [rows]);
  const cell = (n: number | undefined, p: Pick, cls = '') => (
    <td className={`n${cls}`}>{n ? <button type="button" className="mr-num" onClick={() => void open(p)}>{n.toLocaleString()}</button> : ''}</td>
  );

  return (
    <>
      <div className="mr-filters">
        <input className="input" placeholder="품목코드·품명·TYPE 검색" value={q} onChange={e => setQ(e.target.value)}
          onKeyDown={e => { if (e.key === 'Enter') void load(); }} />
        <select className="input" value={customer} onChange={e => setCustomer(e.target.value)} aria-label="고객사">
          <option value="">고객사 전체</option>
          {Object.entries(names).map(([c, n]) => <option key={c} value={c}>{n}</option>)}
        </select>
        <select className="input" value={line} onChange={e => setLine(e.target.value)} aria-label="반출라인">
          <option value="">반출라인 전체</option>
          {(data?.lines ?? []).map(l => <option key={l} value={l}>{l}</option>)}
        </select>
        <button type="button" className="btn btn-primary" onClick={() => void load()} disabled={loading}>{loading ? '조회 중…' : '조회'}</button>
        <span className="mr-dim">1분마다 새로 읽습니다</span>
      </div>
      {err && <div className="mr-err">{err}</div>}

      {data && (
        <div className="mr-card">
          <div className="mr-scroll mr-wip-scroll">
            <table className="mr-table mr-wip">
              <thead>
                <tr>
                  <th rowSpan={2}>TYPE</th><th rowSpan={2}>품명</th><th rowSpan={2}>품목코드</th>
                  <th colSpan={data.columns.length + 1} className="grp">재공 현황</th>
                  {data.lines.length > 0 && <th colSpan={data.lines.length} className="grp ln">반출라인</th>}
                </tr>
                <tr>
                  {data.columns.map(c => <th key={c.oper} title={c.oper}>{c.label}</th>)}
                  <th className="tot">합계</th>
                  {data.lines.map(l => <th key={l} className="ln">{l}</th>)}
                </tr>
              </thead>
              <tbody>
                <tr className="mr-total">
                  <td colSpan={3}>Total</td>
                  {data.columns.map(c => cell(data.operTotals[c.oper], { oper: c.oper, label: c.label }))}
                  {cell(data.total, { label: '전체' }, ' tot')}
                  {data.lines.map(l => cell(data.lineTotals[l], { line: l, label: l }, ' ln'))}
                </tr>
                {rows.map((r, i) => (
                  <tr key={`${r.matType}|${r.matId}`} className={firstOfType[i] ? 'grp-first' : ''}>
                    <td className="t">{firstOfType[i] ? r.matType || '—' : ''}</td>
                    <td className="w">{r.matDesc}</td>
                    <td>{r.matId}</td>
                    {data.columns.map(c => cell(r.opers[c.oper], { oper: c.oper, matId: r.matId, label: `${r.matDesc} · ${c.label}` }))}
                    {cell(r.total, { matId: r.matId, label: r.matDesc }, ' tot')}
                    {data.lines.map(l => cell(r.lines[l], { matId: r.matId, line: l, label: `${r.matDesc} · ${l}` }, ' ln'))}
                  </tr>
                ))}
                {rows.length === 0 && <tr><td colSpan={4 + data.columns.length + data.lines.length} className="mr-empty">재공이 없습니다.</td></tr>}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {pick && (
        <div className="mr-card">
          <div className="mr-card-h">
            <b>{pick.label}</b> <span className="mr-dim">{lots ? `${lots.length.toLocaleString()} LOT` : '불러오는 중…'}</span>
            {lots && lots.length > 0 && <button type="button" className="btn btn-ghost" onClick={() => void downloadLots(`재공_${pick.label}.xlsx`, lots)}>엑셀</button>}
            <button type="button" className="mr-x" onClick={() => setPick(null)} aria-label="닫기">✕</button>
          </div>
          {lots && <LotTable rows={lots} />}
        </div>
      )}
    </>
  );
}

function LotsTab() {
  const now = new Date();
  const [from, setFrom] = useState(ymd(new Date(now.getFullYear(), now.getMonth(), 1)));
  const [to, setTo] = useState(ymd(now));
  const [q, setQ] = useState('');
  const [rows, setRows] = useState<LotRow[] | null>(null);
  const [truncated, setTruncated] = useState(false);
  const [err, setErr] = useState('');
  const [loading, setLoading] = useState(false);

  async function load() {
    setLoading(true); setErr('');
    try {
      const p = new URLSearchParams({ from, to });
      if (q.trim()) p.set('q', q.trim());
      const r = await api.get<{ truncated: boolean; rows: LotRow[] }>(`/api/mes-report/lots?${p}`);
      setRows(r.rows); setTruncated(r.truncated);
    } catch (e) { setErr(e instanceof Error ? e.message : '불러오지 못했습니다.'); }
    finally { setLoading(false); }
  }
  useEffect(() => { void load(); }, []);   // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <>
      <div className="mr-filters">
        <span className="mr-dim">AETS 입고일</span>
        <input type="date" className="input" value={from} onChange={e => setFrom(e.target.value)} aria-label="시작일" />
        <span>~</span>
        <input type="date" className="input" value={to} onChange={e => setTo(e.target.value)} aria-label="끝일" />
        <input className="input" placeholder="LOT·품목코드·품명·TYPE" value={q} onChange={e => setQ(e.target.value)} onKeyDown={e => { if (e.key === 'Enter') void load(); }} />
        <button type="button" className="btn btn-primary" onClick={() => void load()} disabled={loading}>{loading ? '조회 중…' : '조회'}</button>
        {rows && rows.length > 0 && <button type="button" className="btn btn-ghost" onClick={() => void downloadLots(`LOT조회_${from}_${to}.xlsx`, rows)}>엑셀</button>}
        {rows && <span className="mr-dim">{rows.length.toLocaleString()} LOT{truncated ? ' (최대 2만 건까지 — 기간을 줄이세요)' : ''}</span>}
      </div>
      {err && <div className="mr-err">{err}</div>}
      {rows && <div className="mr-card"><LotTable rows={rows} /></div>}
    </>
  );
}
