import { useCallback, useEffect, useMemo, useState } from 'react';
import { api } from '../../api/client';
import LotTable from './LotTable';
import NotConnected from './NotConnected';
import { downloadLots, useLegacyStatus, type LotRow } from './mesReport';
import './MesReport.css';

// MES 조회 › 입출고 현황 — 기존 MES DB 의 재공(지금 공장 안 LOT)을 품목 × 공정 · 반출라인으로 센다.
// 숫자를 누르면 아래에 그 LOT 목록. [LOT 조회] 탭은 입고일 기간으로 LOT 를 찾는다(예전 조회 쿼리와 같은 칸).

/** 공정 칸 순서와 이름(기존 입출고 현황 화면 기준). 목록에 없는 공정은 코드 그대로 뒤에 붙는다. */
const OPERS: [string, string][] = [
  ['2000', 'INPUT'], ['2100', 'IN INSP'], ['3000', 'CLEANING'], ['4000', 'DRY'], ['4100', 'LASER&CO2'], ['5000', 'BAKE'],
  ['6000', 'REPAIR'], ['7000', 'FI INSP'], ['7100', 'BOXING'], ['8000', '천안창고'], ['8100', '동탄창고'],
];
const NO_LINE = '(라인 없음)';
/** 줄을 거르는 조건 — 이 조건으로 LOT 를 걸러 세고, 숫자를 누르면 같은 조건으로 LOT 목록을 보인다(숫자와 목록이 늘 같다). */
interface Key { group?: string; line?: string; proc?: string; matGroup?: string; matId?: string; oper?: string }
const matches = (l: LotRow, k: Key) =>
  (k.group === undefined || l.group === k.group) && (k.line === undefined || (l.line || NO_LINE) === k.line)
  && (k.proc === undefined || l.procName === k.proc) && (k.matGroup === undefined || l.matGroup === k.matGroup)
  && (k.matId === undefined || l.matId === k.matId) && (k.oper === undefined || l.oper === k.oper);
const cmp = (a: string, b: string) => a.localeCompare(b, 'ko', { numeric: true });

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
        {tab === 'wip' ? <WipTab groups={status.groups ?? ['MEMORY', 'FOUNDRY', '기타세정']} /> : <LotsTab />}
      </div>
    </div>
  );
}

