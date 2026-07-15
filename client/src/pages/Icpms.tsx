import { useEffect, useState, useCallback, useRef } from 'react';
import { api } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { ICP_ELEMENTS } from '../api/types';
import type {
  IcpmsFilters, IcpmsSummary, IcpmsComparison, IcpmsMeasurement,
  IcpmsCheckNote, IcpmsHistory, IcpmsActionLog,
} from '../api/types';
import { parseIcpmsUpload, exportIcpms } from './icpmsExcel';
import './Icpms.css';

const csv = (a: string[]) => a.join(',');
const elColor = (el: string) => `hsl(${((ICP_ELEMENTS as readonly string[]).indexOf(el) * 360) / ICP_ELEMENTS.length}, 62%, 52%)`;
const fmt = (n: number) => n >= 100 ? Math.round(n).toLocaleString() : n.toFixed(n >= 10 ? 1 : 2);

type Mode = 'compare' | 'trend' | 'notes';

export default function Icpms() {
  const { user } = useAuth();
  const isMaster = !!user?.isAdmin;
  const [mode, setMode] = useState<Mode>('compare');

  const [filters, setFilters] = useState<IcpmsFilters>({ processTypes: [], baths: [], eqIds: [], dates: [] });
  const [selPt, setSelPt] = useState<string[]>([]);
  const [selBath, setSelBath] = useState<string[]>([]);
  const [selEq, setSelEq] = useState<string[]>([]);
  const [selDates, setSelDates] = useState<string[]>([]);
  const [selEls, setSelEls] = useState<string[]>([...ICP_ELEMENTS]);

  const [summary, setSummary] = useState<IcpmsSummary | null>(null);
  const [comp, setComp] = useState<IcpmsComparison[]>([]);
  const [meas, setMeas] = useState<IcpmsMeasurement[]>([]);
  const [showTable, setShowTable] = useState(false);

  const [notes, setNotes] = useState<IcpmsCheckNote[]>([]);
  const [noteDate, setNoteDate] = useState('');
  const [noteDraft, setNoteDraft] = useState<Record<string, string>>({});
  const [history, setHistory] = useState<{ eqId: string; rows: IcpmsHistory[] } | null>(null);

  const [logs, setLogs] = useState<IcpmsActionLog[] | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);
  const firstLoad = useRef(true);

  // 종속 필터 + 초기 최신일 기본값
  useEffect(() => {
    api.get<IcpmsFilters>(`/api/icpms/filters?processTypes=${encodeURIComponent(csv(selPt))}`).then(f => {
      setFilters(f);
      if (firstLoad.current && f.dates.length) { setSelDates([f.dates[0]]); setNoteDate(f.dates[0]); firstLoad.current = false; }
    }).catch(() => {});
  }, [selPt]);

  const q = `processTypes=${encodeURIComponent(csv(selPt))}&baths=${encodeURIComponent(csv(selBath))}&eqIds=${encodeURIComponent(csv(selEq))}&dates=${encodeURIComponent(csv(selDates))}`;
  const loadData = useCallback(() => {
    api.get<IcpmsComparison[]>(`/api/icpms/comparison?${q}`).then(setComp).catch(() => {});
    api.get<IcpmsMeasurement[]>(`/api/icpms/measurements?${q}`).then(setMeas).catch(() => {});
    api.get<IcpmsSummary>(`/api/icpms/summary?dates=${encodeURIComponent(csv(selDates))}&elements=${encodeURIComponent(csv(selEls))}`).then(setSummary).catch(() => {});
  }, [q, selDates, selEls]);
  useEffect(() => { loadData(); }, [loadData]);

  const loadNotes = useCallback(() => {
    if (!noteDate) { setNotes([]); return; }
    api.get<IcpmsCheckNote[]>(`/api/icpms/checknotes?date=${noteDate}`).then(n => { setNotes(n); setNoteDraft(Object.fromEntries(n.map(x => [x.eqId, x.note]))); }).catch(() => {});
  }, [noteDate]);
  useEffect(() => { if (mode === 'notes') loadNotes(); }, [mode, loadNotes]);

  function toggle(list: string[], set: (v: string[]) => void, v: string) {
    set(list.includes(v) ? list.filter(x => x !== v) : [...list, v]);
  }
  function resetFilters() { setSelPt([]); setSelBath([]); setSelEq([]); setSelDates(filters.dates[0] ? [filters.dates[0]] : []); setSelEls([...ICP_ELEMENTS]); }

  async function onUpload(e: React.ChangeEvent<HTMLInputElement>) {
    const f = e.target.files?.[0]; e.target.value = '';
    if (!f) return;
    try {
      const rows = await parseIcpmsUpload(f);
      if (rows.length === 0) { alert('인식된 데이터가 없습니다. (시트명=설비유형, 헤더에 EQ_ID 필요)'); return; }
      const r = await api.post<{ received: number; inserted: number; skipped: number }>('/api/icpms/measurements/bulk', { rows });
      alert(`업로드 완료: ${r.received}행 중 ${r.inserted}행 추가 (중복 ${r.skipped} 제외)`);
      firstLoad.current = true; setSelPt(p => [...p]); loadData();
    } catch (err) { alert('업로드 실패: ' + (err instanceof Error ? err.message : err)); }
  }
  async function download() {
    try { await exportIcpms(meas); } catch (e) { alert('내보내기 실패: ' + (e instanceof Error ? e.message : e)); }
  }
  async function deleteAll() {
    if (!confirm('측정 데이터를 전체 삭제할까요? 되돌릴 수 없습니다.')) return;
    const r = await api.del<{ deleted: number }>('/api/icpms/measurements');
    alert(`${r.deleted}행 삭제`); firstLoad.current = true; setSelPt([]); loadData();
  }
  async function openLogs() { setLogs(await api.get<IcpmsActionLog[]>('/api/icpms/actionlog')); }

  async function saveNote(eqId: string) {
    await api.put('/api/icpms/checknotes', { eqId, date: noteDate, note: noteDraft[eqId] ?? '' });
    loadNotes();
  }
  async function renameEq(eqId: string) {
    const nv = prompt(`설비명 변경: ${eqId} →`, eqId);
    if (!nv || nv.trim() === eqId) return;
    try { await api.put(`/api/icpms/equipment/${encodeURIComponent(eqId)}`, { newEqId: nv.trim(), process: null }); loadNotes(); loadData(); }
    catch (e) { alert(e instanceof Error ? e.message : '변경 실패'); }
  }
  async function editProcess(eqId: string, cur: string) {
    const nv = prompt(`공정 (예: A급): ${eqId}`, cur);
    if (nv === null) return;
    await api.put(`/api/icpms/equipment/${encodeURIComponent(eqId)}`, { newEqId: null, process: nv.trim() }); loadNotes(); loadData();
  }
  async function addEq() {
    const id = prompt('추가할 설비 ID (예: MDC11)');
    if (!id?.trim()) return;
    try { await api.post('/api/icpms/equipment', { eqId: id.trim() }); loadNotes(); loadData(); }
    catch (e) { alert(e instanceof Error ? e.message : '추가 실패'); }
  }
  async function openHistory(eqId: string) {
    setHistory({ eqId, rows: await api.get<IcpmsHistory[]>(`/api/icpms/checknotes/history?eqId=${encodeURIComponent(eqId)}`) });
  }

  const label = (eqId: string, process: string) => process ? `${eqId} (${process})` : eqId;

  return (
    <div className="icp-page">
      <header className="pg-header">
        <div><h2>설비 ICP-MS</h2></div>
        <button className="btn btn-ghost" onClick={() => fileRef.current?.click()}>엑셀 업로드</button>
        <input ref={fileRef} type="file" accept=".xlsx" style={{ display: 'none' }} onChange={onUpload} />
        <button className="btn btn-ghost" onClick={download}>엑셀 다운로드</button>
        {isMaster && <button className="btn btn-ghost" onClick={openLogs}>작업 이력</button>}
        {isMaster && <button className="btn btn-danger" onClick={deleteAll}>전체 삭제</button>}
      </header>

      <div className="pg-body">
        {/* 모드 세그먼트 */}
        <div className="icp-modes">
          {([['compare', '설비별 비교'], ['trend', '기간별 추이'], ['notes', '점검 일지']] as [Mode, string][]).map(([m, l]) => (
            <button key={m} className={`icp-mode ${mode === m ? 'on' : ''}`} onClick={() => setMode(m)}>{l}</button>
          ))}
        </div>

        {/* 필터 바 */}
        <div className="icp-filters">
          <ChipRow title="설비 유형" opts={filters.processTypes} sel={selPt} onToggle={v => toggle(selPt, setSelPt, v)} />
          <ChipRow title="약액" opts={filters.baths} sel={selBath} onToggle={v => toggle(selBath, setSelBath, v)} />
          <ChipRow title="설비" opts={filters.eqIds} sel={selEq} onToggle={v => toggle(selEq, setSelEq, v)} />
          <ChipRow title="날짜" opts={filters.dates} sel={selDates} onToggle={v => toggle(selDates, setSelDates, v)} />
          <div className="icp-frow">
            <span className="icp-flabel">원소</span>
            <div className="icp-chips">
              <button className={`icp-chip ${selEls.length === ICP_ELEMENTS.length ? 'on' : ''}`} onClick={() => setSelEls([...ICP_ELEMENTS])}>전체</button>
              {ICP_ELEMENTS.map(el => (
                <button key={el} className={`icp-chip ${selEls.includes(el) ? 'on' : ''}`}
                  style={selEls.includes(el) ? { borderColor: elColor(el), color: elColor(el) } : undefined}
                  onClick={() => toggle(selEls, setSelEls, el)}>{el}</button>
              ))}
            </div>
            <button className="icp-reset" onClick={resetFilters}>필터 초기화</button>
          </div>
        </div>

        {/* 요약 카드 */}
        {summary && (
          <div className="icp-cards">
            <Card n={`${summary.totalEquip}대`} l={`측정 완료 ${summary.measuredEquip} · 미측정 ${summary.unmeasuredEquip}`} sub="측정 대상" />
            <Card n={summary.latestDate || '-'} l={`측정일 ${summary.measuredDateCount}일`} sub="최근 측정일" small />
            <Card n={`${fmt(summary.maxValue)} ${summary.unit}`} l={summary.maxEqId ? `${summary.maxEqId} · ${summary.maxElement} · ${summary.maxDate}` : '-'} sub="최고 오염" danger small />
            <Card n={`${fmt(summary.average)} ${summary.unit}`} l={`선택 원소 ${selEls.length}종 평균`} sub="평균" small />
          </div>
        )}

        {mode === 'compare' && (
          <div className="icp-panel">
            <div className="icp-panel-h"><b>설비별 비교</b> <span className="icp-dim">필터 범위 내 각 설비 최신일 대표값</span>
              <button className="icp-link" onClick={() => setShowTable(v => !v)}>{showTable ? '표 접기' : '표 펼치기'}</button></div>
            <BarChart comp={comp} els={selEls} label={label} />
            {showTable && <CompareTable comp={comp} els={selEls} label={label} />}
          </div>
        )}

        {mode === 'trend' && <TrendPanel meas={meas} els={selEls} selEq={selEq} label={label} />}

        {mode === 'notes' && (
          <div className="icp-notes">
            <div className="icp-datecol">
              <div className="icp-datecol-h">측정일</div>
              {filters.dates.map(d => (
                <button key={d} className={`icp-dateitem ${noteDate === d ? 'on' : ''}`} onClick={() => setNoteDate(d)}>{d}</button>
              ))}
              {filters.dates.length === 0 && <div className="icp-dim" style={{ padding: 10 }}>측정 데이터 없음</div>}
            </div>
            <div className="icp-notelist">
              <div className="icp-notelist-h">
                <b>{noteDate || '-'}</b>
                <span className="icp-dim">전체 {notes.length} · 완료 {notes.filter(n => n.measured).length} · 미측정 {notes.filter(n => !n.measured).length}</span>
                <button className="btn btn-ghost icp-mini" onClick={addEq}>+ 설비 추가</button>
              </div>
              {notes.map(n => (
                <div key={n.eqId} className="icp-noterow">
                  <div className="icp-nr-head">
                    <span className="icp-nr-eq" onClick={() => renameEq(n.eqId)} title="클릭하여 설비명 변경">{n.eqId}</span>
                    <span className="icp-nr-proc" onClick={() => editProcess(n.eqId, n.process)} title="클릭하여 공정 변경">{n.process || '공정?'}</span>
                    <span className={`icp-badge ${n.measured ? 'ok' : 'no'}`}>{n.measured ? '측정 완료' : '미측정'}</span>
                    {n.measured && <span className="icp-nr-top">{n.topElement} {fmt(n.topValue)} ppb</span>}
                    <button className="icp-link" onClick={() => openHistory(n.eqId)}>이력</button>
                  </div>
                  <div className="icp-nr-note">
                    <input className="icp-note-in" value={noteDraft[n.eqId] ?? ''} placeholder="특이사항"
                      onChange={e => setNoteDraft({ ...noteDraft, [n.eqId]: e.target.value })}
                      onBlur={() => { if ((noteDraft[n.eqId] ?? '') !== n.note) saveNote(n.eqId); }} />
                  </div>
                </div>
              ))}
            </div>
          </div>
        )}
      </div>

      {history && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setHistory(null); }}>
          <div className="modal-box icp-hist">
            <h3>{history.eqId} 특이사항 이력</h3>
            {history.rows.length === 0 && <p className="icp-dim">기록 없음</p>}
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

