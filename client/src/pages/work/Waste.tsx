import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { api } from '../../api/client';
import './Work.css';
import { useAccess } from '../../auth/useAccess';
import { useIsMobile } from '../../hooks/useIsMobile';
import type { WasteLog, WasteMonth, WasteTrendPoint, WorkEquipment } from '../../api/types';
import { sortByLine } from './common';
import { parseWasteWorkbook } from './wasteImport';
import WasteTrend from './WasteTrend';

// KOH(가성소다)·폐액 현황 — 엑셀 "가성소다, 폐액 증가량 및 약액 교체 현황" 을 옮긴 화면.
// PC 표는 날짜가 칸(가로), 항목(KOH·폐액 前/現/감소·증가, 교체 설비, 비고)이 줄 — 주간·야간 묶음으로 반복.
// 감소량(KOH 前−現)·증가량(폐액 現−前)은 자동 계산. 前 값은 바로 앞 교대의 現 값으로 채워 준다.
// Dip/Spray 교체 설비는 약액 교체 기록에서 한 번에 가져올 수 있다. '추이' 탭에서 2019년부터 월별 변화를 본다.

const DOW = ['일', '월', '화', '수', '목', '금', '토'];
const SHIFTS = ['주', '야'] as const;
const pad = (n: number) => String(n).padStart(2, '0');
const ymd = (y: number, m: number, d: number) => `${y}-${pad(m)}-${pad(d)}`;
const fmt = (v: number | null | undefined) => (v === null || v === undefined ? '' : Number(v.toFixed(3)).toLocaleString('ko-KR'));
function todayYmd() { const t = new Date(); return ymd(t.getFullYear(), t.getMonth() + 1, t.getDate()); }
function addDays(s: string, n: number) {
  const d = new Date(s + 'T00:00:00'); d.setDate(d.getDate() + n);
  return ymd(d.getFullYear(), d.getMonth() + 1, d.getDate());
}
function datesBetween(from: string, to: string): string[] {
  const out: string[] = [];
  for (let d = from; d <= to && out.length < 93; d = addDays(d, 1)) out.push(d);
  return out;
}
const md = (s: string) => `${Number(s.slice(5, 7))}/${Number(s.slice(8, 10))}`;
const dowOf = (s: string) => new Date(s + 'T00:00:00').getDay();

/** 표의 줄 — 날짜가 칸(가로), 항목이 줄(세로). 교대마다 이 줄들이 반복된다. */
type WCell = { text: ReactNode; title?: string; cls?: string };
const num = (v: number | null): WCell | null => (v === null ? null : { text: fmt(v) });
/** 감소·증가 — 줄면 사용(KOH)·늘면 증가(폐액)가 기본, 반대 방향은 보충·수거로 따로 표시(보라). */
function delta(v: number | null, kind: 'caustic' | 'waste'): WCell | null {
  if (v === null || v === 0) return null;
  if (v > 0) return { text: fmt(v), cls: kind === 'caustic' ? 'wf-neg' : 'wf-pos' };
  return { text: `${kind === 'caustic' ? '+' : '−'}${fmt(-v)}`, cls: 'wf-alt', title: `${kind === 'caustic' ? 'KOH 보충' : '폐액 수거'} ${fmt(-v)}` };
}
const txt = (s: string): WCell | null => (s ? { text: s, title: s, cls: 'wf-wtext' } : null);
/** wide = 1주 보기(칸이 넓다) — 비고를 점 대신 글자 그대로 */
const WROWS: { key: string; label: string; first?: boolean; cell: (r: WasteLog, wide?: boolean) => WCell | null }[] = [
  { key: 'cb', label: 'KOH 前', first: true, cell: r => num(r.causticBefore) },
  { key: 'ca', label: 'KOH 現', cell: r => num(r.causticAfter) },
  { key: 'cu', label: 'KOH 감소', cell: r => delta(r.causticUsed, 'caustic') },
  { key: 'wb', label: '폐액 前', first: true, cell: r => num(r.wasteBefore) },
  { key: 'wa', label: '폐액 現', cell: r => num(r.wasteAfter) },
  { key: 'wi', label: '폐액 증가', cell: r => delta(r.wasteIncrease, 'waste') },
  { key: 'dip', label: 'Dip 교체', first: true, cell: r => txt(r.dipEquipment) },
  { key: 'spray', label: 'Spray 교체', cell: r => txt(r.sprayEquipment) },
  { key: 'daily', label: '일교체량', cell: r => num(r.dailyChange) },
  { key: 'note', label: '비고', cell: (r, wide) => (!r.note ? null : wide
    ? { text: r.note, title: r.note, cls: 'wf-wtext wf-wnoteful' }
    : { text: <i className="wf-dot" />, title: r.note }) },
];

