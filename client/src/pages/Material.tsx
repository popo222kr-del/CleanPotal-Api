import { useEffect, useState, useRef, useCallback } from 'react';
import html2canvas from 'html2canvas';
import { api } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import type { MaterialDay, MaterialRow, MaterialCell, MaterialDestination, Dispatch as DispatchDto } from '../api/types';
import './Material.css';

const QUICK = ['내근', '차량검사소', '천안 ↔ 동탄', '단축근무 (휴무)', '휴무'];
const PAGE_TITLE = '천안사업장 자재 & 물류 일정 현황';

type Period = 'am' | 'pm';

// 목적지에 '휴무'가 들어가면 휴무 칸 (단축근무 (휴무)·휴무 공통)
const isRest = (c: MaterialCell) => c.destination.includes('휴무');

function todayStr(): string {
  const d = new Date();
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

const WEEK = ['일', '월', '화', '수', '목', '금', '토'];
function formatKDate(s: string): string {
  const [y, m, d] = s.split('-').map(Number);
  const dow = WEEK[new Date(y, m - 1, d).getDay()];
  return `${y}년 ${m}월 ${d}일 (${dow})`;
}
function addDays(s: string, delta: number): string {
  const [y, m, d] = s.split('-').map(Number);
  const dt = new Date(y, m - 1, d + delta);
  const p = (n: number) => String(n).padStart(2, '0');
  return `${dt.getFullYear()}-${p(dt.getMonth() + 1)}-${p(dt.getDate())}`;
}

// 차량 라벨 "5t (2255)" → { size: '5t', no: '2255' }
function splitVehicle(label: string): { size: string; no: string } {
  const m = label.match(/^(.*?)\s*\((.*)\)\s*$/);
  return m ? { size: m[1].trim(), no: m[2].trim() } : { size: label, no: '' };
}

export default function Material() {
  const { user } = useAuth();
  const canEditRoster = !!(user?.isAdmin || user?.canManageSchedule);

  const [date, setDate] = useState(todayStr());
  const [data, setData] = useState<MaterialDay | null>(null);
  const [rows, setRows] = useState<MaterialRow[]>([]);
  const [noteAm, setNoteAm] = useState('');
  const [notePm, setNotePm] = useState('');
  const [dirty, setDirty] = useState(false);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [dayDispatch, setDayDispatch] = useState<MaterialDestination[]>([]);   // 이 날짜의 배차표 (인수인계 연동)
  const [openKey, setOpenKey] = useState<string | null>(null);
  const [rosterOpen, setRosterOpen] = useState(false);

  const captureRef = useRef<HTMLDivElement>(null);

  const load = useCallback(async (d: string) => {
    setLoading(true);
    try {
      const day = await api.get<MaterialDay>(`/api/material?date=${d}`);
      setData(day);
      setRows(day.rows.map(r => ({ ...r, am: { ...r.am, vehicles: [...r.am.vehicles] }, pm: { ...r.pm, vehicles: [...r.pm.vehicles] } })));
      setNoteAm(day.noteAm);
      setNotePm(day.notePm);
      setDirty(false);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(date); }, [date, load]);

  // 이 날짜의 배차표 → 목적지 드롭다운 최상단 (인수인계 배차표와 실시간 공유)
  useEffect(() => {
    (async () => {
      try {
        const list = await api.get<DispatchDto[]>(`/api/dispatch/day?date=${date}`);
        const seen = new Set<string>();
        const out: MaterialDestination[] = [];
        for (const d of list) {
          const n = d.vendorName.trim();
          if (!n || seen.has(n)) continue;
          seen.add(n);
          out.push({ name: n, address: d.fullAddress || d.incomingDetails || '' });
        }
        setDayDispatch(out);
      } catch { setDayDispatch([]); }
    })();
  }, [date]);

  // 미저장 변경 경고 (앱 종료/닫기)
  useEffect(() => {
    const h = (e: BeforeUnloadEvent) => { if (dirty) { e.preventDefault(); e.returnValue = ''; } };
    window.addEventListener('beforeunload', h);
    return () => window.removeEventListener('beforeunload', h);
  }, [dirty]);

  // 날짜 변경 등 이동 전 미저장 경고
  function guard(fn: () => void) {
    if (dirty && !window.confirm('저장하지 않은 변경사항이 있습니다. 계속하시겠습니까?')) return;
    fn();
  }
  const goPrev = () => guard(() => setDate(d => addDays(d, -1)));
  const goNext = () => guard(() => setDate(d => addDays(d, 1)));
  const goToday = () => guard(() => setDate(todayStr()));
  const pickDate = (v: string) => { if (v) guard(() => setDate(v)); };

  function updateCell(i: number, p: Period, patch: Partial<MaterialCell>) {
    setRows(rs => rs.map((r, idx) => idx === i ? { ...r, [p]: { ...r[p], ...patch } } : r));
    setDirty(true);
  }
  function setDestination(i: number, p: Period, value: string) {
    // 휴무로 바뀌면 차량 배정 해제 (휴무 칸은 배차 불가)
    updateCell(i, p, value.includes('휴무') ? { destination: value, vehicles: [] } : { destination: value });
  }
  function toggleVehicle(i: number, p: Period, key: string) {
    setRows(rs => rs.map((r, idx) => {
      if (idx !== i) return r;
      const cell = r[p];
      if (isRest(cell)) return r; // 휴무 칸은 차량 선택 비활성화
      const has = cell.vehicles.includes(key);
      return { ...r, [p]: { ...cell, vehicles: has ? cell.vehicles.filter(v => v !== key) : [...cell.vehicles, key] } };
    }));
    setDirty(true);
  }

  async function save() {
    setSaving(true);
    try {
      const payload = {
        rows: rows.map(r => ({
          person: r.person,
          am: { destination: r.am.destination, vehicles: r.am.vehicles },
          pm: { destination: r.pm.destination, vehicles: r.pm.vehicles },
        })),
        noteAm, notePm,
      };
      const day = await api.put<MaterialDay>(`/api/material?date=${date}`, payload);
      setData(day);
      setDirty(false);
    } finally {
      setSaving(false);
    }
  }

  async function reloadAfterRoster() {
    setRosterOpen(false);
    await load(date);
  }

  // ── 캡처 (공유용 단일 이미지) ──
  const [capturing, setCapturing] = useState(false);
  async function renderCanvas(): Promise<HTMLCanvasElement | null> {
    if (!captureRef.current) return null;
    return html2canvas(captureRef.current, {
      backgroundColor: null,    // 라운드 모서리 밖은 투명
      scale: Math.max(2, window.devicePixelRatio || 1),  // 고해상도(선명)
      useCORS: true,
      logging: false,
    });
  }
  async function copyImage() {
    setCapturing(true);
    try {
      const canvas = await renderCanvas();
      if (!canvas) return;
      const blob: Blob | null = await new Promise(res => canvas.toBlob(res, 'image/png'));
      if (!blob) return;
      if (navigator.clipboard && 'write' in navigator.clipboard) {
        await navigator.clipboard.write([new ClipboardItem({ 'image/png': blob })]);
        alert('이미지가 클립보드에 복사되었습니다. 메신저에 붙여넣으세요.');
      } else {
        downloadBlob(blob);
      }
    } catch {
      // 클립보드 권한이 없으면 파일 다운로드로 대체
      try {
        const canvas = await renderCanvas();
        const blob: Blob | null = canvas ? await new Promise(res => canvas.toBlob(res, 'image/png')) : null;
        if (blob) downloadBlob(blob);
        else alert('이미지 생성에 실패했습니다.');
      } catch { alert('이미지 생성에 실패했습니다.'); }
    } finally {
      setCapturing(false);
    }
  }
  function downloadBlob(blob: Blob) {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `자재물류_${date}.png`;
    a.click();
    URL.revokeObjectURL(url);
  }

  const vehicles = data?.vehicles ?? [];
  const empty = !!data && data.roster.length === 0;

  return (
    <div className="mat" style={{ fontSize: 16 }}>
      <header className="mat-head">
        <h2>{PAGE_TITLE}</h2>
        <div className="mat-actions">
          <button className="btn btn-ghost" onClick={copyImage} disabled={capturing}>이미지 복사</button>
          <button className="btn btn-primary" onClick={save} disabled={saving || !dirty}>
            {saving ? '저장 중…' : dirty ? '저장 *' : '저장됨'}
          </button>
        </div>
      </header>

      <div className="mat-body">
        {/* 날짜 네비게이션 (알약) */}
        <div className="mat-datebar">
          <button className="mat-nav" onClick={goPrev} title="이전 날짜">◀</button>
          <div className="mat-datepill">
            <span className="mat-datetxt">{formatKDate(date)}</span>
            <input className="mat-dateinput" type="date" value={date} onChange={e => pickDate(e.target.value)} />
          </div>
          <button className="mat-nav" onClick={goNext} title="다음 날짜">▶</button>
          <button className="mat-today" onClick={goToday}>오늘</button>
          {canEditRoster && (
            <button className="btn btn-ghost mat-roster-btn" onClick={() => guard(() => setRosterOpen(true))}>인원 관리</button>
          )}
          {dirty && <span className="mat-dirty">● 미저장</span>}
        </div>

        {loading && <div className="mat-loading">불러오는 중…</div>}
        {empty && (
          <div className="mat-empty">
            담당자 로스터가 비어 있습니다. {canEditRoster ? '"인원 관리"에서 담당자를 추가하세요.' : '관리자에게 담당자 등록을 요청하세요.'}
          </div>
        )}

        {!loading && !empty && data && (
          <div className="mat-tablewrap">
            <table className="mat-table">
              <thead>
                <tr>
                  <th className="c-name" rowSpan={2}>담당자<div className="c-sub">자재 / 물류</div></th>
                  <th className="grp am" colSpan={6}>오전 (AM)</th>
                  <th className="grp pm" colSpan={6}>오후 (PM)</th>
                </tr>
                <tr>
                  {(['am', 'pm'] as Period[]).map(p => (
                    <PeriodHeaders key={p} vehicles={vehicles} />
                  ))}
                </tr>
              </thead>
              <tbody>
                {rows.map((r, i) => {
                  const dimName = isRest(r.am) && isRest(r.pm);
                  return (
                    <tr key={r.person}>
                      <td className={`c-name ${dimName ? 'dim' : ''}`}>{r.person}</td>
                      {(['am', 'pm'] as Period[]).map(p => (
                        <PeriodCells
                          key={p} rowIdx={i} period={p} cell={r[p]} vehicles={vehicles}
                          openKey={openKey} setOpenKey={setOpenKey} dayDispatch={dayDispatch}
                          onDest={setDestination} onToggleVehicle={toggleVehicle} />
                      ))}
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}

        {/* 특이사항 — 오전/오후 좌우 분리 (표의 AM/PM과 정렬) */}
        {!loading && !empty && (
          <div className="mat-notes">
            <div className="mat-note">
              <label>오전 (AM) 특이사항</label>
              <textarea value={noteAm} onChange={e => { setNoteAm(e.target.value); setDirty(true); }} rows={3} />
            </div>
            <div className="mat-note">
              <label>오후 (PM) 특이사항</label>
              <textarea value={notePm} onChange={e => { setNotePm(e.target.value); setDirty(true); }} rows={3} />
            </div>
          </div>
        )}
      </div>

      {/* 캡처 전용 읽기 뷰 (화면 밖) */}
      <div className="mat-capture-holder" aria-hidden>
        <MaterialCapture ref={captureRef} date={date} rows={rows} vehicles={vehicles} noteAm={noteAm} notePm={notePm} />
      </div>

      {rosterOpen && data && (
        <RosterEditor initial={data.roster} onClose={() => setRosterOpen(false)} onSaved={reloadAfterRoster} />
      )}
    </div>
  );
}

function PeriodHeaders({ vehicles }: { vehicles: { key: string; label: string }[] }) {
  return (
    <>
      <th className="c-dest">목적지 &amp; 근무</th>
      {vehicles.map(v => {
        const { size, no } = splitVehicle(v.label);
        return <th key={v.key} className="c-veh"><div className="veh-size">{size}</div><div className="veh-no">{no}</div></th>;
      })}
    </>
  );
}

function PeriodCells({ rowIdx, period, cell, vehicles, openKey, setOpenKey, dayDispatch, onDest, onToggleVehicle }: {
  rowIdx: number; period: Period; cell: MaterialCell; vehicles: { key: string; label: string }[];
  openKey: string | null; setOpenKey: (k: string | null) => void;
  dayDispatch: MaterialDestination[];
  onDest: (i: number, p: Period, v: string) => void;
  onToggleVehicle: (i: number, p: Period, key: string) => void;
}) {
  const rest = isRest(cell);
  const key = `${rowIdx}:${period}`;
  const open = openKey === key;
  return (
    <>
      <td className={`c-dest ${rest ? 'rest' : ''}`}>
        <div className="mat-dest">
          <input
            className="mat-dest-input" value={cell.destination}
            placeholder="목적지 & 근무"
            onChange={e => onDest(rowIdx, period, e.target.value)} />
          <button className="mat-dest-btn" title="배차표에서 불러오기 / 빠른 선택"
            onClick={() => setOpenKey(open ? null : key)}>▾</button>
          {open && (
            <>
              <div className="mat-backdrop" onClick={() => setOpenKey(null)} />
              <div className="mat-dropdown">
                <div className="dd-sec dd-today">🚚 이 날짜 배차표</div>
                {dayDispatch.length === 0 && <div className="dd-empty">이 날짜 배차 없음</div>}
                {dayDispatch.map((d, idx) => (
                  <button key={`dd-${idx}`} className="dd-item dd-dest" title={d.name}
                    onClick={() => { onDest(rowIdx, period, d.name); setOpenKey(null); }}>
                    <span className="dd-name">{d.name}</span>
                    {d.address && <span className="dd-addr">{d.address}</span>}
                  </button>
                ))}
                <div className="dd-sec">빠른 선택</div>
                {QUICK.map(q => (
                  <button key={q} className="dd-item" onClick={() => { onDest(rowIdx, period, q); setOpenKey(null); }}>{q}</button>
                ))}
              </div>
            </>
          )}
        </div>
      </td>
      {vehicles.map(v => {
        const on = cell.vehicles.includes(v.key);
        return (
          <td key={v.key}
            className={`c-veh ${rest ? 'rest' : ''} ${on ? 'on' : ''}`}
            onClick={() => !rest && onToggleVehicle(rowIdx, period, v.key)}>
            {on ? '●' : ''}
          </td>
        );
      })}
    </>
  );
}

// ── 캡처 전용 읽기 뷰 ──
const MaterialCapture = ({ ref, date, rows, vehicles, noteAm, notePm }: {
  ref: React.Ref<HTMLDivElement>;
  date: string; rows: MaterialRow[]; vehicles: { key: string; label: string }[];
  noteAm: string; notePm: string;
}) => {
  return (
    <div className="mat-capture" ref={ref}>
      <div className="cap-title">{PAGE_TITLE}</div>
      <div className="cap-date">{formatKDate(date)}</div>
      <table className="cap-table">
        <thead>
          <tr>
            <th rowSpan={2}>담당자</th>
            <th className="grp am" colSpan={vehicles.length + 1}>오전 (AM)</th>
            <th className="grp pm" colSpan={vehicles.length + 1}>오후 (PM)</th>
          </tr>
          <tr>
            {(['am', 'pm'] as Period[]).map(p => (
              <CapHeaders key={p} vehicles={vehicles} />
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map(r => {
            const dim = isRest(r.am) && isRest(r.pm);
            return (
              <tr key={r.person}>
                <td className={`cap-name ${dim ? 'dim' : ''}`}>{r.person}</td>
                {(['am', 'pm'] as Period[]).map(p => (
                  <CapCells key={p} cell={r[p]} vehicles={vehicles} />
                ))}
              </tr>
            );
          })}
        </tbody>
      </table>
      <div className="cap-notes">
        <div className="cap-note"><b>오전 특이사항</b><span>{noteAm || '-'}</span></div>
        <div className="cap-note"><b>오후 특이사항</b><span>{notePm || '-'}</span></div>
      </div>
    </div>
  );
};

function CapHeaders({ vehicles }: { vehicles: { key: string; label: string }[] }) {
  return (
    <>
      <th className="cap-dest">목적지 &amp; 근무</th>
      {vehicles.map(v => {
        const { size, no } = splitVehicle(v.label);
        return <th key={v.key} className="cap-veh"><div>{size}</div><div className="cap-vno">{no}</div></th>;
      })}
    </>
  );
}
function CapCells({ cell, vehicles }: { cell: MaterialCell; vehicles: { key: string; label: string }[] }) {
  const rest = isRest(cell);
  return (
    <>
      <td className={`cap-dest ${rest ? 'rest' : ''}`}>{cell.destination}</td>
      {vehicles.map(v => (
        <td key={v.key} className={`cap-veh ${rest ? 'rest' : ''}`}>{cell.vehicles.includes(v.key) ? '●' : ''}</td>
      ))}
    </>
  );
}

// ── 인원(로스터) 관리 모달 ──
function RosterEditor({ initial, onClose, onSaved }: {
  initial: string[]; onClose: () => void; onSaved: () => void;
}) {
  const [names, setNames] = useState<string[]>(initial);
  const [saving, setSaving] = useState(false);

  const setName = (i: number, v: string) => setNames(ns => ns.map((n, idx) => idx === i ? v : n));
  const add = () => setNames(ns => [...ns, '']);
  const remove = (i: number) => setNames(ns => ns.filter((_, idx) => idx !== i));
  const move = (i: number, delta: number) => setNames(ns => {
    const j = i + delta;
    if (j < 0 || j >= ns.length) return ns;
    const copy = [...ns];
    [copy[i], copy[j]] = [copy[j], copy[i]];
    return copy;
  });

  async function save() {
    setSaving(true);
    try {
      await api.put('/api/material/roster', { names: names.map(n => n.trim()).filter(Boolean) });
      onSaved();
    } catch {
      alert('저장 권한이 없거나 오류가 발생했습니다.');
      setSaving(false);
    }
  }

  return (
    <div className="mat-modal-backdrop" onClick={onClose}>
      <div className="mat-modal" onClick={e => e.stopPropagation()}>
        <div className="mat-modal-head">
          <h3>담당자 인원 관리</h3>
          <button className="mat-modal-x" onClick={onClose}>✕</button>
        </div>
        <p className="mat-modal-hint">추가 · 삭제 · 순서 변경 · 이름 수정 후 저장하면 표에 반영됩니다.</p>
        <div className="mat-roster-list">
          {names.map((n, i) => (
            <div key={i} className="mat-roster-row">
              <span className="mat-roster-idx">{i + 1}</span>
              <input className="input" value={n} placeholder="이름" onChange={e => setName(i, e.target.value)} />
              <button className="mat-mini" title="위로" onClick={() => move(i, -1)} disabled={i === 0}>▲</button>
              <button className="mat-mini" title="아래로" onClick={() => move(i, 1)} disabled={i === names.length - 1}>▼</button>
              <button className="mat-mini del" title="삭제" onClick={() => remove(i)}>✕</button>
            </div>
          ))}
          {names.length === 0 && <div className="mat-empty">담당자가 없습니다. 아래에서 추가하세요.</div>}
        </div>
        <div className="mat-modal-foot">
          <button className="btn btn-ghost" onClick={add}>+ 담당자 추가</button>
          <div style={{ flex: 1 }} />
          <button className="btn btn-ghost" onClick={onClose}>취소</button>
          <button className="btn btn-primary" onClick={save} disabled={saving}>{saving ? '저장 중…' : '저장'}</button>
        </div>
      </div>
    </div>
  );
}