function ChipRow({ title, opts, sel, onToggle }: { title: string; opts: string[]; sel: string[]; onToggle: (v: string) => void }) {
  return (
    <div className="icp-frow">
      <span className="icp-flabel">{title}</span>
      <div className="icp-chips">
        {opts.length === 0 && <span className="icp-dim">-</span>}
        {opts.map(o => <button key={o} className={`icp-chip ${sel.includes(o) ? 'on' : ''}`} onClick={() => onToggle(o)}>{o}</button>)}
      </div>
    </div>
  );
}

function Card({ n, l, sub, danger, small }: { n: string; l: string; sub: string; danger?: boolean; small?: boolean }) {
  return (
    <div className={`icp-card ${danger ? 'danger' : ''}`}>
      <span className="icp-card-sub">{sub}</span>
      <span className={`icp-card-n ${small ? 'sm' : ''}`}>{n}</span>
      <span className="icp-card-l">{l}</span>
    </div>
  );
}

function BarChart({ comp, els, label }: { comp: IcpmsComparison[]; els: string[]; label: (a: string, b: string) => string }) {
  if (comp.length === 0) return <div className="icp-empty">표시할 데이터가 없습니다. 필터/날짜를 확인하세요.</div>;
  const maxV = Math.max(1, ...comp.flatMap(c => els.map(e => c.values[e] ?? 0)));
  const groupW = Math.max(60, Math.min(140, els.length * 12 + 24));
  const H = 240, pad = 28;
  const W = comp.length * groupW + 40;
  return (
    <div className="icp-chartwrap">
      <svg width={W} height={H + 44} className="icp-chart">
        {[0, 0.25, 0.5, 0.75, 1].map(t => (
          <g key={t}>
            <line x1={36} x2={W} y1={pad + (H - pad) * (1 - t)} y2={pad + (H - pad) * (1 - t)} stroke="#E2E8F0" />
            <text x={4} y={pad + (H - pad) * (1 - t) + 4} fontSize={9} fill="#94A3B8">{fmt(maxV * t)}</text>
          </g>
        ))}
        {comp.map((c, gi) => {
          const gx = 40 + gi * groupW;
          const bw = (groupW - 16) / els.length;
          return (
            <g key={c.eqId}>
              {els.map((el, ei) => {
                const v = c.values[el] ?? 0;
                const h = (H - pad) * (v / maxV);
                return <rect key={el} x={gx + 8 + ei * bw} y={pad + (H - pad) - h} width={Math.max(1, bw - 1)} height={h} fill={elColor(el)} />;
              })}
              <text x={gx + groupW / 2} y={H + 12} fontSize={9} fill="#475569" textAnchor="middle">{label(c.eqId, c.process).slice(0, 14)}</text>
            </g>
          );
        })}
      </svg>
      <div className="icp-legend">{els.map(el => <span key={el} className="icp-leg"><i style={{ background: elColor(el) }} />{el}</span>)}</div>
    </div>
  );
}

