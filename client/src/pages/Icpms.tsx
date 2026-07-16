import { useEffect, useMemo, useState, useCallback, useRef } from 'react';
import { api } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { ICP_ELEMENTS } from '../api/types';
import type { IcpmsEquipment, IcpmsMeasurement, IcpmsHistory, IcpmsActionLog } from '../api/types';
import { parseIcpmsUpload, exportIcpms, downloadIcpmsSample } from './icpmsExcel';
import './Icpms.css';

const ELS = ICP_ELEMENTS as readonly string[];
const elColor = (el: string) => `hsl(${(ELS.indexOf(el) * 360) / ELS.length}, 62%, 48%)`;
const fmt = (n: number) => n >= 1000 ? Math.round(n).toLocaleString() : Number(n.toFixed(2)).toLocaleString();

type Mode = 'compare' | 'trend' | 'notes';
type PeriodUnit = '일별' | '월별' | '년별';

// 분석일 → 기간 키 (WPF PeriodKey)
const periodKey = (date: string, unit: PeriodUnit) =>
  unit === '년별' ? date.slice(0, 4) : unit === '월별' ? date.slice(0, 7) : date;

// 점검일지 편집 행
interface NoteRow {
  origEqId: string; eqId: string; origProcess: string; process: string;
  measured: boolean; summary: string; origNote: string; note: string;
}

