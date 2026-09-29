import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { api } from '../../api/client';
import './Work.css';
import { useAccess } from '../../auth/useAccess';
import { useIsMobile } from '../../hooks/useIsMobile';
import type { ChemicalCell, ChemicalImportResult, ChemicalMonth, WorkEquipment } from '../../api/types';
import { parseChemicalWorkbook } from './chemicalImport';
import EquipmentEditor from './EquipmentEditor';
import { contentTone, sortByLine } from './common';

// 약액(CHEMICAL) 교체 기록 — 엑셀 "CHEMICAL 교체 및 설비 변경점" 을 옮긴 화면.
// PC: 날짜 × 설비 표(엑셀과 같은 모양). 폰: 기록이 있는 날만 날짜별 카드로.
// 칸 색은 엑셀 규칙 그대로 — S2 100% 노랑, S2 50% 초록. 메모(설비 변경점·사용횟수)가 있으면 점을 찍는다.

const DOW = ['일', '월', '화', '수', '목', '금', '토'];
const QUICK = ['S2 100%, HF 100%', 'S2 50%, HF 100%', 'S2 100%', 'HF 100%', 'DI 100%'];

const pad = (n: number) => String(n).padStart(2, '0');
const ymd = (y: number, m: number, d: number) => `${y}-${pad(m)}-${pad(d)}`;
function todayYmd() { const t = new Date(); return ymd(t.getFullYear(), t.getMonth() + 1, t.getDate()); }