function CompareTable({ comp, els, label }: { comp: IcpmsComparison[]; els: string[]; label: (a: string, b: string) => string }) {
  return (
    <div className="icp-tablewrap">
      <table className="icp-table"><thead><tr><th className="l">설비</th>{els.map(e => <th key={e}>{e}</th>)}</tr></thead>
        <tbody>{comp.map(c => <tr key={c.eqId}><td className="l">{label(c.eqId, c.process)}</td>{els.map(e => <td key={e}>{fmt(c.values[e] ?? 0)}</td>)}</tr>)}</tbody>
      </table>
    </div>
  );
}

function TrendPanel({ meas, els, selEq, label }: { meas: IcpmsMeasurement[]; els: string[]; selEq: string[]; label: (a: string, b: string) => string }) {
  // 날짜별 평균: 설비 1대 → 원소별 라인 / 여러 대 → 설비별 라인(첫 원소)
  const dates = [...new Set(meas.map(m => m.analysisDate))].sort();
  if (dates.length === 0) return <div className="icp-panel"><div className="icp-empty">표시할 데이터가 없습니다.</div></div>;
  const single = selEq.length === 1;
  const series: { name: string; color: string; pts: (number | null)[] }[] = [];
  if (single) {
    for (const el of els) series.push({ name: el, color: elColor(el), pts: dates.map(d => avg(meas.filter(m => m.analysisDate === d).map(m => m.values[el] ?? 0))) });
  } else {
    const el = els[0] ?? 'Na';
    const eqs = [...new Set(meas.map(m => m.eqId))];
    eqs.forEach((eq, i) => series.push({ name: eq, color: `hsl(${(i * 360) / Math.max(1, eqs.length)},60%,52%)`, pts: dates.map(d => { const r = meas.filter(m => m.analysisDate === d && m.eqId === eq); return r.length ? avg(r.map(m => m.values[el] ?? 0)) : null; }) }));
  }
  const all = series.flatMap(s => s.pts.filter((p): p is number => p != null));
  const maxV = Math.max(1, ...all);
  const W = Math.max(360, dates.length * 90), H = 260, pad = 30;
  const x = (i: number) => 40 + (dates.length === 1 ? W / 2 : (i * (W - 60)) / (dates.length - 1));
  const y = (v: number) => pad + (H - pad) * (1 - v / maxV);
  return (
    <div className="icp-panel">
      <div className="icp-panel-h"><b>기간별 추이</b> <span className="icp-dim">{single ? '설비 1대 · 원소별' : `설비별 · 기준원소 ${els[0] ?? 'Na'}`}</span></div>
      <div className="icp-chartwrap">
        <svg width={W} height={H + 30} className="icp-chart">
          {[0, 0.5, 1].map(t => <line key={t} x1={36} x2={W} y1={y(maxV * t)} y2={y(maxV * t)} stroke="#E2E8F0" />)}
          {dates.map((d, i) => <text key={d} x={x(i)} y={H + 14} fontSize={9} fill="#475569" textAnchor="middle">{d.slice(5)}</text>)}
          {series.map(s => (
            <g key={s.name}>
              <polyline fill="none" stroke={s.color} strokeWidth={2}
                points={s.pts.map((p, i) => p == null ? '' : `${x(i)},${y(p)}`).filter(Boolean).join(' ')} />
              {s.pts.map((p, i) => p == null ? null : <circle key={i} cx={x(i)} cy={y(p)} r={2.5} fill={s.color} />)}
            </g>
          ))}
        </svg>
        <div className="icp-legend">{series.slice(0, 12).map(s => <span key={s.name} className="icp-leg"><i style={{ background: s.color }} />{single ? s.name : label(s.name, '')}</span>)}</div>
      </div>
    </div>
  );
}

function avg(a: number[]): number { return a.length ? a.reduce((s, v) => s + v, 0) / a.length : 0; }