export default function Icpms() {
  const { user } = useAuth();
  const isMaster = !!user?.isAdmin;
  const [mode, setMode] = useState<Mode>('compare');

  // WPF와 동일: 전체 데이터를 받아 클라이언트에서 필터
  const [all, setAll] = useState<IcpmsMeasurement[]>([]);
  const [equip, setEquip] = useState<IcpmsEquipment[]>([]);
  const [selPt, setSelPt] = useState<string[]>([]);
  const [selBath, setSelBath] = useState<string[]>([]);
  const [selEq, setSelEq] = useState<string[]>([]);
  const [selDates, setSelDates] = useState<string[]>([]);
  const [selEls, setSelEls] = useState<string[]>([...ELS]);
  const [unit, setUnit] = useState<PeriodUnit>('월별');
  const [chartOpen, setChartOpen] = useState(true);
  const [tableOpen, setTableOpen] = useState(true);

  const [noteDate, setNoteDate] = useState('');
  const [noteRows, setNoteRows] = useState<NoteRow[]>([]);
  const [newEqName, setNewEqName] = useState('');
  const [history, setHistory] = useState<{ eqId: string; rows: IcpmsHistory[] } | null>(null);
  const [logs, setLogs] = useState<IcpmsActionLog[] | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);
  const firstLoad = useRef(true);

  const procMap = useMemo(() => new Map(equip.map(e => [e.eqId, e.process])), [equip]);
  const eqLabel = (eq: string) => { const p = procMap.get(eq); return p ? `${eq} (${p})` : eq; };

  const loadAll = useCallback(async () => {
    const [ms, eqs] = await Promise.all([
      api.get<IcpmsMeasurement[]>('/api/icpms/measurements'),
      api.get<IcpmsEquipment[]>('/api/icpms/equipment'),
    ]);
    setAll(ms); setEquip(eqs);
    if (firstLoad.current) {
      const latest = ms.map(m => m.analysisDate).filter(Boolean).sort().pop();
      if (latest) { setSelDates([latest]); setNoteDate(latest); }
      firstLoad.current = false;
    }
  }, []);
  useEffect(() => { loadAll().catch(() => {}); }, [loadAll]);

  // 종속 필터 옵션: 설비유형 선택 시 나머지 옵션이 그 유형 데이터로 좁혀짐
  const ptOpts = useMemo(() => [...new Set(all.map(m => m.processType).filter(Boolean))].sort(), [all]);
  const scoped = useMemo(() => selPt.length ? all.filter(m => selPt.includes(m.processType)) : all, [all, selPt]);
  const bathOpts = useMemo(() => [...new Set(scoped.map(m => m.bathGb).filter(Boolean))].sort(), [scoped]);
  const eqOpts = useMemo(() => [...new Set(scoped.map(m => m.eqId).filter(Boolean))].sort(), [scoped]);
  const dateOpts = useMemo(() => [...new Set(scoped.map(m => m.analysisDate).filter(Boolean))].sort().reverse(), [scoped]);
  const allDates = useMemo(() => [...new Set(all.map(m => m.analysisDate).filter(Boolean))].sort().reverse(), [all]);
  // 필터가 좁아져 데이터 없는 날짜가 선택에 남으면 해제 (WPF와 동일)
  useEffect(() => { setSelDates(p => p.filter(d => dateOpts.includes(d))); }, [dateOpts]);

  // 필터 적용 (빈 선택 = 전체)
  const rows = useMemo(() => {
    let q = all.filter(m => m.eqId);
    if (selPt.length) q = q.filter(m => selPt.includes(m.processType));
    if (selBath.length) q = q.filter(m => selBath.includes(m.bathGb));
    if (selEq.length) q = q.filter(m => selEq.includes(m.eqId));
    if (selDates.length) q = q.filter(m => selDates.includes(m.analysisDate));
    return q;
  }, [all, selPt, selBath, selEq, selDates]);

  // 요약 카드 (WPF UpdateStats)
  const stats = useMemo(() => {
    const measuredEq = new Set(rows.map(r => r.eqId)).size;
    const totalEq = new Set([...all.map(r => r.eqId), ...equip.map(e => e.eqId)].filter(Boolean)).size;
    const dates = rows.map(r => r.analysisDate).filter(Boolean);
    let maxV = -Infinity, maxEq = '', maxEl = '', maxDate = '';
    let sum = 0, n = 0;
    for (const r of rows)
      for (const el of selEls) {
        const v = r.values[el] ?? 0;
        sum += v; n++;
        if (v > maxV) { maxV = v; maxEq = r.eqId; maxEl = el; maxDate = r.analysisDate; }
      }
    return {
      count: rows.length, totalEq, measuredEq, unmeasured: Math.max(0, totalEq - measuredEq),
      latest: dates.length ? dates.reduce((a, b) => a > b ? a : b) : '-',
      dateCount: new Set(dates).size,
      maxV: maxV > -Infinity ? maxV : null, maxEq, maxEl, maxDate,
      avg: n > 0 ? sum / n : null,
    };
  }, [rows, all, equip, selEls]);

  function resetFilters() {
    setSelPt([]); setSelBath([]); setSelEq([]);
    setSelDates(allDates[0] ? [allDates[0]] : []);
    setSelEls([...ELS]);
  }

  // ── 점검 일지 ──
  const loadNotes = useCallback(async (date: string) => {
    if (!date) { setNoteRows([]); return; }
    const items = await api.get<{ eqId: string; process: string; measured: boolean; topElement: string; topValue: number; note: string }[]>(`/api/icpms/checknotes?date=${date}`);
    setNoteRows(items.map(i => ({
      origEqId: i.eqId, eqId: i.eqId, origProcess: i.process, process: i.process,
      measured: i.measured, summary: i.measured ? `최고 ${i.topElement} ${fmt(i.topValue)}` : '-',
      origNote: i.note, note: i.note,
    })));
  }, []);
  useEffect(() => { if (mode === 'notes' && noteDate) loadNotes(noteDate).catch(() => {}); }, [mode, noteDate, loadNotes]);

  async function saveNotes() {
    let renamed = 0, procs = 0, notes = 0;
    try {
      for (const r of noteRows) {
        const idNow = r.origEqId;
        if (r.eqId.trim() && r.eqId.trim() !== r.origEqId) {
          await api.put(`/api/icpms/equipment/${encodeURIComponent(idNow)}`, { newEqId: r.eqId.trim(), process: null }); renamed++;
        }
        const effId = r.eqId.trim() || idNow;
        if (r.process !== r.origProcess) {
          await api.put(`/api/icpms/equipment/${encodeURIComponent(effId)}`, { newEqId: null, process: r.process }); procs++;
        }
        if (r.note !== r.origNote) {
          await api.put('/api/icpms/checknotes', { eqId: effId, date: noteDate, note: r.note }); notes++;
        }
      }
      alert(`저장 완료 (이름변경 ${renamed} · 공정 ${procs} · 특이사항 ${notes})`);
    } catch (e) { alert('저장 중 오류: ' + (e instanceof Error ? e.message : e)); }
    await loadAll(); await loadNotes(noteDate);
  }
  async function addEq() {
    const id = newEqName.trim();
    if (!id) return;
    try { await api.post('/api/icpms/equipment', { eqId: id }); setNewEqName(''); await loadAll(); await loadNotes(noteDate); }
    catch (e) { alert(e instanceof Error ? e.message : '추가 실패'); }
  }
  async function openHistory(eqId: string) {
    setHistory({ eqId, rows: await api.get<IcpmsHistory[]>(`/api/icpms/checknotes/history?eqId=${encodeURIComponent(eqId)}`) });
  }
  async function deleteEq(eqId: string) {
    if (!confirm(`설비 삭제: ${eqId}?\n(측정 데이터가 없는 설비만 삭제됩니다)`)) return;
    try { await api.del(`/api/icpms/equipment/${encodeURIComponent(eqId)}`); await loadAll(); await loadNotes(noteDate); }
    catch (e) { alert(e instanceof Error ? e.message : '삭제 실패'); }
  }

  // ── 업로드/다운로드/마스터 ──
  async function onUpload(e: React.ChangeEvent<HTMLInputElement>) {
    const f = e.target.files?.[0]; e.target.value = '';
    if (!f) return;
    try {
      const uploadRows = await parseIcpmsUpload(f);
      const r = await api.post<{ received: number; inserted: number; skipped: number }>('/api/icpms/measurements/bulk', { rows: uploadRows });
      alert(`업로드 완료: ${r.received}행 중 ${r.inserted}행 추가 (중복 ${r.skipped} 제외)`);
      firstLoad.current = true; await loadAll();
    } catch (err) { alert('업로드 실패\n\n' + (err instanceof Error ? err.message : String(err))); }
  }
  async function download() {
    try { await exportIcpms(rows.length ? rows : all); } catch (e) { alert('내보내기 실패: ' + (e instanceof Error ? e.message : e)); }
  }
  async function deleteAll() {
    if (!confirm('측정 데이터를 전체 삭제할까요? 되돌릴 수 없습니다.')) return;
    const r = await api.del<{ deleted: number }>('/api/icpms/measurements');
    alert(`${r.deleted}행 삭제`); firstLoad.current = true; await loadAll();
  }
  async function openLogs() { setLogs(await api.get<IcpmsActionLog[]>('/api/icpms/actionlog')); }

  const noteMeasured = noteRows.filter(r => r.measured).length;

  return (
    <div className="icp-page">
      <header className="pg-header">
        <div><h2>설비 ICP-MS</h2><p>ICP-MS 설비별 분석 데이터를 확인합니다.</p></div>
        <button className="btn btn-primary" onClick={() => fileRef.current?.click()}>엑셀 업로드</button>
        <input ref={fileRef} type="file" accept=".xlsx" style={{ display: 'none' }} onChange={onUpload} />
        <button className="btn btn-ghost" onClick={download}>다운로드</button>
        <button className="btn btn-ghost" onClick={() => downloadIcpmsSample()}>양식 샘플</button>
        {isMaster && <button className="btn btn-ghost" onClick={openLogs}>작업 이력</button>}
        {isMaster && <button className="btn btn-danger" onClick={deleteAll}>전체 삭제</button>}
      </header>

      <div className="pg-body">
        {/* 모드 세그먼트 + 드롭다운 필터 (한 줄) */}
        <div className="icp-bar">
          <div className="icp-modes">
            {([['compare', '설비별 비교'], ['trend', '기간별 추이'], ['notes', '점검 일지']] as [Mode, string][]).map(([m, l]) => (
              <button key={m} className={`icp-mode ${mode === m ? 'on' : ''}`} onClick={() => setMode(m)}>{l}</button>
            ))}
          </div>
          <span className="icp-flt-lbl">설비 유형</span>
          <MultiSelect width={120} options={ptOpts} sel={selPt} onChange={setSelPt} />
          <span className="icp-flt-lbl">약액</span>
          <MultiSelect width={120} options={bathOpts} sel={selBath} onChange={setSelBath} />
          <span className="icp-flt-lbl">설비</span>
          <MultiSelect width={120} options={eqOpts} sel={selEq} onChange={setSelEq} />
          {mode !== 'notes' && <>
            <span className="icp-flt-lbl">날짜</span>
            <DateFilter width={120} dates={dateOpts} sel={selDates} onChange={setSelDates} />
          </>}
          {mode === 'trend' && <>
            <span className="icp-flt-lbl">단위</span>
            <select className="icp-sel" value={unit} onChange={e => setUnit(e.target.value as PeriodUnit)}>
              <option>일별</option><option>월별</option><option>년별</option>
            </select>
          </>}
          <button className="icp-reset" onClick={resetFilters}>필터 초기화</button>
        </div>

        {/* 원소 칩 */}
        <div className="icp-elbar">
          <span className="icp-flt-lbl">원소</span>
          <button className={`icp-chip ${selEls.length === ELS.length ? 'on' : ''}`} onClick={() => setSelEls([...ELS])}>전체</button>
          {ELS.map(el => (
            <button key={el} className={`icp-chip ${selEls.length !== ELS.length && selEls.includes(el) ? 'on' : ''}`}
              onClick={() => setSelEls(p => p.length === ELS.length ? [el] : p.includes(el) ? (p.length === 1 ? [...ELS] : p.filter(x => x !== el)) : [...p, el])}>{el}</button>
          ))}
        </div>

        {/* 요약 카드 4 */}
        <div className="icp-cards">
          <Card sub="측정 건수" n={stats.count.toLocaleString()} l={`전체 ${stats.totalEq}대 · 측정 완료 ${stats.measuredEq}대 · 미측정 ${stats.unmeasured}대`} />
          <Card sub="최근 측정일" n={stats.latest} l={stats.dateCount ? `측정일 ${stats.dateCount}일` : ''} small />
          <Card sub="최고 오염" n={stats.maxV == null ? '-' : fmt(stats.maxV)} l={stats.maxV == null ? '' : `${stats.maxEq} · ${stats.maxEl} · ${stats.maxDate}`} danger />
          <Card sub="평균 (선택 원소)" n={stats.avg == null ? '-' : fmt(stats.avg)} l="ppb" />
        </div>

        {mode !== 'notes' && (
          <>
            {/* 차트 카드 (접이식) */}
            <div className="icp-panel">
              <button className="icp-panel-toggle" onClick={() => setChartOpen(v => !v)}>
                <b>차트</b><span className="icp-arrow">{chartOpen ? '▾' : '▸'}</span>
              </button>
              {chartOpen && (
                <div className="icp-panel-body">
                  {all.length === 0
                    ? <div className="icp-empty">데이터가 없습니다. '엑셀 업로드'로 분석 데이터를 추가하세요.</div>
                    : mode === 'compare'
                      ? <BarChart rows={rows} els={selEls} procMap={procMap} />
                      : <TrendChart rows={rows} els={selEls} unit={unit} eqLabel={eqLabel} />}
                </div>
              )}
            </div>

            {/* 데이터 표 카드 (접이식) */}
            <div className="icp-panel">
              <button className="icp-panel-toggle" onClick={() => setTableOpen(v => !v)}>
                <b>데이터 표</b><span className="icp-count">{rows.length}행</span><span className="icp-arrow">{tableOpen ? '▾' : '▸'}</span>
              </button>
              {tableOpen && (
                <div className="icp-panel-body icp-tablewrap">
                  <table className="icp-table">
                    <thead><tr><th>설비</th><th>약액</th><th>구분</th><th>분석일</th><th>단위</th>{selEls.map(e => <th key={e}>{e}</th>)}</tr></thead>
                    <tbody>
                      {[...rows].sort((a, b) => b.analysisDate.localeCompare(a.analysisDate) || a.eqId.localeCompare(b.eqId)).map(r => (
                        <tr key={r.id}>
                          <td className="b">{r.eqId}</td><td>{r.bathGb}</td><td>{r.category}</td><td>{r.analysisDate}</td><td>{r.unit}</td>
                          {selEls.map(e => <td key={e}>{Number(((r.values[e] ?? 0)).toFixed(3))}</td>)}
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          </>
        )}

        {mode === 'notes' && (
          <div className="icp-notes">
            {/* 좌: 측정일 목록 */}
            <div className="icp-datecol">
              <div className="icp-datecol-h">측정일</div>
              {allDates.map(d => (
                <button key={d} className={`icp-dateitem ${noteDate === d ? 'on' : ''}`} onClick={() => setNoteDate(d)}>{d}</button>
              ))}
              {allDates.length === 0 && <div className="icp-dim" style={{ padding: 12 }}>측정 데이터 없음</div>}
            </div>
            {/* 우: 선택 날짜 현황 */}
            <div className="icp-notelist">
              <div className="icp-notelist-h">
                <div>
                  <div className="icp-nl-date">{noteDate || '측정일을 선택하세요'}</div>
                  <div className="icp-dim">전체 {noteRows.length}대 · 측정 완료 {noteMeasured}대 · 미측정 {noteRows.length - noteMeasured}대</div>
                </div>
                <div className="icp-nl-actions">
                  <input className="icp-neweq" placeholder="추가할 설비명" value={newEqName}
                    onChange={e => setNewEqName(e.target.value)} onKeyDown={e => { if (e.key === 'Enter') addEq(); }} />
                  <button className="btn btn-ghost" onClick={addEq}>설비 추가</button>
                  <button className="btn btn-primary" onClick={saveNotes}>저장</button>
                </div>
              </div>
              <div className="icp-note-cols">
                <span>설비</span><span className="proc">공정</span><span>상태</span><span>주요값</span><span>특이사항</span><span></span>
              </div>
              <div className="icp-note-scroll">
                {noteRows.map((r, i) => (
                  <div key={r.origEqId} className="icp-noterow">
                    <input className="icp-eq-in" value={r.eqId} title="설비명(변경 시 측정 데이터도 매칭)"
                      onChange={e => setNoteRows(p => p.map((x, j) => j === i ? { ...x, eqId: e.target.value } : x))} />
                    <input className="icp-proc-in" value={r.process} placeholder="공정/급 (예: A급)"
                      onChange={e => setNoteRows(p => p.map((x, j) => j === i ? { ...x, process: e.target.value } : x))} />
                    <span className={`icp-badge ${r.measured ? 'ok' : 'no'}`}>{r.measured ? '측정 완료' : '미측정'}</span>
                    <span className="icp-nr-sum">{r.summary}</span>
                    <input className="icp-note-in" value={r.note}
                      onChange={e => setNoteRows(p => p.map((x, j) => j === i ? { ...x, note: e.target.value } : x))} />
                    <div className="icp-rowbtns">
                      <button className="icp-histbtn" onClick={() => openHistory(r.origEqId)}>이력</button>
                      {!r.measured && <button className="icp-delbtn" title="설비 삭제(측정 데이터 없는 설비만)" onClick={() => deleteEq(r.origEqId)}>✕</button>}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          </div>
        )}
      </div>

      {history && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setHistory(null); }}>
          <div className="modal-box icp-hist">
            <h3>{history.eqId} 특이사항 이력</h3>
            {history.rows.length === 0 && <p className="icp-dim">기록된 특이사항이 없습니다.</p>}
            {history.rows.map((h, i) => (
              <div key={i} className="icp-hist-row"><b>{h.checkDate}</b><span>{h.note}</span><small>{h.updatedAt}</small></div>
            ))}
            <div className="modal-actions"><button className="btn btn-primary" onClick={() => setHistory(null)}>닫기</button></div>
          </div>
        </div>
      )}

      {logs && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setLogs(null); }}>
          <div className="modal-box icp-log">
            <h3>작업 이력 (최근 500)</h3>
            <div className="icp-log-wrap">
              <table className="icp-log-t"><thead><tr><th>일시</th><th>작업자</th><th>구분</th><th>내용</th></tr></thead>
                <tbody>{logs.map(l => <tr key={l.id}><td>{l.createdAt}</td><td>{l.userName}</td><td>{l.actionType}</td><td className="l">{l.detail}</td></tr>)}</tbody>
              </table>
            </div>
            <div className="modal-actions"><button className="btn btn-primary" onClick={() => setLogs(null)}>닫기</button></div>
          </div>
        </div>
      )}
    </div>
  );
}

// ── 드롭다운 다중선택 필터 (WPF MultiSelectFilter) ──
function MultiSelect({ width, options, sel, onChange }: { width: number; options: string[]; sel: string[]; onChange: (v: string[]) => void }) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const h = (e: MouseEvent) => { if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false); };
    document.addEventListener('mousedown', h);
    return () => document.removeEventListener('mousedown', h);
  }, []);
  const label = sel.length === 0 ? '전체' : sel.length === 1 ? sel[0] : `${sel[0]} 외 ${sel.length - 1}`;
  return (
    <div className="icp-ms" style={{ width }} ref={ref}>
      <button className="icp-ms-btn" onClick={() => setOpen(v => !v)}>
        <span className="icp-ms-txt">{label}</span><span className="icp-arrow">▾</span>
      </button>
      {open && (
        <div className="icp-ms-pop">
          <button className="icp-ms-clear" onClick={() => onChange([])}>전체 (선택 해제)</button>
          {options.map(o => (
            <label key={o} className="icp-ms-item">
              <input type="checkbox" checked={sel.includes(o)}
                onChange={() => onChange(sel.includes(o) ? sel.filter(x => x !== o) : [...sel, o])} />
              {o}
            </label>
          ))}
          {options.length === 0 && <div className="icp-dim" style={{ padding: 8 }}>옵션 없음</div>}
        </div>
      )}
    </div>
  );
}