export default function Waste() {
  const { canEditOffice: canEdit } = useAccess();
  const isMobile = useIsMobile();
  const now = new Date();
  // 기본은 최근 1주(오늘·전날 비교) — 한 달 전체는 '월별', 긴 흐름·원인 찾기는 '추이'
  const [tab, setTab] = useState<'week' | 'month' | 'trend'>('week');
  const [weekEnd, setWeekEnd] = useState(todayYmd());
  const [year, setYear] = useState(now.getFullYear());
  const [month, setMonth] = useState(now.getMonth() + 1);
  const [data, setData] = useState<WasteMonth | null>(null);
  const [trend, setTrend] = useState<WasteTrendPoint[] | null>(null);
  const [edit, setEdit] = useState<{ date: string; shift: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);

  const range = useMemo(() => (tab === 'week'
    ? { from: addDays(weekEnd, -6), to: weekEnd }
    : { from: ymd(year, month, 1), to: ymd(year, month, new Date(year, month, 0).getDate()) }), [tab, weekEnd, year, month]);
  const load = useCallback(async () => {
    const r = await api.get<WasteMonth>(`/api/worklog/waste/range?from=${range.from}&to=${range.to}`);
    setData(r);
  }, [range]);
  useEffect(() => { if (tab !== 'trend') { setData(null); load().catch(() => setData(null)); } }, [load, tab]);
  useEffect(() => {
    if (tab === 'trend' && !trend) api.get<WasteTrendPoint[]>('/api/worklog/waste/trend').then(setTrend).catch(() => setTrend([]));
  }, [tab, trend]);

  function shiftMonth(delta: number) {
    const d = new Date(year, month - 1 + delta, 1);
    setYear(d.getFullYear()); setMonth(d.getMonth() + 1);
  }

  const byKey = useMemo(() => {
    const m = new Map<string, WasteLog>();
    for (const r of data?.rows ?? []) m.set(`${r.date}|${r.shift}`, r);
    return m;
  }, [data]);
  const dates = useMemo(() => datesBetween(range.from, range.to), [range]);
  const wide = tab === 'week';   // 1주는 칸이 넓어 교체 설비·비고를 글자 그대로 보여 준다
  const today = todayYmd();
  const slots = dates.flatMap(date => SHIFTS.map(s => ({ date, shift: s })));

  /** 그 줄 바로 앞 줄의 現 값(前 값 채우기용) — 같은 달 안이면 앞 줄, 달 첫 줄이면 지난 달 마지막 줄. */
  function prevAfter(date: string, shift: string): { caustic: number | null; waste: number | null } {
    const idx = slots.findIndex(s => s.date === date && s.shift === shift);
    for (let i = idx - 1; i >= 0; i--) {
      const r = byKey.get(`${slots[i].date}|${slots[i].shift}`);
      if (r && (r.causticAfter !== null || r.wasteAfter !== null)) return { caustic: r.causticAfter, waste: r.wasteAfter };
    }
    return { caustic: data?.prevCausticAfter ?? null, waste: data?.prevWasteAfter ?? null };
  }

  // 줄어든 KOH = 사용, 늘어난 KOH = 보충 / 늘어난 폐액 = 발생, 줄어든 폐액 = 수거 — 서로 지워지지 않게 따로 합한다
  const pos = (v: number | null) => (v !== null && v > 0 ? v : 0);
  const neg = (v: number | null) => (v !== null && v < 0 ? -v : 0);
  const sumUsed = (data?.rows ?? []).reduce((s, r) => s + pos(r.causticUsed), 0);
  const sumRefill = (data?.rows ?? []).reduce((s, r) => s + neg(r.causticUsed), 0);
  const sumInc = (data?.rows ?? []).reduce((s, r) => s + pos(r.wasteIncrease), 0);
  const sumRemoved = (data?.rows ?? []).reduce((s, r) => s + neg(r.wasteIncrease), 0);
  const changes = (data?.rows ?? []).filter(r => r.dipEquipment || r.sprayEquipment).length;
  /** 하루(주+야) 합계 — 오늘·전날 비교용 */
  function dayTotals(date: string) {
    const rs = SHIFTS.map(s => byKey.get(`${date}|${s}`)).filter((r): r is WasteLog => !!r);
    return {
      has: rs.length > 0,
      used: rs.reduce((s, r) => s + pos(r.causticUsed), 0), refill: rs.reduce((s, r) => s + neg(r.causticUsed), 0),
      inc: rs.reduce((s, r) => s + pos(r.wasteIncrease), 0), removed: rs.reduce((s, r) => s + neg(r.wasteIncrease), 0),
      koh: [...rs].reverse().find(r => r.causticAfter !== null)?.causticAfter ?? null,
      waste: [...rs].reverse().find(r => r.wasteAfter !== null)?.wasteAfter ?? null,
      eq: rs.flatMap(r => [r.dipEquipment, r.sprayEquipment]).filter(Boolean).join(', '),
    };
  }

  async function importFile(f: File) {
    setBusy(true);
    try {
      const parsed = await parseWasteWorkbook(f);
      if (parsed.rows.length === 0) { alert('가져올 줄을 찾지 못했습니다. "(폐액)" 월별 시트가 있는 파일인지 확인하세요.'); return; }
      const overwrite = confirm(
        `폐액 시트 ${parsed.sheets}개 · ${parsed.from} ~ ${parsed.to} · ${parsed.rows.length}줄을 가져옵니다.\n\n` +
        '이미 웹에 적힌 줄은 엑셀 내용으로 덮어쓸까요?\n[확인] 덮어쓰기   [취소] 비어 있는 줄만 채우기');
      const r = await api.post<{ added: number; updated: number; skipped: number }>('/api/worklog/waste/import', { rows: parsed.rows, overwrite });
      alert(`새로 ${r.added}줄 · 고침 ${r.updated}줄 · 건너뜀 ${r.skipped}줄`);
      setTrend(null);
      await load();
    } catch (e) {
      alert(e instanceof Error ? e.message : '엑셀을 가져오지 못했습니다.');
    } finally { setBusy(false); }
  }

  return (
    <div className="wf-page">
      <header className="pg-header">
        <div>
          <h2>KOH·폐액 현황</h2>
          <p>최근 1주(오늘·전날 비교) · 월별 기록 · 2019년부터 추이와 원인 찾기</p>
        </div>
        {canEdit && <button className="btn btn-ghost" disabled={busy} onClick={() => fileRef.current?.click()}>{busy ? '가져오는 중…' : '엑셀 가져오기'}</button>}
        <input ref={fileRef} type="file" accept=".xlsx" hidden onChange={e => { const f = e.target.files?.[0]; if (f) void importFile(f); e.target.value = ''; }} />
      </header>
      <div className="pg-body">
        <div className="wf-tabs">
          <button className={tab === 'week' ? 'on' : ''} onClick={() => setTab('week')}>최근 1주</button>
          <button className={tab === 'month' ? 'on' : ''} onClick={() => setTab('month')}>월별 기록</button>
          <button className={tab === 'trend' ? 'on' : ''} onClick={() => setTab('trend')}>추이</button>
        </div>

        {tab === 'trend' ? <WasteTrend points={trend} /> : (<>
          <div className="wf-toolbar">
            {tab === 'week' ? (<>
              <div className="wf-monthnav">
                <button onClick={() => setWeekEnd(addDays(weekEnd, -7))} aria-label="이전 주">‹</button>
                <input className="input wf-date" type="date" value={weekEnd} onChange={e => e.target.value && setWeekEnd(e.target.value)} />
                <button onClick={() => setWeekEnd(addDays(weekEnd, 7))} aria-label="다음 주">›</button>
              </div>
              <b className="wf-daylabel">{md(range.from)} ~ {md(range.to)}</b>
              {weekEnd !== today && <button className="btn btn-ghost wf-sm" onClick={() => setWeekEnd(today)}>오늘</button>}
            </>) : (
              <div className="wf-monthnav">
                <button onClick={() => shiftMonth(-1)} aria-label="이전 달">‹</button>
                <b>{year}년 {month}월</b>
                <button onClick={() => shiftMonth(1)} aria-label="다음 달">›</button>
              </div>
            )}
            <span className="wf-stat">KOH 사용 <b>{fmt(sumUsed)}</b>{sumRefill > 0 && <> · 보충 <b>{fmt(sumRefill)}</b></>}</span>
            <span className="wf-stat">폐액 증가 <b>{fmt(sumInc)}</b>{sumRemoved > 0 && <> · 수거 <b>{fmt(sumRemoved)}</b></>}</span>
            <span className="wf-stat">약액 교체 <b>{changes}</b>회</span>
          </div>

          {tab === 'week' && data && <DayCompare cur={{ date: weekEnd, ...dayTotals(weekEnd) }} prev={{ date: addDays(weekEnd, -1), ...dayTotals(addDays(weekEnd, -1)) }} />}

          {!data ? <div className="wf-empty">불러오는 중…</div> : isMobile ? (
            <div className="wf-mlist">
              {[...dates].reverse().map(date => {
                const dow = dowOf(date);
                const rs = SHIFTS.map(s => ({ s, r: byKey.get(`${date}|${s}`) }));
                if (!rs.some(x => x.r) && date !== today && tab !== 'week') return null;
                return (
                  <div key={date} className={`wf-mcard ${date === today ? 'today' : ''}`}>
                    <div className={`wf-mday dow${dow}`}>{md(date)} ({DOW[dow]})</div>
                    {rs.map(({ s, r }) => (
                      <button key={s} className="wf-wrow" onClick={() => canEdit && setEdit({ date, shift: s })}>
                        <b className={`wf-shift s${s}`}>{s}</b>
                        {r ? <>
                          <span>KOH {fmt(r.causticBefore)}→{fmt(r.causticAfter)} <Delta v={r.causticUsed} kind="caustic" /></span>
                          <span>폐액 {fmt(r.wasteBefore)}→{fmt(r.wasteAfter)} <Delta v={r.wasteIncrease} kind="waste" /></span>
                          {(r.dipEquipment || r.sprayEquipment) && <span className="wf-wchg">교체 {[r.dipEquipment, r.sprayEquipment].filter(Boolean).join(' · ')}</span>}
                          {r.note && <span className="wf-wchg wf-wnotem">{r.note}</span>}
                        </> : <span className="wf-dim">기록 없음{canEdit ? ' — 눌러서 입력' : ''}</span>}
                      </button>
                    ))}
                  </div>
                );
              })}
            </div>
          ) : (
            <div className="wf-gridwrap">
              <table className={`wf-tgrid wf-koh ${wide ? 'wide' : ''}`}>
                <thead>
                  <tr>
                    <th className="wf-tname">구분</th>
                    {dates.map(date => {
                      const dow = dowOf(date);
                      return <th key={date} className={`wf-tday dow${dow} ${date === today ? 'today' : ''}`}>{wide ? md(date) : Number(date.slice(8))}<small>{DOW[dow]}</small></th>;
                    })}
                  </tr>
                </thead>
                <tbody>
                  {SHIFTS.map(shift => [
                    <tr key={`h-${shift}`} className="wf-tline"><td className={`wf-tname wf-shift s${shift}`}>{shift === '주' ? '주간' : '야간'}</td><td colSpan={dates.length} /></tr>,
                    ...WROWS.map(row => (
                      <tr key={`${shift}-${row.key}`} className={row.first ? 'wf-wfirst' : ''}>
                        <td className="wf-tname">{row.label}</td>
                        {dates.map(date => {
                          const d = date;
                          const r = byKey.get(`${date}|${shift}`);
                          const cell = r ? row.cell(r, wide) : null;
                          return (
                            <td key={d} className={`wf-tcell ${date === today ? 'today' : ''} ${canEdit ? 'editable' : ''} ${cell?.cls ?? ''}`}
                              title={cell?.title ?? `${md(date)} ${shift === '주' ? '주간' : '야간'}`}
                              onClick={() => canEdit && setEdit({ date, shift })}>
                              {cell?.text}
                            </td>
                          );
                        })}
                      </tr>
                    )),
                  ])}
                </tbody>
              </table>
            </div>
          )}
        </>)}
      </div>

      {edit && (
        <WasteEditor date={edit.date} shift={edit.shift} row={byKey.get(`${edit.date}|${edit.shift}`)}
          prev={prevAfter(edit.date, edit.shift)} chem={data?.chemicalByDate[edit.date] ?? []}
          onClose={() => setEdit(null)} onSaved={async () => { setEdit(null); setTrend(null); await load(); }} />
      )}
    </div>
  );
}

