import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { api } from '../../api/client';
import './Work.css';
import { useAccess } from '../../auth/useAccess';
import { useIsMobile } from '../../hooks/useIsMobile';
import type { WasteLog, WasteMonth, WasteTrendPoint } from '../../api/types';
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
const WROWS: { key: string; label: string; first?: boolean; cell: (r: WasteLog) => WCell | null }[] = [
  { key: 'cb', label: 'KOH 前', first: true, cell: r => num(r.causticBefore) },
  { key: 'ca', label: 'KOH 現', cell: r => num(r.causticAfter) },
  { key: 'cu', label: 'KOH 감소', cell: r => delta(r.causticUsed, 'caustic') },
  { key: 'wb', label: '폐액 前', first: true, cell: r => num(r.wasteBefore) },
  { key: 'wa', label: '폐액 現', cell: r => num(r.wasteAfter) },
  { key: 'wi', label: '폐액 증가', cell: r => delta(r.wasteIncrease, 'waste') },
  { key: 'dip', label: 'Dip 교체', first: true, cell: r => txt(r.dipEquipment) },
  { key: 'spray', label: 'Spray 교체', cell: r => txt(r.sprayEquipment) },
  { key: 'daily', label: '일교체량', cell: r => num(r.dailyChange) },
  { key: 'note', label: '비고', cell: r => (r.note ? { text: <i className="wf-dot" />, title: r.note } : null) },
];