// ── 날짜 달력 필터 (WPF DatePopup): 분석 일자가 있는 날만 선택 가능, 멀티 선택 + 초기화 ──
function DateFilter({ width, dates, sel, onChange }: { width: number; dates: string[]; sel: string[]; onChange: (v: string[]) => void }) {
  const [open, setOpen] = useState(false);
  const [month, setMonth] = useState('');   // yyyy-MM
  const ref = useRef<HTMLDivElement>(null);
  const dataSet = useMemo(() => new Set(dates), [dates]);

  useEffect(() => {
    const h = (e: MouseEvent) => { if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false); };
    document.addEventListener('mousedown', h);
    return () => document.removeEventListener('mousedown', h);
  }, []);

  function openPopup() {
    if (!open) {
      // 선택된 날짜(없으면 최신 데이터 날짜)의 달로 이동
      const base = sel[0] ?? dates[0];
      if (base) setMonth(base.slice(0, 7));
      else if (!month) setMonth(new Date().toISOString().slice(0, 7));
    }
    setOpen(v => !v);
  }
  function moveMonth(d: number) {
    const [y, m] = month.split('-').map(Number);
    const nd = new Date(y, m - 1 + d, 1);
    setMonth(`${nd.getFullYear()}-${String(nd.getMonth() + 1).padStart(2, '0')}`);
  }
  const todayStr = (() => {
    const n = new Date();
    return `${n.getFullYear()}-${String(n.getMonth() + 1).padStart(2, '0')}-${String(n.getDate()).padStart(2, '0')}`;
  })();
  function goToday() { setMonth(todayStr.slice(0, 7)); }
  function toggleDay(dateStr: string) {
    onChange(sel.includes(dateStr) ? sel.filter(x => x !== dateStr) : [...sel, dateStr].sort());
  }

  const label = sel.length === 0 ? '전체' : sel.length === 1 ? sel[0] : `${sel[0]} 외 ${sel.length - 1}`;
  const [yy, mm] = month ? month.split('-').map(Number) : [0, 0];
  const firstDow = month ? new Date(yy, mm - 1, 1).getDay() : 0;
  const dayCount = month ? new Date(yy, mm, 0).getDate() : 0;
  const cells: (number | null)[] = [...Array(firstDow).fill(null), ...Array.from({ length: dayCount }, (_, i) => i + 1)];

  return (
    <div className="icp-ms" style={{ width }} ref={ref}>
      <button className="icp-ms-btn" onClick={openPopup}>
        <span className="icp-ms-txt">{label}</span><span className="icp-arrow">▾</span>
      </button>
      {open && (
        <div className="icp-cal-pop">
          <div className="icp-cal-head">
            <button className="icp-cal-nav" onClick={() => moveMonth(-1)}>◀</button>
            <b>{yy}년 {mm}월</b>
            <div className="icp-cal-headr">
              <button className="icp-cal-today" onClick={goToday}>오늘</button>
              <button className="icp-cal-nav" onClick={() => moveMonth(1)}>▶</button>
            </div>
          </div>
          <div className="icp-cal-grid">
            {['일', '월', '화', '수', '목', '금', '토'].map((d, i) => (
              <span key={d} className={`icp-cal-dow ${i === 0 ? 'sun' : i === 6 ? 'sat' : ''}`}>{d}</span>
            ))}
            {cells.map((d, i) => {
              if (d === null) return <span key={`e${i}`} />;
              const ds = `${month}-${String(d).padStart(2, '0')}`;
              const has = dataSet.has(ds);
              const on = sel.includes(ds);
              return (
                <button key={ds} disabled={!has} onClick={() => toggleDay(ds)}
                  className={`icp-cal-day ${on ? 'on' : ''} ${has ? 'has' : ''} ${ds === todayStr ? 'today' : ''}`}>{d}</button>
              );
            })}
          </div>
          <div className="icp-cal-foot">
            <span className="icp-dim">{sel.length ? `${sel.length}일 선택` : '전체 (미선택)'}</span>
            <button className="icp-cal-reset" onClick={() => onChange([])}>초기화</button>
          </div>
        </div>
      )}
    </div>
  );
}