export default function Chemical() {
  const { canEditOffice: canEdit } = useAccess();
  const isMobile = useIsMobile();
  const now = new Date();
  const [year, setYear] = useState(now.getFullYear());
  const [month, setMonth] = useState(now.getMonth() + 1);
  const [data, setData] = useState<ChemicalMonth | null>(null);
  const [edit, setEdit] = useState<{ date: string; eqCode: string } | null>(null);
  const [eqOpen, setEqOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);

  const load = useCallback(async () => {
    setData(await api.get<ChemicalMonth>(`/api/worklog/chemical?year=${year}&month=${month}`));
  }, [year, month]);
  useEffect(() => { load().catch(() => setData(null)); }, [load]);

  function shift(delta: number) {
    const d = new Date(year, month - 1 + delta, 1);
    setYear(d.getFullYear()); setMonth(d.getMonth() + 1);
  }

  // 약액 교체 표는 세정 설비만(BAKE 오븐은 약액이 없다)
  const eq = useMemo(() => sortByLine((data?.equipment ?? []).filter(e => e.kind !== 'BAKE')), [data]);
  const lines = useMemo(() => {
    const out: { line: string; items: WorkEquipment[] }[] = [];
    for (const e of eq) {
      const last = out[out.length - 1];
      if (last && last.line === e.line) last.items.push(e); else out.push({ line: e.line, items: [e] });
    }
    return out;
  }, [eq]);
  const byKey = useMemo(() => {
    const m = new Map<string, ChemicalCell>();
    for (const c of data?.cells ?? []) m.set(`${c.date}|${c.eqCode}`, c);
    return m;
  }, [data]);
  const days = new Date(year, month, 0).getDate();
  const today = todayYmd();

  async function importFile(f: File) {
    setBusy(true);
    try {
      const parsed = await parseChemicalWorkbook(f);
      if (parsed.cells.length === 0) { alert('가져올 칸을 찾지 못했습니다. "일자/요일/설비명" 머리글이 있는 양식인지 확인하세요.'); return; }
      const overwrite = confirm(
        `${parsed.from} ~ ${parsed.to} · ${parsed.cells.length}칸 · 설비 ${parsed.codes.length}대를 가져옵니다.\n\n` +
        '이미 웹에 적힌 칸은 엑셀 내용으로 덮어쓸까요?\n[확인] 덮어쓰기   [취소] 비어 있는 칸만 채우기');
      const r = await api.post<ChemicalImportResult>('/api/worklog/chemical/import', { cells: parsed.cells, overwrite });
      alert(`새로 ${r.added}칸 · 고침 ${r.updated}칸 · 건너뜀 ${r.skipped}칸` +
        (r.newEquipment.length ? `\n\n목록에 없던 설비를 새로 넣었습니다: ${r.newEquipment.join(', ')}\n(같은 설비를 다른 이름으로 적은 것이면 '설비 목록' 에서 이름을 고치면 기록도 따라갑니다)` : ''));
      await load();
    } catch (e) {
      alert(e instanceof Error ? e.message : '엑셀을 가져오지 못했습니다.');
    } finally {
      setBusy(false);
    }
  }

  const monthCells = (data?.cells ?? []).filter(c => eq.some(e => e.code === c.eqCode));
  const cur = edit ? byKey.get(`${edit.date}|${edit.eqCode}`) : undefined;

  return (
    <div className="wf-page">
      <header className="pg-header">
        <div>
          <h2>약액(CHEMICAL) 교체 기록</h2>
          <p>설비별 약액 교체와 설비 변경점 · 업무보고의 약액교체현황이 여기서 채워집니다.</p>
        </div>
        {canEdit && <button className="btn btn-ghost" onClick={() => setEqOpen(true)}>설비 목록</button>}
        {canEdit && <button className="btn btn-ghost" disabled={busy} onClick={() => fileRef.current?.click()}>{busy ? '가져오는 중…' : '엑셀 가져오기'}</button>}
        {canEdit && isMobile && <button className="btn btn-primary" onClick={() => setEdit({ date: today, eqCode: eq[0]?.code ?? '' })}>+ 기록</button>}
        <input ref={fileRef} type="file" accept=".xlsx" hidden onChange={e => { const f = e.target.files?.[0]; if (f) void importFile(f); e.target.value = ''; }} />
      </header>

      <div className="pg-body">
        <div className="wf-toolbar">
          <div className="wf-monthnav">
            <button onClick={() => shift(-1)} aria-label="이전 달">‹</button>
            <b>{year}년 {month}월</b>
            <button onClick={() => shift(1)} aria-label="다음 달">›</button>
          </div>
          <span className="wf-count">이달 기록 {monthCells.length}칸</span>
          <span className="wf-legend">
            <i className="wf-sw full" />S2 100% <i className="wf-sw half" />S2 50% <i className="wf-sw other" />그 밖 <i className="wf-dot" />메모
          </span>
        </div>

        {!data ? <div className="wf-empty">불러오는 중…</div> : isMobile ? (
          <div className="wf-mlist">
            {monthCells.length === 0 && <div className="wf-empty">이달 기록이 없습니다.</div>}
            {Array.from({ length: days }, (_, i) => days - i).map(d => {
              const date = ymd(year, month, d);
              const list = eq.map(e => ({ e, c: byKey.get(`${date}|${e.code}`) })).filter(x => x.c);
              if (list.length === 0) return null;
              const dow = new Date(year, month - 1, d).getDay();
              return (
                <div key={d} className={`wf-mcard ${date === today ? 'today' : ''}`}>
                  <div className={`wf-mday dow${dow}`}>{month}/{d} ({DOW[dow]})</div>
                  {list.map(({ e, c }) => (
                    <button key={e.code} className="wf-mrow" onClick={() => canEdit && setEdit({ date, eqCode: e.code })}>
                      <b>{e.code}</b>
                      <span className={`wf-chip ${contentTone(c!.content)}`}>{c!.content || '메모'}</span>
                      {c!.note && <em>{c!.note.split('\n')[0]}</em>}
                    </button>
                  ))}
                </div>
              );
            })}
          </div>
        ) : (
          <div className="wf-gridwrap">
            <table className="wf-grid">
              <thead>
                <tr>
                  <th className="wf-dcol" rowSpan={2}>일자</th>
                  {lines.map(l => <th key={l.line} colSpan={l.items.length} className="wf-lhead">{l.line}</th>)}
                </tr>
                <tr>
                  {eq.map(e => <th key={e.code} title={e.process}><b>{e.code}</b><small>{e.process}</small></th>)}
                </tr>
              </thead>
              <tbody>
                {Array.from({ length: days }, (_, i) => i + 1).map(d => {
                  const date = ymd(year, month, d);
                  const dow = new Date(year, month - 1, d).getDay();
                  return (
                    <tr key={d} className={date === today ? 'today' : ''}>
                      <td className={`wf-dcol dow${dow}`}>{month}/{d} <span>{DOW[dow]}</span></td>
                      {eq.map(e => {
                        const c = byKey.get(`${date}|${e.code}`);
                        return (
                          <td key={e.code} className={`wf-cell ${c ? contentTone(c.content) : ''} ${canEdit ? 'editable' : ''}`}
                            title={c ? `${c.content}${c.note ? `\n\n${c.note}` : ''}\n— ${c.updatedBy}` : ''}
                            onClick={() => canEdit && setEdit({ date, eqCode: e.code })}>
                            {c?.content.split(', ').map((p, i) => <span key={i}>{p}</span>)}
                            {c?.note && <i className="wf-dot" />}
                          </td>
                        );
                      })}
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {edit && (
        <CellEditor date={edit.date} eqCode={edit.eqCode} equipment={eq} cell={cur}
          onClose={() => setEdit(null)} onSaved={async () => { setEdit(null); await load(); }} />
      )}
      {eqOpen && <EquipmentEditor onClose={() => setEqOpen(false)} onSaved={async () => { setEqOpen(false); await load(); }} />}
    </div>
  );
}

function CellEditor({ date: date0, eqCode: eq0, equipment, cell, onClose, onSaved }: {
  date: string; eqCode: string; equipment: WorkEquipment[]; cell?: ChemicalCell;
  onClose: () => void; onSaved: () => Promise<void>;
}) {
  const [date, setDate] = useState(date0);
  const [eqCode, setEqCode] = useState(eq0);
  const [content, setContent] = useState(cell?.content ?? '');
  const [note, setNote] = useState(cell?.note ?? '');
  const [saving, setSaving] = useState(false);
  const e = equipment.find(x => x.code === eqCode);

  async function save(clear = false) {
    if (clear && !confirm('이 칸을 지울까요?')) return;
    setSaving(true);
    try {
      await api.put('/api/worklog/chemical', { date, eqCode, content: clear ? '' : content, note: clear ? '' : note });
      await onSaved();
    } catch (err) {
      alert(err instanceof Error ? err.message : '저장하지 못했습니다.');
    } finally { setSaving(false); }
  }

  return (
    <div className="modal-bg" onClick={ev => { if (ev.target === ev.currentTarget) onClose(); }}>
      <div className="modal-box wf-edit">
        <h3>약액 교체 · {eqCode} <small>{e?.process}</small></h3>
        <div className="wf-edit-row">
          <label>일자<input className="input" type="date" value={date} disabled={!!cell} onChange={ev => setDate(ev.target.value)} /></label>
          <label>설비<select className="input" value={eqCode} disabled={!!cell} onChange={ev => setEqCode(ev.target.value)}>
            {equipment.map(x => <option key={x.code} value={x.code}>{x.code} · {x.process}</option>)}
          </select></label>
        </div>
        <label>교체 내용</label>
        <div className="wf-quick">
          {QUICK.map(q => (
            <button key={q} type="button" className={`wf-chip ${contentTone(q)} ${content === q ? 'on' : ''}`} onClick={() => setContent(q)}>{q}</button>
          ))}
        </div>
        <input className="input" value={content} placeholder="예: S2 100%, HF 100%" onChange={ev => setContent(ev.target.value)} />
        <label>메모 <span className="wf-hint">설비 변경점 · 사용횟수 · Heater 교체 · BATH 청소 등</span></label>
        <textarea className="input wf-note" value={note} rows={4} placeholder={'예) 사용횟수 : 105회\nPOLY공정 → TEOS공정 변경'} onChange={ev => setNote(ev.target.value)} />
        {cell && <p className="wf-hint">마지막 수정: {cell.updatedBy} · {cell.updatedAt.slice(0, 16).replace('T', ' ')}</p>}
        <div className="modal-actions">
          {cell && <button className="btn btn-ghost wf-danger" disabled={saving} onClick={() => save(true)}>지우기</button>}
          <span style={{ flex: 1 }} />
          <button className="btn btn-ghost" onClick={onClose}>취소</button>
          <button className="btn btn-primary" disabled={saving} onClick={() => save()}>{saving ? '저장 중…' : '저장'}</button>
        </div>
      </div>
    </div>
  );
}