export default function Waste() {
  const { canEditOffice: canEdit } = useAccess();
  const isMobile = useIsMobile();
  const now = new Date();
  const [tab, setTab] = useState<'month' | 'trend'>('month');
  const [year, setYear] = useState(now.getFullYear());
  const [month, setMonth] = useState(now.getMonth() + 1);
  const [data, setData] = useState<WasteMonth | null>(null);
  const [trend, setTrend] = useState<WasteTrendPoint[] | null>(null);
  const [edit, setEdit] = useState<{ date: string; shift: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);

  const load = useCallback(async () => {
    setData(await api.get<WasteMonth>(`/api/worklog/waste?year=${year}&month=${month}`));
  }, [year, month]);
  useEffect(() => { load().catch(() => setData(null)); }, [load]);
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
  const days = new Date(year, month, 0).getDate();
  const today = todayYmd();
  const slots = Array.from({ length: days }, (_, i) => i + 1).flatMap(d => SHIFTS.map(s => ({ date: ymd(year, month, d), d, shift: s })));

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
          <p>하루 주·야 두 줄 · 감소량/증가량 자동 계산 · 2019년부터 추이</p>
        </div>
        {canEdit && <button className="btn btn-ghost" disabled={busy} onClick={() => fileRef.current?.click()}>{busy ? '가져오는 중…' : '엑셀 가져오기'}</button>}
        <input ref={fileRef} type="file" accept=".xlsx" hidden onChange={e => { const f = e.target.files?.[0]; if (f) void importFile(f); e.target.value = ''; }} />
      </header>
      <div className="pg-body">
        <div className="wf-tabs">
          <button className={tab === 'month' ? 'on' : ''} onClick={() => setTab('month')}>월별 기록</button>
          <button className={tab === 'trend' ? 'on' : ''} onClick={() => setTab('trend')}>추이</button>
        </div>

        {tab === 'trend' ? <WasteTrend points={trend} /> : (<>
          <div className="wf-toolbar">
            <div className="wf-monthnav">
              <button onClick={() => shiftMonth(-1)} aria-label="이전 달">‹</button>
              <b>{year}년 {month}월</b>
              <button onClick={() => shiftMonth(1)} aria-label="다음 달">›</button>
            </div>
            <span className="wf-stat">KOH 사용 <b>{fmt(sumUsed)}</b>{sumRefill > 0 && <> · 보충 <b>{fmt(sumRefill)}</b></>}</span>
            <span className="wf-stat">폐액 증가 <b>{fmt(sumInc)}</b>{sumRemoved > 0 && <> · 수거 <b>{fmt(sumRemoved)}</b></>}</span>
            <span className="wf-stat">약액 교체 <b>{changes}</b>회</span>
          </div>

          {!data ? <div className="wf-empty">불러오는 중…</div> : isMobile ? (
            <div className="wf-mlist">
              {Array.from({ length: days }, (_, i) => days - i).map(d => {
                const date = ymd(year, month, d);
                const dow = new Date(year, month - 1, d).getDay();
                const rs = SHIFTS.map(s => ({ s, r: byKey.get(`${date}|${s}`) }));
                if (!rs.some(x => x.r) && date !== today) return null;
                return (
                  <div key={d} className={`wf-mcard ${date === today ? 'today' : ''}`}>
                    <div className={`wf-mday dow${dow}`}>{month}/{d} ({DOW[dow]})</div>
                    {rs.map(({ s, r }) => (
                      <button key={s} className="wf-wrow" onClick={() => canEdit && setEdit({ date, shift: s })}>
                        <b className={`wf-shift s${s}`}>{s}</b>
                        {r ? <>
                          <span>KOH {fmt(r.causticBefore)}→{fmt(r.causticAfter)} <Delta v={r.causticUsed} kind="caustic" /></span>
                          <span>폐액 {fmt(r.wasteBefore)}→{fmt(r.wasteAfter)} <Delta v={r.wasteIncrease} kind="waste" /></span>
                          {(r.dipEquipment || r.sprayEquipment) && <span className="wf-wchg">교체 {[r.dipEquipment, r.sprayEquipment].filter(Boolean).join(' · ')}</span>}
                        </> : <span className="wf-dim">기록 없음{canEdit ? ' — 눌러서 입력' : ''}</span>}
                      </button>
                    ))}
                  </div>
                );
              })}
            </div>
          ) : (
            <div className="wf-gridwrap">
              <table className="wf-tgrid wf-koh">
                <thead>
                  <tr>
                    <th className="wf-tname">구분</th>
                    {Array.from({ length: days }, (_, i) => i + 1).map(d => {
                      const date = ymd(year, month, d);
                      const dow = new Date(year, month - 1, d).getDay();
                      return <th key={d} className={`wf-tday dow${dow} ${date === today ? 'today' : ''}`}>{d}<small>{DOW[dow]}</small></th>;
                    })}
                  </tr>
                </thead>
                <tbody>
                  {SHIFTS.map(shift => [
                    <tr key={`h-${shift}`} className="wf-tline"><td className={`wf-tname wf-shift s${shift}`}>{shift === '주' ? '주간' : '야간'}</td><td colSpan={days} /></tr>,
                    ...WROWS.map(row => (
                      <tr key={`${shift}-${row.key}`} className={row.first ? 'wf-wfirst' : ''}>
                        <td className="wf-tname">{row.label}</td>
                        {Array.from({ length: days }, (_, i) => i + 1).map(d => {
                          const date = ymd(year, month, d);
                          const r = byKey.get(`${date}|${shift}`);
                          const cell = r ? row.cell(r) : null;
                          return (
                            <td key={d} className={`wf-tcell ${date === today ? 'today' : ''} ${canEdit ? 'editable' : ''} ${cell?.cls ?? ''}`}
                              title={cell?.title ?? `${month}/${d} ${shift === '주' ? '주간' : '야간'}`}
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
    dip: row?.dipEquipment ?? '', spray: row?.sprayEquipment ?? '', daily: s(row?.dailyChange), note: row?.note ?? '',
  });
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
            dipEquipment: f.dip, sprayEquipment: f.spray, dailyChange: n(f.daily), note: f.note });
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
      <div className="modal-box wf-edit">
        <h3>{d.getMonth() + 1}/{d.getDate()} ({DOW[d.getDay()]}) · {shift === '주' ? '주간' : '야간'}</h3>
        <div className="wf-wgrid">
          <span className="wf-wlbl">KOH</span>
          <label>前{inp('cb')}</label><label>現{inp('ca')}</label>
          <span className="wf-wcalc">{used !== null && used < 0 ? '보충' : '감소'} <b>{used === null ? '-' : Math.abs(Number(used.toFixed(3)))}</b></span>
          <span className="wf-wlbl">폐액</span>
          <label>前{inp('wb')}</label><label>現{inp('wa')}</label>
          <span className="wf-wcalc">{inc !== null && inc < 0 ? '수거' : '증가'} <b>{inc === null ? '-' : Math.abs(Number(inc.toFixed(3)))}</b></span>
        </div>
        <div className="wf-edit-row">
          <label>Dip 교체 설비<input className="input" value={f.dip} placeholder="예: NDC02, MDC01" onChange={e => setF({ ...f, dip: e.target.value })} /></label>
          <label>Spray 교체 설비<input className="input" value={f.spray} placeholder="예: NSC01-1" onChange={e => setF({ ...f, spray: e.target.value })} /></label>
        </div>
        {chem.length > 0 && (
          <button type="button" className="wf-link" onClick={() => setF({ ...f, dip: chemDip.join(', '), spray: chemSpray.join(', ') })}>
            약액 교체 기록에서 채우기: {chem.join(', ')}
          </button>
        )}
        <div className="wf-edit-row">
          <label>일교체량{inp('daily')}</label>
          <label style={{ flex: 2 }}>비고<input className="input" value={f.note} onChange={e => setF({ ...f, note: e.target.value })} /></label>
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