function Card({ sub, n, l, danger, small }: { sub: string; n: string; l: string; danger?: boolean; small?: boolean }) {
  return (
    <div className={`icp-card ${danger ? 'danger' : ''}`}>
      <span className="icp-card-sub">{sub}</span>
      <span className={`icp-card-n ${small ? 'sm' : ''}`}>{n}{danger && n !== '-' ? <small> ppb</small> : null}</span>
      <span className="icp-card-l">{l}</span>
    </div>
  );
}

// ── 설비별 비교: 각 설비의 필터 내 최신일 값(동일일 다행 평균), 2줄 라벨 ──
function BarChart({ rows, els, procMap }: { rows: IcpmsMeasurement[]; els: string[]; procMap: Map<string, string> }) {
  const eqData = useMemo(() => {
    const groups = new Map<string, IcpmsMeasurement[]>();
    for (const r of rows) (groups.get(r.eqId) ?? groups.set(r.eqId, []).get(r.eqId)!).push(r);
    return [...groups.entries()].sort(([a], [b]) => a.localeCompare(b)).map(([eq, list]) => {
      const latest = list.reduce((m, r) => r.analysisDate > m ? r.analysisDate : m, '');
      const sub = list.filter(r => r.analysisDate === latest);
      const values = Object.fromEntries(els.map(el => [el, sub.reduce((s, r) => s + (r.values[el] ?? 0), 0) / sub.length]));
      return { eq, values };
    });
  }, [rows, els]);
  if (eqData.length === 0) return <div className="icp-empty">표시할 데이터가 없습니다. 필터/날짜를 확인하세요.</div>;

  const maxV = Math.max(1e-9, ...eqData.flatMap(d => els.map(e => d.values[e] ?? 0)));
  const groupW = Math.max(64, Math.min(200, els.length * 9 + 30));
  const H = 250, padT = 12, padB = 40, padL = 46;
  const W = padL + eqData.length * groupW + 16;
  const plotH = H - padT - padB;
  return (
    <div className="icp-chartwrap">
      <svg width={W} height={H} className="icp-chart">
        <text x={12} y={H / 2} fontSize={11} fill="#6B7280" transform={`rotate(-90 12 ${H / 2})`} textAnchor="middle">ppb</text>
        {[0, 0.5, 1].map(t => (
          <g key={t}>
            <line x1={padL} x2={W} y1={padT + plotH * (1 - t)} y2={padT + plotH * (1 - t)} stroke="#F1F5F9" />
            <text x={padL - 6} y={padT + plotH * (1 - t) + 4} fontSize={10} fill="#9CA3AF" textAnchor="end">{fmt(maxV * t)}</text>
          </g>
        ))}
        {eqData.map((d, gi) => {
          const gx = padL + gi * groupW;
          const bw = (groupW - 18) / els.length;
          const proc = procMap.get(d.eq);
          return (
            <g key={d.eq}>
              {els.map((el, ei) => {
                const v = d.values[el] ?? 0;
                const h = plotH * (v / maxV);
                return <rect key={el} x={gx + 9 + ei * bw} y={padT + plotH - h} width={Math.max(1, bw - 1)} height={h} rx={1} fill={elColor(el)} />;
              })}
              <text x={gx + groupW / 2} y={H - 24} fontSize={10.5} fill="#0F172A" fontWeight={700} textAnchor="middle">{d.eq}</text>
              {proc && <text x={gx + groupW / 2} y={H - 11} fontSize={9.5} fill="#64748B" textAnchor="middle">({proc})</text>}
            </g>
          );
        })}
      </svg>
      <div className="icp-legend">{els.map(el => <span key={el} className="icp-leg"><i style={{ background: elColor(el) }} />{el}</span>)}</div>
    </div>
  );
}