function WipTab({ groups }: { groups: string[] }) {
  const [all, setAll] = useState<LotRow[] | null>(null);
  const [err, setErr] = useState('');
  const [loading, setLoading] = useState(false);
  // 조회 조건 — 칸마다 따로
  const [fGroup, setFGroup] = useState('');
  const [fLine, setFLine] = useState('');
  const [fProc, setFProc] = useState('');
  const [fMatGroup, setFMatGroup] = useState('');
  const [fMatId, setFMatId] = useState('');
  const [fDesc, setFDesc] = useState('');
  const [pick, setPick] = useState<{ key: Key; label: string } | null>(null);

  const load = useCallback(async () => {
    setLoading(true); setErr('');
    try { setAll(await api.get<LotRow[]>('/api/mes-report/wip/lots')); }
    catch (e) { setErr(e instanceof Error ? e.message : '불러오지 못했습니다.'); }
    finally { setLoading(false); }
  }, []);
  useEffect(() => { void load(); }, [load]);

  const groupOrder = (g: string) => { const i = groups.indexOf(g); return i < 0 ? groups.length : i; };
  const opts = useMemo(() => {
    const u = (f: (l: LotRow) => string) => [...new Set((all ?? []).map(f).filter(Boolean))].sort(cmp);
    return { line: u(l => l.line || NO_LINE), proc: u(l => l.procName), matGroup: u(l => l.matGroup) };
  }, [all]);

  // 조건에 맞는 LOT
  const lots = useMemo(() => (all ?? []).filter(l =>
    (!fGroup || l.group === fGroup) && (!fLine || (l.line || NO_LINE) === fLine) && (!fProc || l.procName === fProc)
    && (!fMatGroup || l.matGroup === fMatGroup)
    && (!fMatId.trim() || l.matId.toLowerCase().includes(fMatId.trim().toLowerCase()))
    && (!fDesc.trim() || l.matDesc.toLowerCase().includes(fDesc.trim().toLowerCase()))), [all, fGroup, fLine, fProc, fMatGroup, fMatId, fDesc]);

  // 표: 구분(MEMORY → FOUNDRY → 기타세정) 묶음 아래 라인 → PROCESS → 제품군 → 품명 줄
  const view = useMemo(() => {
    const known = new Set(OPERS.map(o => o[0]));
    const cols: [string, string][] = [...OPERS, ...[...new Set(lots.map(l => l.oper).filter(o => o && !known.has(o)))].sort(cmp).map(o => [o, o] as [string, string])];
    const count = (xs: LotRow[]) => { const m: Record<string, number> = {}; xs.forEach(l => { m[l.oper] = (m[l.oper] ?? 0) + 1; }); return m; };
    const byGroup = new Map<string, LotRow[]>();
    lots.forEach(l => { const g = byGroup.get(l.group) ?? []; g.push(l); byGroup.set(l.group, g); });
    const sections = [...byGroup.entries()].sort((a, b) => groupOrder(a[0]) - groupOrder(b[0]) || cmp(a[0], b[0])).map(([group, xs]) => {
      const rowsMap = new Map<string, { key: Key; line: string; proc: string; matGroup: string; matDesc: string; matId: string; lots: LotRow[] }>();
      xs.forEach(l => {
        const line = l.line || NO_LINE;
        const id = [line, l.procName, l.matGroup, l.matId].join('\u0001');
        let r = rowsMap.get(id);
        if (!r) { r = { key: { group, line, proc: l.procName, matGroup: l.matGroup, matId: l.matId }, line, proc: l.procName, matGroup: l.matGroup, matDesc: l.matDesc, matId: l.matId, lots: [] }; rowsMap.set(id, r); }
        r.lots.push(l);
      });
      const rows = [...rowsMap.values()].sort((a, b) => cmp(a.line, b.line) || cmp(a.proc, b.proc) || cmp(a.matGroup, b.matGroup) || cmp(a.matDesc, b.matDesc))
        .map(r => ({ ...r, counts: count(r.lots), total: r.lots.length }));
      return { group, counts: count(xs), total: xs.length, rows };
    });
    return { cols, sections, counts: count(lots), total: lots.length };
  }, [lots, groups]);   // eslint-disable-line react-hooks/exhaustive-deps

  const detail = useMemo(() => (pick ? lots.filter(l => matches(l, pick.key)) : []), [pick, lots]);

  const cell = (n: number | undefined, key: Key, label: string, cls = '') => (
    <td className={`n${cls}`}>{n ? <button type="button" className="mr-num" onClick={() => setPick({ key, label })}>{n.toLocaleString()}</button> : ''}</td>
  );
  const colLabel = (o: string) => view.cols.find(c => c[0] === o)?.[1] ?? o;
  const reset = () => { setFGroup(''); setFLine(''); setFProc(''); setFMatGroup(''); setFMatId(''); setFDesc(''); };
  const width = 6 + view.cols.length + 1;

  return (
    <>
      <div className="mr-filters">
        <select className="input" value={fGroup} onChange={e => setFGroup(e.target.value)} aria-label="구분">
          <option value="">구분 전체</option>{groups.map(g => <option key={g} value={g}>{g}</option>)}
        </select>
        <select className="input" value={fLine} onChange={e => setFLine(e.target.value)} aria-label="라인">
          <option value="">라인 전체</option>{opts.line.map(x => <option key={x} value={x}>{x}</option>)}
        </select>
        <select className="input" value={fProc} onChange={e => setFProc(e.target.value)} aria-label="PROCESS">
          <option value="">PROCESS 전체</option>{opts.proc.map(x => <option key={x} value={x}>{x}</option>)}
        </select>
        <select className="input" value={fMatGroup} onChange={e => setFMatGroup(e.target.value)} aria-label="제품군">
          <option value="">제품군 전체</option>{opts.matGroup.map(x => <option key={x} value={x}>{x}</option>)}
        </select>
        <input className="input mr-w-sm" placeholder="품목코드" value={fMatId} onChange={e => setFMatId(e.target.value)} aria-label="품목코드" />
        <input className="input" placeholder="품명" value={fDesc} onChange={e => setFDesc(e.target.value)} aria-label="품명" />
        <button type="button" className="btn btn-ghost" onClick={reset}>조건 지우기</button>
        <button type="button" className="btn btn-primary" onClick={() => void load()} disabled={loading}>{loading ? '조회 중…' : '새로 조회'}</button>
        {all && <span className="mr-dim">{lots.length.toLocaleString()} / {all.length.toLocaleString()} LOT</span>}
      </div>
      {err && <div className="mr-err">{err}</div>}
      {!all && !err && <div className="mr-dim">불러오는 중…</div>}

      {all && (
        <div className="mr-card">
          <div className="mr-scroll mr-wip-scroll">
            <table className="mr-table mr-wip">
              <thead>
                <tr>
                  <th rowSpan={2}>라인</th><th rowSpan={2}>PROCESS</th><th rowSpan={2}>제품군</th><th rowSpan={2}>품명</th><th rowSpan={2}>품목코드</th>
                  <th colSpan={view.cols.length + 1} className="grp">재공 현황</th>
                </tr>
                <tr>
                  {view.cols.map(([o, l]) => <th key={o} title={o}>{l}</th>)}
                  <th className="tot">합계</th>
                </tr>
              </thead>
              <tbody>
                <tr className="mr-total">
                  <td colSpan={5}>Total</td>
                  {view.cols.map(([o, l]) => cell(view.counts[o], { oper: o }, `전체 · ${l}`))}
                  {cell(view.total, {}, '전체', ' tot')}
                </tr>
                {view.sections.map(sec => (
                  <Section key={sec.group} sec={sec} cols={view.cols} cell={cell} colLabel={colLabel} />
                ))}
                {lots.length === 0 && <tr><td colSpan={width} className="mr-empty">조건에 맞는 재공이 없습니다.</td></tr>}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {pick && (
        <div className="mr-card">
          <div className="mr-card-h">
            <b>{pick.label}</b> <span className="mr-dim">{detail.length.toLocaleString()} LOT</span>
            {detail.length > 0 && <button type="button" className="btn btn-ghost" onClick={() => void downloadLots(`재공_${pick.label.replace(/[\\/:*?"<>|]/g, '_')}.xlsx`, detail)}>엑셀</button>}
            <button type="button" className="mr-x" onClick={() => setPick(null)} aria-label="닫기">✕</button>
          </div>
          <LotTable rows={detail} />
        </div>
      )}
    </>
  );
}

type CellFn = (n: number | undefined, key: Key, label: string, cls?: string) => React.ReactElement;
interface SectionData {
  group: string; counts: Record<string, number>; total: number;
  rows: { key: Key; line: string; proc: string; matGroup: string; matDesc: string; matId: string; counts: Record<string, number>; total: number }[];
}

/** 구분 묶음 — 머리 줄(소계) 아래 줄들. 같은 라인·PROCESS·제품군이 이어지면 이름은 첫 줄에만. */
function Section({ sec, cols, cell, colLabel }: { sec: SectionData; cols: [string, string][]; cell: CellFn; colLabel: (o: string) => string }) {
  return (
    <>
      <tr className="mr-sec">
        <td colSpan={5}>{sec.group} <span className="mr-dim">소계</span></td>
        {cols.map(([o]) => cell(sec.counts[o], { group: sec.group, oper: o }, `${sec.group} · ${colLabel(o)}`))}
        {cell(sec.total, { group: sec.group }, sec.group, ' tot')}
      </tr>
      {sec.rows.map((r, i) => {
        const prev = sec.rows[i - 1];
        const newLine = !prev || prev.line !== r.line;
        const newProc = newLine || prev.proc !== r.proc;
        const newGrp = newProc || prev.matGroup !== r.matGroup;
        const name = `${r.line} · ${r.matDesc}`;
        return (
          <tr key={`${r.line}|${r.proc}|${r.matGroup}|${r.matId}`} className={newLine ? 'grp-first' : ''}>
            <td className="t">{newLine ? r.line : ''}</td>
            <td className="t">{newProc ? (r.proc || '—') : ''}</td>
            <td>{newGrp ? (r.matGroup || '—') : ''}</td>
            <td className="w">{r.matDesc}</td>
            <td>{r.matId}</td>
            {cols.map(([o]) => cell(r.counts[o], { ...r.key, oper: o }, `${name} · ${colLabel(o)}`))}
            {cell(r.total, r.key, name, ' tot')}
          </tr>
        );
      })}
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