type DayT = { date: string; has: boolean; used: number; refill: number; inc: number; removed: number; koh: number | null; waste: number | null; eq: string };

/**
 * 오늘·전날 비교 — 하루(주+야) 합계. 전날보다 늘면 ▲, 줄면 ▼ 와 차이를 붙인다.
 * 사용량·증가량이 크게 달라지면 원인(교체 설비)을 바로 옆에서 보도록 교체 설비도 같이 둔다.
 */
function DayCompare({ cur, prev }: { cur: DayT; prev: DayT }) {
  const diff = (a: number, b: number) => {
    const d = Number((a - b).toFixed(3));
    if (!cur.has || !prev.has || d === 0) return null;
    return <em className={d > 0 ? 'up' : 'down'}>{d > 0 ? '▲' : '▼'} {fmt(Math.abs(d))}</em>;
  };
  const col = (t: DayT, label: string) => (
    <div className="wf-cmp-day">
      <span className="wf-cmp-date">{label} <small>{md(t.date)} ({DOW[dowOf(t.date)]})</small></span>
      {!t.has ? <span className="wf-dim">기록 없음</span> : (<>
        <span>KOH 사용 <b>{fmt(t.used)}</b>{t.refill > 0 && <i> · 보충 {fmt(t.refill)}</i>}</span>
        <span>폐액 증가 <b>{fmt(t.inc)}</b>{t.removed > 0 && <i> · 수거 {fmt(t.removed)}</i>}</span>
        <span className="wf-dim">잔량 KOH {fmt(t.koh)} · 폐액 {fmt(t.waste)}</span>
        <span className="wf-dim">교체 {t.eq || '없음'}</span>
      </>)}
    </div>
  );
  return (
    <div className="wf-cmp">
      {col(prev, '전날')}
      <div className="wf-cmp-diff">
        <span>KOH 사용 {diff(cur.used, prev.used) ?? <i className="wf-dim">-</i>}</span>
        <span>폐액 증가 {diff(cur.inc, prev.inc) ?? <i className="wf-dim">-</i>}</span>
      </div>
      {col(cur, '오늘')}
    </div>
  );
}