// ── 기간별 추이: 단위(일/월/년) 그룹 평균. 설비 1대=원소별, 여러 대=설비별(첫 원소) ──
function TrendChart({ rows, els, unit, eqLabel }: { rows: IcpmsMeasurement[]; els: string[]; unit: PeriodUnit; eqLabel: (eq: string) => string }) {
  const dated = rows.filter(r => r.analysisDate);
  const periods = [...new Set(dated.map(r => periodKey(r.analysisDate, unit)))].sort();
  const eqIds = [...new Set(dated.map(r => r.eqId))].sort();
  if (periods.length === 0) return <div className="icp-empty">표시할 데이터가 없습니다.</div>;

  const avg = (list: number[]) => list.length ? list.reduce((s, v) => s + v, 0) / list.length : null;
  const single = eqIds.length === 1;
  const series = single
    ? els.map(el => ({ name: el, color: elColor(el), pts: periods.map(p => avg(dated.filter(r => periodKey(r.analysisDate, unit) === p).map(r => r.values[el] ?? 0))) }))
    : eqIds.map((eq, i) => {
        const el = els[0] ?? 'Fe';
        return { name: eqLabel(eq), color: `hsl(${(i * 360) / Math.max(1, eqIds.length)},60%,48%)`, pts: periods.map(p => avg(dated.filter(r => r.eqId === eq && periodKey(r.analysisDate, unit) === p).map(r => r.values[el] ?? 0))) };
      });

  const all = series.flatMap(s => s.pts.filter((p): p is number => p != null));
  const maxV = Math.max(1e-9, ...all);
  const W = Math.max(420, 60 + periods.length * 88), H = 260, padT = 12, padB = 44, padL = 46;
  const plotH = H - padT - padB;
  const x = (i: number) => padL + (periods.length === 1 ? (W - padL) / 2 : (i * (W - padL - 20)) / (periods.length - 1));
  const y = (v: number) => padT + plotH * (1 - v / maxV);
  return (
    <div className="icp-chartwrap">
      <svg width={W} height={H} className="icp-chart">
        <text x={12} y={H / 2} fontSize={11} fill="#6B7280" transform={`rotate(-90 12 ${H / 2})`} textAnchor="middle">ppb</text>
        {[0, 0.5, 1].map(t => (
          <g key={t}>
            <line x1={padL} x2={W} y1={y(maxV * t)} y2={y(maxV * t)} stroke="#F1F5F9" />
            <text x={padL - 6} y={y(maxV * t) + 4} fontSize={10} fill="#9CA3AF" textAnchor="end">{fmt(maxV * t)}</text>
          </g>
        ))}
        {periods.map((p, i) => (
          <text key={p} x={x(i)} y={H - 14} fontSize={10} fill="#475569" textAnchor="end" transform={`rotate(-30 ${x(i)} ${H - 14})`}>{p}</text>
        ))}
        {series.map(s => (
          <g key={s.name}>
            <polyline fill="none" stroke={s.color} strokeWidth={2}
              points={s.pts.map((p, i) => p == null ? '' : `${x(i)},${y(p)}`).filter(Boolean).join(' ')} />
            {s.pts.map((p, i) => p == null ? null : <circle key={i} cx={x(i)} cy={y(p)} r={3} fill="#fff" stroke={s.color} strokeWidth={2} />)}
          </g>
        ))}
      </svg>
      <div className="icp-legend">{series.slice(0, 14).map(s => <span key={s.name} className="icp-leg"><i style={{ background: s.color }} />{s.name}</span>)}</div>
    </div>
  );
}
