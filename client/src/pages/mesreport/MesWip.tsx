import { useCallback, useEffect, useMemo, useState } from 'react';
import { api } from '../../api/client';
import LotTable from './LotTable';
import NotConnected from './NotConnected';
import { downloadLots, HIST_COLS, useLegacyStatus, type LotCol, type LotRow } from './mesReport';
import { cmp, inScope, matches, NO_FILTERS, NO_LINE, openLotsWindow, OTHER, passes, secOf, type Filters, type Key, type Scope } from './wipShared';
import './MesReport.css';

// MES 조회 › 삼성 입·출고 현황 / 기타 입·출고 현황(scope) — 기존 MES DB 의 재공(지금 공장 안 LOT)을 품목 × 공정 · 반출라인으로 센다.
// 숫자를 누르면 아래에 그 LOT 목록. [세정 이력 조회] 탭은 기존 MES 의 같은 이름 화면처럼 입고일 기간·WIP/SHIP·칸별 조건으로 LOT 를 찾는다(출하된 LOT 포함).

/** 공정 칸 순서와 이름(기존 입출고 현황 화면 기준). 목록에 없는 공정은 코드 그대로 뒤에 붙는다. */
const OPERS: [string, string][] = [
  ['2000', 'INPUT'], ['2100', 'IN INSP'], ['3000', 'CLEANING'], ['4000', 'DRY'], ['4100', 'LASER&CO2'], ['5000', 'BAKE'],
  ['6000', 'REPAIR'], ['7000', 'FI INSP'], ['7100', 'BOXING'], ['8000', '천안창고'], ['8100', '동탄창고'],
];
const ymd = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

export default function MesWip({ scope = 'samsung' }: { scope?: Scope }) {
  const status = useLegacyStatus();
  const title = scope === 'other' ? '기타 입·출고 현황' : '삼성 입·출고 현황';
  const sub = scope === 'other' ? '삼성(MEMORY·FOUNDRY) 외 고객사' : 'MEMORY · FOUNDRY';
  const [tab, setTab] = useState<'wip' | 'hist'>('wip');
  if (status === undefined) return <div className="page-loading">불러오는 중…</div>;
  if (!status?.configured) return <NotConnected title={title} />;
  return (
    <div className="mr-page">
      <header className="pg-header">
        <div><h2>{title}</h2><p>{sub} · 기존 MES 기준 재공(지금 공장 안 LOT)과 세정 이력 조회 · 읽기 전용</p></div>
      </header>
      <div className="pg-body">
        <div className="mr-tabs" role="tablist">
          <button type="button" role="tab" aria-selected={tab === 'wip'} className={tab === 'wip' ? 'on' : ''} onClick={() => setTab('wip')}>재공 현황</button>
          <button type="button" role="tab" aria-selected={tab === 'hist'} className={tab === 'hist' ? 'on' : ''} onClick={() => setTab('hist')}>세정 이력 조회</button>
        </div>
        {tab === 'wip' ? <WipTab key={scope} scope={scope} groups={status.groups ?? ['MEMORY', 'FOUNDRY', OTHER]} /> : <HistoryTab key={scope} scope={scope} />}
      </div>
    </div>
  );
}