/** 변화량 표시 — KOH 는 줄면 사용, 늘면 '보충' / 폐액은 늘면 증가, 줄면 '수거'. */
function Delta({ v, kind }: { v: number | null; kind: 'caustic' | 'waste' }) {
  if (v === null || v === 0) return null;
  if (v > 0) return <em className={kind === 'caustic' ? 'wf-neg' : 'wf-pos'}>{fmt(v)}</em>;
  return <em className="wf-alt">{kind === 'caustic' ? '보충' : '수거'} {fmt(-v)}</em>;
}

function WasteEditor({ date, shift, row, prev, chem, onClose, onSaved }: {
  date: string; shift: string; row?: WasteLog; prev: { caustic: number | null; waste: number | null }; chem: string[];
  onClose: () => void; onSaved: () => Promise<void>;
}) {
  const s = (v: number | null | undefined, fallback: number | null = null) => (v ?? fallback) === null ? '' : String(v ?? fallback);
  // 새 줄이면 前 값을 앞 줄의 現 값으로 채워 둔다(엑셀에서도 그렇게 이어 적었다)
  const [f, setF] = useState({
    cb: s(row?.causticBefore, row ? null : prev.caustic), ca: s(row?.causticAfter),
    wb: s(row?.wasteBefore, row ? null : prev.waste), wa: s(row?.wasteAfter),
    daily: s(row?.dailyChange), note: row?.note ?? '',
  });
  const [dip, setDip] = useState<string[]>(() => splitEq(row?.dipEquipment));
  const [spray, setSpray] = useState<string[]>(() => splitEq(row?.sprayEquipment));
  const eqs = useWashEquipment();
  // Dip = DC 설비(MDC·NDC), Spray = SC 설비(MSC·NSC)
  const dipOpts = eqs.filter(c => !/SC/.test(c));
  const sprayOpts = eqs.filter(c => /SC/.test(c));
  const [saving, setSaving] = useState(false);
  const n = (t: string) => (t.trim() === '' ? null : Number(t));
  const bad = [f.cb, f.ca, f.wb, f.wa, f.daily].some(t => t.trim() !== '' && !Number.isFinite(Number(t)));
  const used = n(f.cb) !== null && n(f.ca) !== null ? n(f.cb)! - n(f.ca)! : null;
  const inc = n(f.wb) !== null && n(f.wa) !== null ? n(f.wa)! - n(f.wb)! : null;
  // 약액 교체 기록의 그날 설비 — DC 는 Dip, SC 는 Spray
  const chemDip = chem.filter(c => /DC/.test(c)); const chemSpray = chem.filter(c => /SC/.test(c));

  async function save(clear = false) {
    if (clear && !confirm('이 줄을 지울까요?')) return;
    if (!clear && bad) { alert('숫자 칸에 숫자가 아닌 값이 있습니다.'); return; }
    setSaving(true);
    try {
      await api.put('/api/worklog/waste', clear
        ? { date, shift }
        : { date, shift, causticBefore: n(f.cb), causticAfter: n(f.ca), wasteBefore: n(f.wb), wasteAfter: n(f.wa),
            dipEquipment: joinEq(dip, dipOpts), sprayEquipment: joinEq(spray, sprayOpts), dailyChange: n(f.daily), note: f.note });
      await onSaved();
    } catch (e) {
      alert(e instanceof Error ? e.message : '저장하지 못했습니다.');
    } finally { setSaving(false); }
  }
  const inp = (k: keyof typeof f, ph = '') => (
    <input className="input" inputMode="decimal" value={f[k]} placeholder={ph} onChange={e => setF({ ...f, [k]: e.target.value })} />
  );
  const d = new Date(date + 'T00:00:00');
  return (
    <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="modal-box wf-edit wf-kedit">
        <h3>{d.getMonth() + 1}/{d.getDate()} ({DOW[d.getDay()]}) · {shift === '주' ? '주간' : '야간'}</h3>
        <div className="wf-wgrid">
          <span className="wf-wlbl">KOH</span>
          <label>前{inp('cb')}</label><label>現{inp('ca')}</label>
          <span className="wf-wcalc">{used !== null && used < 0 ? '보충' : '감소'} <b>{used === null ? '-' : Math.abs(Number(used.toFixed(3)))}</b></span>
          <span className="wf-wlbl">폐액</span>
          <label>前{inp('wb')}</label><label>現{inp('wa')}</label>
          <span className="wf-wcalc">{inc !== null && inc < 0 ? '수거' : '증가'} <b>{inc === null ? '-' : Math.abs(Number(inc.toFixed(3)))}</b></span>
        </div>
        {/* 교체 설비는 설비 목록에서 눌러 고른다(글자로 치지 않게 — 이름이 제각각이 되지 않도록) */}
        <div className="wf-edit-row">
          {/* label 로 감싸면 목록 안을 누를 때 버튼이 눌려 목록이 닫힌다 — div 로 둔다 */}
          <div className="wf-ddfield"><span>Dip 교체 설비</span><EqPicker options={dipOpts} value={dip} onChange={setDip} placeholder="설비 선택" /></div>
          <div className="wf-ddfield"><span>Spray 교체 설비</span><EqPicker options={sprayOpts} value={spray} onChange={setSpray} placeholder="설비 선택" /></div>
        </div>
        {chem.length > 0 && (
          <button type="button" className="wf-link" onClick={() => { setDip(chemDip); setSpray(chemSpray); }}>
            약액 교체 기록에서 채우기: {chem.join(', ')}
          </button>
        )}
        <div className="wf-edit-row wf-kbottom">
          <label className="wf-kdaily">일교체량{inp('daily')}</label>
          <label className="wf-knote">비고<textarea className="input wf-note" rows={3} value={f.note} onChange={e => setF({ ...f, note: e.target.value })} /></label>
        </div>
        <div className="modal-actions">
          {row && <button className="btn btn-ghost wf-danger" disabled={saving} onClick={() => save(true)}>지우기</button>}
          <span style={{ flex: 1 }} />
          <button className="btn btn-ghost" onClick={onClose}>취소</button>
          <button className="btn btn-primary" disabled={saving} onClick={() => save()}>{saving ? '저장 중…' : '저장'}</button>
        </div>
      </div>
    </div>
  );
}