function WipTab({ groups, scope }: { groups: string[]; scope: Scope }) {
  const [all, setAll] = useState<LotRow[] | null>(null);
  const [err, setErr] = useState('');
  const [loading, setLoading] = useState(false);
  // 조회 조건 — 칸마다 따로
  const [f, setF] = useState<Filters>(NO_FILTERS);
  const setOne = (k: keyof Filters) => (e: { target: { value: string } }) => setF(x => ({ ...x, [k]: e.target.value }));
  const [pick, setPick] = useState<{ key: Key; label: string } | null>(null);

  const load = useCallback(async () => {
    setLoading(true); setErr('');
    try { setAll(await api.get<LotRow[]>('/api/mes-report/wip/lots')); }
    catch (e) { setErr(e instanceof Error ? e.message : '불러오지 못했습니다.'); }
    finally { setLoading(false); }
  }, []);
  useEffect(() => { void load(); }, [load]);

  const groupOrder = (g: string) => { const i = groups.indexOf(g); return i < 0 ? groups.length : i; };
  // 이 화면(삼성/기타)에 해당하는 LOT
  const mine = useMemo(() => (all ?? []).filter(l => inScope(l, scope)), [all, scope]);
  const opts = useMemo(() => {
    const u = (f: (l: LotRow) => string) => [...new Set(mine.map(f).filter(Boolean))].sort(cmp);
    return { line: u(l => l.line || NO_LINE), proc: u(l => l.procName), matGroup: u(l => l.matGroup) };
  }, [mine]);
  const secNames = useMemo(() => (scope === 'other'
    ? [...new Set(mine.map(l => secOf(l, scope)))].sort(cmp)
    : groups.filter(g => g !== OTHER)), [mine, scope, groups]);
  const lots = useMemo(() => mine.filter(l => passes(l, f, scope)), [mine, f, scope]);

  // 표: 구분(MEMORY → FOUNDRY → 기타세정) 묶음 아래 라인 → PROCESS → 제품군 → 품명 줄
  const view = useMemo(() => {
    const known = new Set(OPERS.map(o => o[0]));
    const cols: [string, string][] = [...OPERS, ...[...new Set(lots.map(l => l.oper).filter(o => o && !known.has(o)))].sort(cmp).map(o => [o, o] as [string, string])];
    const count = (xs: LotRow[]) => { const m: Record<string, number> = {}; xs.forEach(l => { m[l.oper] = (m[l.oper] ?? 0) + 1; }); return m; };
    const byGroup = new Map<string, LotRow[]>();
    lots.forEach(l => { const k = secOf(l, scope); const g = byGroup.get(k) ?? []; g.push(l); byGroup.set(k, g); });
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
  }, [lots, groups, scope]);   // eslint-disable-line react-hooks/exhaustive-deps

  const detail = useMemo(() => (pick ? lots.filter(l => matches(l, pick.key, scope)) : []), [pick, lots, scope]);

  const cell = (n: number | undefined, key: Key, label: string, cls = '') => (
    n
      ? <td className={`n hit${cls}`} title={`${label} — 눌러서 LOT 목록`} onClick={() => setPick({ key, label })}><button type="button" className="mr-num">{n.toLocaleString()}</button></td>
      : <td className={`n${cls}`} />
  );
  const colLabel = (o: string) => view.cols.find(c => c[0] === o)?.[1] ?? o;
  const reset = () => setF(NO_FILTERS);
  const width = 6 + view.cols.length + 1;

  return (
    <>
      <div className="mr-filters">
        <select className="input" value={f.group} onChange={setOne('group')} aria-label={scope === 'other' ? '고객사' : '구분'}>
          <option value="">{scope === 'other' ? '고객사 전체' : '구분 전체'}</option>{secNames.map(g => <option key={g} value={g}>{g}</option>)}
        </select>
        <select className="input" value={f.line} onChange={setOne('line')} aria-label="라인">
          <option value="">라인 전체</option>{opts.line.map(x => <option key={x} value={x}>{x}</option>)}
        </select>
        <select className="input" value={f.proc} onChange={setOne('proc')} aria-label="PROCESS">
          <option value="">PROCESS 전체</option>{opts.proc.map(x => <option key={x} value={x}>{x}</option>)}
        </select>
        <select className="input" value={f.matGroup} onChange={setOne('matGroup')} aria-label="제품군">
          <option value="">제품군 전체</option>{opts.matGroup.map(x => <option key={x} value={x}>{x}</option>)}
        </select>
        <input className="input mr-w-sm" placeholder="품목코드" value={f.matId} onChange={setOne('matId')} aria-label="품목코드" />
        <input className="input" placeholder="품명" value={f.desc} onChange={setOne('desc')} aria-label="품명" />
        <button type="button" className="btn btn-ghost" onClick={reset}>조건 지우기</button>
        <button type="button" className="btn btn-primary" onClick={() => void load()} disabled={loading}>{loading ? '조회 중…' : '새로 조회'}</button>
        {all && <span className="mr-dim">{lots.length.toLocaleString()} / {mine.length.toLocaleString()} LOT</span>}
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
            <button type="button" className="btn btn-ghost" onClick={() => openLotsWindow(scope, pick.key, f, pick.label)}>새 창으로 보기</button>
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

/** 같은 값이 이어지는 줄 수 — 셀 병합(rowSpan)용. 위 단계가 바뀌면 끊는다. */
function spans<T>(rows: T[], key: (r: T) => string): number[] {
  const out = new Array<number>(rows.length).fill(0);
  for (let i = 0; i < rows.length;) {
    let j = i + 1;
    while (j < rows.length && key(rows[j]) === key(rows[i])) j++;
    out[i] = j - i;
    i = j;
  }
  return out;
}

/** 묶음(구분 또는 고객사) — 머리 줄(소계) 아래 줄들. 라인·PROCESS·제품군은 엑셀 셀 병합처럼 이어지는 줄 전체에 걸친다. */
function Section({ sec, cols, cell, colLabel }: { sec: SectionData; cols: [string, string][]; cell: CellFn; colLabel: (o: string) => string }) {
  const lineSpan = spans(sec.rows, r => r.line);
  const procSpan = spans(sec.rows, r => `${r.line}\u0001${r.proc}`);
  const grpSpan = spans(sec.rows, r => `${r.line}\u0001${r.proc}\u0001${r.matGroup}`);
  // 라인마다 흰색·회색을 번갈아 — 라인 경계가 눈에 띄게
  let band = -1;
  const bands = lineSpan.map(n => (n > 0 ? ++band : band) % 2 === 1);
  return (
    <>
      <tr className="mr-sec">
        <td colSpan={5}>{sec.group} <span className="mr-dim">소계</span></td>
        {cols.map(([o]) => cell(sec.counts[o], { group: sec.group, oper: o }, `${sec.group} · ${colLabel(o)}`))}
        {cell(sec.total, { group: sec.group }, sec.group, ' tot')}
      </tr>
      {sec.rows.map((r, i) => {
        const name = `${r.line} · ${r.matDesc}`;
        return (
          <tr key={`${r.line}|${r.proc}|${r.matGroup}|${r.matId}`} className={`${lineSpan[i] ? 'grp-first' : ''}${bands[i] ? ' band' : ''}`}>
            {lineSpan[i] > 0 && <td className="t merge" rowSpan={lineSpan[i]}>{r.line}</td>}
            {procSpan[i] > 0 && <td className="t merge" rowSpan={procSpan[i]}>{r.proc || '—'}</td>}
            {grpSpan[i] > 0 && <td className="merge" rowSpan={grpSpan[i]}>{r.matGroup || '—'}</td>}
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

/** 세정 이력 조회 조건 — 기존 MES 화면의 칸 그대로(서버 SQL 에서 "포함" 검색) */
const HIST_FIELDS = [
  ['line', 'LINE'], ['matType', 'MAT_TYPE'], ['matId', 'MAT_ID·품명'], ['sn', 'S/N'], ['proc', 'PROCESS'],
  ['outNo', 'OUT NO'], ['eq', '설비명'], ['circle', '분임조'], ['lotId', 'LOT ID'],
] as const;
type HistField = typeof HIST_FIELDS[number][0];

function HistoryTab({ scope }: { scope: Scope }) {
  const now = new Date();
  const [from, setFrom] = useState(ymd(now));
  const [to, setTo] = useState(ymd(now));
  const [wip, setWip] = useState(true);
  const [ship, setShip] = useState(true);
  const [f, setF] = useState<Record<HistField, string>>(() => Object.fromEntries(HIST_FIELDS.map(([k]) => [k, ''])) as Record<HistField, string>);
  const [rows, setRows] = useState<LotRow[] | null>(null);
  const [truncated, setTruncated] = useState(false);
  const [err, setErr] = useState('');
  const [loading, setLoading] = useState(false);
  const cols = scope === 'other' ? [...HIST_COLS.slice(0, 6), ['customer', '고객사', 14] as LotCol, ...HIST_COLS.slice(6)] : HIST_COLS;

  async function load() {
    setLoading(true); setErr('');
    try {
      const p = new URLSearchParams({ from, to, wip: String(wip), ship: String(ship) });
      HIST_FIELDS.forEach(([k]) => { if (f[k].trim()) p.set(k, f[k].trim()); });
      const r = await api.get<{ truncated: boolean; rows: LotRow[] }>(`/api/mes-report/lots?${p}`);
      setRows(r.rows.filter(l => inScope(l, scope))); setTruncated(r.truncated);
    } catch (e) { setErr(e instanceof Error ? e.message : '불러오지 못했습니다.'); }
    finally { setLoading(false); }
  }
  useEffect(() => { void load(); }, []);   // eslint-disable-line react-hooks/exhaustive-deps
  const onEnter = (e: React.KeyboardEvent) => { if (e.key === 'Enter') void load(); };
  const reset = () => setF(Object.fromEntries(HIST_FIELDS.map(([k]) => [k, ''])) as Record<HistField, string>);

  return (
    <>
      <div className="mr-hist-filters">
        <div className="mr-filters">
          <span className="mr-dim">입고일</span>
          <input type="date" className="input" value={from} onChange={e => setFrom(e.target.value)} aria-label="입고일 시작" />
          <span>~</span>
          <input type="date" className="input" value={to} onChange={e => setTo(e.target.value)} aria-label="입고일 끝" />
          <label className="mr-chk"><input type="checkbox" checked={wip} onChange={e => setWip(e.target.checked)} /> WIP <span className="mr-dim">(공장 안)</span></label>
          <label className="mr-chk"><input type="checkbox" checked={ship} onChange={e => setShip(e.target.checked)} /> SHIP <span className="mr-dim">(출하)</span></label>
          <button type="button" className="btn btn-primary" onClick={() => void load()} disabled={loading}>{loading ? '조회 중…' : '조회'}</button>
          <button type="button" className="btn btn-ghost" onClick={reset}>조건 지우기</button>
          {rows && rows.length > 0 && <button type="button" className="btn btn-ghost" onClick={() => void downloadLots(`세정이력_${from}_${to}.xlsx`, rows, cols)}>Excel</button>}
          {rows && <span className="mr-dim">{rows.length.toLocaleString()} LOT{truncated ? ' (최대 2만 건까지 — 기간이나 조건을 줄이세요)' : ''}</span>}
        </div>
        <div className="mr-filters mr-hist-fields">
          {HIST_FIELDS.map(([k, label]) => (
            <label key={k}><span>{label}</span>
              <input className="input" value={f[k]} onChange={e => setF(x => ({ ...x, [k]: e.target.value }))} onKeyDown={onEnter} />
            </label>
          ))}
        </div>
        <div className="mr-dim">기간만으로는 1년까지, 조건을 하나라도 넣으면 3년까지 조회됩니다. 조건은 모두 '포함' 검색입니다.</div>
      </div>
      {err && <div className="mr-err">{err}</div>}
      {rows && <div className="mr-card mr-hist"><LotTable rows={rows} cols={cols} numbered /></div>}
    </>
  );
}