/** "MDC03 WDC04" · "NDC02, MDC01" → ["MDC03", "WDC04"] (엑셀에서 가져온 옛 기록은 띄어쓰기로도 나뉘어 있다) */
function splitEq(v?: string): string[] {
  return [...new Set((v ?? '').split(/[,\s/·]+/).map(x => x.trim()).filter(Boolean))];
}
/** 설비 목록 순서대로 이어 붙인다(목록에 없는 옛 이름은 뒤에). */
function joinEq(sel: string[], order: string[]): string {
  return [...order.filter(c => sel.includes(c)), ...sel.filter(c => !order.includes(c))].join(', ');
}

let eqCache: Promise<string[]> | null = null;
/** 약액을 쓰는 세정 설비 코드(설비 목록에서 켠 것, 라인 순). */
function useWashEquipment(): string[] {
  const [list, setList] = useState<string[]>([]);
  useEffect(() => {
    eqCache ??= api.get<WorkEquipment[]>('/api/worklog/equipment')
      .then(all => sortByLine(all.filter(e => e.isActive && e.kind !== 'BAKE')).map(e => e.code))
      .catch(() => { eqCache = null; return []; });
    let alive = true;
    eqCache.then(l => { if (alive) setList(l); });
    return () => { alive = false; };
  }, []);
  return list;
}

/**
 * 설비 고르기 — 칸을 누르면 설비 목록이 아래로 펼쳐지고, 체크해서 고른다(여러 대 가능, 글자로 칠 수 없음).
 * 목록에 없는 옛 이름(엑셀에서 가져온 WDC04 등)은 체크된 채 목록 끝에 보이고, 풀면 빠진다.
 * 편집 창이 스크롤되는 상자라 목록은 화면 기준(fixed)으로 띄운다 — 창 안에 갇혀 잘리지 않게.
 */
function EqPicker({ options, value, onChange, placeholder }: { options: string[]; value: string[]; onChange: (v: string[]) => void; placeholder: string }) {
  const [pos, setPos] = useState<{ left: number; width: number; top?: number; bottom?: number } | null>(null);
  const btn = useRef<HTMLButtonElement>(null);
  const pop = useRef<HTMLDivElement>(null);
  const legacy = value.filter(v => !options.includes(v));
  const all = [...options, ...legacy];
  const toggle = (c: string) => onChange(value.includes(c) ? value.filter(x => x !== c) : [...value, c]);

  function open() {
    const r = btn.current!.getBoundingClientRect();
    const below = window.innerHeight - r.bottom;
    setPos(below < 260 && r.top > below
      ? { left: r.left, width: r.width, bottom: window.innerHeight - r.top + 4 }
      : { left: r.left, width: r.width, top: r.bottom + 4 });
  }
  useEffect(() => {
    if (!pos) return;
    const close = (e: Event) => {
      const t = e.target as Node;
      if (e.type === 'mousedown' && (pop.current?.contains(t) || btn.current?.contains(t))) return;
      if (e.type === 'scroll' && pop.current?.contains(t)) return;
      setPos(null);
    };
    document.addEventListener('mousedown', close);
    window.addEventListener('scroll', close, true);
    window.addEventListener('resize', close);
    return () => { document.removeEventListener('mousedown', close); window.removeEventListener('scroll', close, true); window.removeEventListener('resize', close); };
  }, [pos]);

  const shown = [...options.filter(c => value.includes(c)), ...legacy];
  return (
    <div className="wf-dd">
      <button ref={btn} type="button" className={`input wf-dd-btn ${pos ? 'open' : ''}`} onClick={() => (pos ? setPos(null) : open())}>
        <span className={shown.length ? '' : 'wf-dim'}>{shown.length ? shown.join(', ') : placeholder}</span>
        <i>▾</i>
      </button>
      {pos && (
        <div ref={pop} className="wf-dd-pop" style={{ position: 'fixed', left: pos.left, width: pos.width, top: pos.top, bottom: pos.bottom }}>
          {value.length > 0 && <button type="button" className="wf-dd-clear" onClick={() => onChange([])}>선택 해제</button>}
          {all.map(c => (
            <label key={c} className={`wf-dd-item ${value.includes(c) ? 'on' : ''}`} title={legacy.includes(c) ? '설비 목록에 없는 이름(예전 기록) — 풀면 빠집니다' : undefined}>
              <input type="checkbox" checked={value.includes(c)} onChange={() => toggle(c)} />
              <span>{c}</span>
              {legacy.includes(c) && <em>옛 기록</em>}
            </label>
          ))}
          {all.length === 0 && <div className="wf-dd-none">설비 목록을 불러오는 중…</div>}
        </div>
      )}
    </div>
  );
}
