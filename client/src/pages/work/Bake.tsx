import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { api } from '../../api/client';
import './Work.css';
import { useAccess } from '../../auth/useAccess';
import { useIsMobile } from '../../hooks/useIsMobile';
import type { BakeDay, BakeImportResult, BakeLog, BakeSave, BakeSearch } from '../../api/types';
import { parseBakeWorkbook } from './bakeImport';

// BAKE OVEN 그을음 기록 — 엑셀 "BAKE OVEN 그을음 현황" 을 옮긴 화면.
// 하루 = 주간·야간 블록(한 교대에 두 번 돌리면 회차 ①②). 가동한 오븐은 한 줄씩, 비가동·HOLD 오븐은 아래에 묶어서 보여 준다.
// 그을음(X 가 아닌 값)·Q'TZ 가루(有)는 빨갛게. '보트 이력' 탭에서 S/N 로 지난 기록과 이상 칸을 찾는다.

const DOW = ['일', '월', '화', '수', '목', '금', '토'];
const SHIFTS = ['주', '야'] as const;
const STATUS = ['비가동', 'HOLD', 'PM'];
const DEF = { soot: 'X', tempUp: 'PN2 30', tempDown: 'CN2 0', tempDown2: 'PN2 30', quartz: '無', note: '정상' };
const pad = (n: number) => String(n).padStart(2, '0');
function todayYmd() { const t = new Date(); return `${t.getFullYear()}-${pad(t.getMonth() + 1)}-${pad(t.getDate())}`; }
function addDays(s: string, n: number) {
  const d = new Date(s + 'T00:00:00'); d.setDate(d.getDate() + n);
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}
const md = (s: string) => `${Number(s.slice(5, 7))}/${Number(s.slice(8, 10))}`;
/** 시각 — 블록 날짜와 같으면 시:분만, 아니면 월/일 붙여서. */
const hm = (at: string | null, base: string) => (!at ? '' : at.slice(0, 10) === base ? at.slice(11, 16) : `${md(at)} ${at.slice(11, 16)}`);
const roundMark = (n: number) => '①②③④⑤⑥⑦⑧⑨'[n - 1] ?? String(n);
const blockKey = (shift: string, round: number) => `${shift}|${round}`;

export default function Bake() {
  const { canEditOffice: canEdit } = useAccess();
  const [tab, setTab] = useState<'day' | 'find'>('day');
  const [date, setDate] = useState(todayYmd());
  const [q, setQ] = useState('');
  const [busy, setBusy] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);
  const [reloadKey, setReloadKey] = useState(0);

  async function importFile(f: File) {
    setBusy(true);
    try {
      const p = await parseBakeWorkbook(f);
      if (p.rows.length === 0) { alert('가져올 칸을 찾지 못했습니다. "(그을음)" 월별 시트가 있는 파일인지 확인하세요.'); return; }
      if (!confirm(`그을음 시트 ${p.sheets}개 · ${p.from} ~ ${p.to}\n블록 ${p.blocks.toLocaleString()}개 · 오븐 칸 ${p.rows.length.toLocaleString()}개\n오븐: ${p.ovens.join(', ')}\n\n가져올까요?`)) return;
      const overwrite = confirm('이미 웹에 적힌 칸은 엑셀 내용으로 덮어쓸까요?\n[확인] 덮어쓰기   [취소] 비어 있는 칸만 채우기');
      const sum = { added: 0, updated: 0, skipped: 0, newEquipment: [] as string[] };
      for (let i = 0; i < p.rows.length; i += 5000) {
        const r = await api.post<BakeImportResult>('/api/worklog/bake/import', { rows: p.rows.slice(i, i + 5000), overwrite });
        sum.added += r.added; sum.updated += r.updated; sum.skipped += r.skipped; sum.newEquipment.push(...r.newEquipment);
      }
      alert(`새로 ${sum.added.toLocaleString()}칸 · 고침 ${sum.updated.toLocaleString()}칸 · 건너뜀 ${sum.skipped.toLocaleString()}칸`
        + (sum.newEquipment.length ? `\n설비 목록에 새로 넣은 오븐: ${sum.newEquipment.join(', ')}` : ''));
      setReloadKey(k => k + 1);
    } catch (e) {
      alert(e instanceof Error ? e.message : '엑셀을 가져오지 못했습니다.');
    } finally { setBusy(false); }
  }

  return (
    <div className="wf-page">
      <header className="pg-header">
        <div>
          <h2>BAKE 그을음 기록</h2>
          <p>교대별 오븐 투입·배출 · 그을음/온도/Q'TZ 확인 · 보트(S/N) 이력</p>
        </div>
        {canEdit && <button className="btn btn-ghost" disabled={busy} onClick={() => fileRef.current?.click()}>{busy ? '가져오는 중…' : '엑셀 가져오기'}</button>}
        <input ref={fileRef} type="file" accept=".xlsx" hidden onChange={e => { const f = e.target.files?.[0]; if (f) void importFile(f); e.target.value = ''; }} />
      </header>
      <div className="pg-body">
        <div className="wf-tabs">
          <button className={tab === 'day' ? 'on' : ''} onClick={() => setTab('day')}>일별 기록</button>
          <button className={tab === 'find' ? 'on' : ''} onClick={() => setTab('find')}>보트 이력·이상 찾기</button>
        </div>
        {tab === 'day'
          ? <BakeDayView key={reloadKey} date={date} setDate={setDate} canEdit={canEdit} onFind={s => { setQ(s); setTab('find'); }} />
          : <BakeFind q={q} setQ={setQ} onOpen={d => { setDate(d); setTab('day'); }} />}
      </div>
    </div>
  );
}

// ───────── 일별 ─────────

interface Blk { shift: string; round: number; rows: BakeLog[] }

function BakeDayView({ date, setDate, canEdit, onFind }: {
  date: string; setDate: (d: string) => void; canEdit: boolean; onFind: (sn: string) => void;
}) {
  const isMobile = useIsMobile();
  const [data, setData] = useState<BakeDay | null>(null);
  const [extra, setExtra] = useState<string[]>([]);          // 아직 칸이 없는 새 회차
  const [edit, setEdit] = useState<{ shift: string; round: number; eqCode: string } | null>(null);

  const load = useCallback(async () => {
    const r = await api.get<BakeDay>(`/api/worklog/bake?date=${date}`);
    setData(cur => (cur === null || r.date === date ? r : cur));
  }, [date]);
  useEffect(() => {
    let alive = true;
    setData(null); setExtra([]);
    api.get<BakeDay>(`/api/worklog/bake?date=${date}`).then(r => { if (alive) setData(r); }).catch(() => { if (alive) setData(null); });
    return () => { alive = false; };
  }, [date]);

  // 블록: 주 ①②… → 야 ①②…  (기록이 없어도 주·야 ① 은 늘 보인다)
  const blocks = useMemo<Blk[]>(() => {
    const m = new Map<string, Blk>();
    for (const s of SHIFTS) m.set(blockKey(s, 1), { shift: s, round: 1, rows: [] });
    for (const k of extra) { const [s, r] = k.split('|'); if (!m.has(k)) m.set(k, { shift: s, round: Number(r), rows: [] }); }
    for (const r of data?.rows ?? []) {
      const k = blockKey(r.shift, r.round);
      if (!m.has(k)) m.set(k, { shift: r.shift, round: r.round, rows: [] });
      m.get(k)!.rows.push(r);
    }
    return [...m.values()].sort((a, b) => (a.shift === b.shift ? a.round - b.round : a.shift === '주' ? -1 : 1));
  }, [data, extra]);

  /** 바로 앞 블록(이어받기용) — 같은 교대 앞 회차 → 같은 날 주간 마지막 회차 → 전날 마지막 기록. */
  function prevOf(b: Blk): BakeLog[] {
    const i = blocks.indexOf(b);
    for (let j = i - 1; j >= 0; j--) if (blocks[j].rows.length) return blocks[j].rows;
    return data?.prevRows ?? [];
  }

  async function inherit(b: Blk) {
    const src = prevOf(b).filter(r => r.status);
    if (src.length === 0) { alert('이어받을 비가동·HOLD 오븐이 앞 기록에 없습니다.'); return; }
    try {
      await api.post('/api/worklog/bake/import', {
        overwrite: false,
        rows: src.map(r => ({ date, shift: b.shift, round: b.round, eqCode: r.eqCode, status: r.status, note: r.note === r.status ? '' : r.note })),
      });
      await load();
    } catch (e) { alert(e instanceof Error ? e.message : '이어받지 못했습니다.'); }
  }
  async function removeRound(b: Blk) {
    if (!confirm(`${b.shift === '주' ? '주간' : '야간'} ${roundMark(b.round)} 회차의 오븐 ${b.rows.length}칸을 모두 지울까요?`)) return;
    try {
      await api.del(`/api/worklog/bake/round?date=${date}&shift=${encodeURIComponent(b.shift)}&round=${b.round}`);
      setExtra(x => x.filter(k => k !== blockKey(b.shift, b.round)));
      await load();
    } catch (e) { alert(e instanceof Error ? e.message : '지우지 못했습니다.'); }
  }
  function addRound(shift: string) {
    const max = Math.max(...blocks.filter(b => b.shift === shift).map(b => b.round));
    if (max >= 9) return;
    setExtra(x => [...x, blockKey(shift, max + 1)]);
  }

  const runs = (data?.rows ?? []).filter(r => !r.status);
  const soot = runs.filter(r => r.hasSoot).length;
  const quartz = runs.filter(r => r.hasQuartz).length;
  const d = new Date(date + 'T00:00:00');
  const itemList = useMemo(() => [...new Set([...(data?.rows ?? []), ...(data?.prevRows ?? [])].map(r => r.item).filter(Boolean))].sort(), [data]);
  const editRow = edit ? data?.rows.find(r => r.shift === edit.shift && r.round === edit.round && r.eqCode === edit.eqCode) : undefined;

  return (
    <>
      <div className="wf-toolbar">
        <div className="wf-monthnav">
          <button onClick={() => setDate(addDays(date, -1))} aria-label="전날">‹</button>
          <input className="input wf-date" type="date" value={date} onChange={e => e.target.value && setDate(e.target.value)} />
          <button onClick={() => setDate(addDays(date, 1))} aria-label="다음날">›</button>
        </div>
        <b className="wf-daylabel">{d.getMonth() + 1}월 {d.getDate()}일 ({DOW[d.getDay()]})</b>
        {date !== todayYmd() && <button className="btn btn-ghost wf-sm" onClick={() => setDate(todayYmd())}>오늘</button>}
        <span className="wf-stat">가동 <b>{runs.length}</b>회</span>
        <span className="wf-stat">그을음 <b className={soot ? 'wf-bad' : ''}>{soot}</b></span>
        <span className="wf-stat">Q'TZ 가루 <b className={quartz ? 'wf-bad' : ''}>{quartz}</b></span>
      </div>

      {!data ? <div className="wf-empty">불러오는 중…</div> : (
        <div className="wf-blocks">
          {blocks.map((b, i) => {
            const last = blocks[i + 1]?.shift !== b.shift;
            const byEq = new Map(b.rows.map(r => [r.eqCode, r]));
            const running = data.ovens.filter(o => byEq.get(o.code) && !byEq.get(o.code)!.status);
            // 비가동·HOLD 등 상태별로 묶고, 적지 않은 오븐은 '미기록'
            const idle = new Map<string, string[]>();
            for (const o of data.ovens) {
              const r = byEq.get(o.code);
              if (r && !r.status) continue;
              const k = r ? r.status : '';
              idle.set(k, [...(idle.get(k) ?? []), o.code]);
            }
            const multi = blocks.filter(x => x.shift === b.shift).length > 1;
            return (
              <section key={blockKey(b.shift, b.round)} className="wf-bblock">
                <div className="wf-bhead">
                  <b className={`wf-shift s${b.shift}`}>{b.shift === '주' ? '주간' : '야간'}{multi && ` ${roundMark(b.round)}`}</b>
                  <span className="wf-dim">{running.length ? `가동 ${running.length}` : b.rows.length ? '가동 없음' : '기록 없음'}</span>
                  <span style={{ flex: 1 }} />
                  {canEdit && b.rows.length === 0 && <button className="btn btn-ghost wf-sm" onClick={() => inherit(b)}>앞 기록 비가동·HOLD 이어받기</button>}
                  {canEdit && last && b.rows.length > 0 && b.round < 9 && <button className="btn btn-ghost wf-sm" onClick={() => addRound(b.shift)}>+ 회차</button>}
                  {canEdit && b.rows.length > 0 && <button className="btn btn-ghost wf-sm wf-danger" onClick={() => removeRound(b)}>회차 지우기</button>}
                </div>

                {running.length > 0 && (isMobile ? (
                  <div className="wf-blist">
                    {running.map(o => {
                      const r = byEq.get(o.code)!;
                      return (
                        <button key={o.code} className={`wf-brow ${r.hasSoot || r.hasQuartz ? 'bad' : ''}`}
                          onClick={() => canEdit && setEdit({ shift: b.shift, round: b.round, eqCode: o.code })}>
                          <span className="wf-bcode">{o.code}</span>
                          <span className="wf-btime">{hm(r.trackIn, date) || '?'} → {hm(r.trackOut, date) || '?'}</span>
                          <Checks r={r} />
                          <span className="wf-bitem">{r.item}{r.serialNo && <> · <u>{r.serialNo}</u></>}</span>
                          <SootLine r={r} />
                          {r.note && r.note !== '정상' && <span className="wf-bitem">{r.note}</span>}
                        </button>
                      );
                    })}
                  </div>
                ) : (
                  <div className="wf-gridwrap wf-bwrap">
                    <table className="wf-wtable wf-btable">
                      <thead><tr>
                        <th>오븐</th><th>TRACK IN</th><th>TRACK OUT</th><th>품명</th><th>S/N</th>
                        <th>그을음</th><th>온도 ↑</th><th>온도 ↓</th><th>Q'TZ</th><th>비고</th>
                      </tr></thead>
                      <tbody>
                        {running.map(o => {
                          const r = byEq.get(o.code)!;
                          return (
                            <tr key={o.code} className={canEdit ? 'editable' : ''} onClick={() => canEdit && setEdit({ shift: b.shift, round: b.round, eqCode: o.code })}>
                              <td className="wf-bcode">{o.code}</td>
                              <td>{hm(r.trackIn, date)}</td><td>{hm(r.trackOut, date)}</td>
                              <td className="wf-bitemcol" title={r.item}>{r.item}</td>
                              <td>{r.serialNo && <button className="wf-link" title="이 보트의 지난 기록" onClick={e => { e.stopPropagation(); onFind(r.serialNo); }}>{r.serialNo}</button>}</td>
                              <td className={r.hasSoot ? 'wf-badcell' : 'wf-ok'} title={r.soot}>{r.soot}</td>
                              <td className="wf-ok">{r.tempUp}</td>
                              <td className="wf-ok">{[r.tempDown, r.tempDown2].filter(Boolean).join(' / ')}</td>
                              <td className={r.hasQuartz ? 'wf-badcell' : 'wf-ok'}>{r.quartz}</td>
                              <td className={r.note === '정상' ? 'wf-ok' : 'wf-wnote'} title={r.note}>{r.note}</td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                  </div>
                ))}

                <div className="wf-bidle">
                  {[...idle.entries()].sort((a, b2) => (a[0] === '' ? 1 : b2[0] === '' ? -1 : 0)).map(([st, codes]) => (
                    <div key={st} className="wf-bidlerow">
                      <span className={`wf-bst ${st ? '' : 'none'}`}>{st || '미기록'}</span>
                      {codes.map(c => (
                        <button key={c} className="wf-bchip" disabled={!canEdit} onClick={() => setEdit({ shift: b.shift, round: b.round, eqCode: c })}>{c}</button>
                      ))}
                    </div>
                  ))}
                </div>
              </section>
            );
          })}
        </div>
      )}

      {edit && (
        <BakeEditor date={date} shift={edit.shift} round={edit.round} eqCode={edit.eqCode} row={editRow} items={itemList}
          onClose={() => setEdit(null)} onSaved={async () => { setEdit(null); await load(); }} />
      )}
    </>
  );
}

/** 그을음·Q'TZ 한눈에 — 정상이면 흐린 글자, 이상이면 빨간 표. */
function Checks({ r }: { r: BakeLog }) {
  return (
    <span className="wf-bchecks">
      {r.hasSoot ? <em className="wf-badtag">그을음</em> : <em className="wf-ok">그을음 {r.soot || '-'}</em>}
      {r.hasQuartz ? <em className="wf-badtag">Q'TZ {r.quartz}</em> : <em className="wf-ok">Q'TZ {r.quartz || '-'}</em>}
    </span>
  );
}

/** 그을음 내용(위치·설명) — 폰 목록에서 배지 옆에 두면 줄이 밀려 따로 한 줄로. */
function SootLine({ r }: { r: BakeLog }) {
  if (!r.hasSoot || r.soot === 'O') return null;
  return <span className="wf-bitem wf-sootline">그을음: {r.soot}</span>;
}

// ───────── 한 칸 입력 ─────────

function BakeEditor({ date, shift, round, eqCode, row, items, onClose, onSaved }: {
  date: string; shift: string; round: number; eqCode: string; row?: BakeLog; items: string[];
  onClose: () => void; onSaved: () => Promise<void>;
}) {
  const fresh = !row;
  const [f, setF] = useState<BakeSave>(() => row
    ? { ...row, trackIn: row.trackIn?.slice(0, 16) ?? null, trackOut: row.trackOut?.slice(0, 16) ?? null }
    : { date, shift, round, eqCode, status: '', trackIn: null, trackOut: null, item: '', serialNo: '', ...DEF });
  const [saving, setSaving] = useState(false);
  const running = !f.status;
  const other = !!f.status && !STATUS.includes(f.status);

  function setStatus(s: string) {
    // 가동으로 바꾸면 비어 있는 확인 칸을 기본값으로 채운다
    if (!s) setF(x => ({ ...x, status: '', soot: x.soot || DEF.soot, tempUp: x.tempUp || DEF.tempUp, tempDown: x.tempDown || DEF.tempDown,
      tempDown2: x.tempDown2 || DEF.tempDown2, quartz: x.quartz || DEF.quartz, note: x.note || DEF.note }));
    else setF(x => ({ ...x, status: s }));
  }
  async function save(clear = false) {
    if (clear && !confirm('이 칸을 지울까요?')) return;
    const body: BakeSave = clear
      ? { date, shift, round, eqCode, status: '', trackIn: null, trackOut: null, item: '', serialNo: '', soot: '', tempUp: '', tempDown: '', tempDown2: '', quartz: '', note: '' }
      : running
        ? { ...f, trackIn: f.trackIn ? `${f.trackIn}:00` : null, trackOut: f.trackOut ? `${f.trackOut}:00` : null }
        // 비가동·HOLD 는 상태와 비고만
        : { ...f, trackIn: null, trackOut: null, item: '', serialNo: '', soot: '', tempUp: '', tempDown: '', tempDown2: '', quartz: '', note: f.note === DEF.note ? '' : f.note };
    if (!clear && running && body.trackIn && body.trackOut && body.trackOut < body.trackIn) { alert('TRACK OUT 이 TRACK IN 보다 빠릅니다.'); return; }
    setSaving(true);
    try {
      await api.put('/api/worklog/bake', body);
      await onSaved();
    } catch (e) {
      alert(e instanceof Error ? e.message : '저장하지 못했습니다.');
    } finally { setSaving(false); }
  }
  const txt = (k: 'item' | 'serialNo' | 'soot' | 'tempUp' | 'tempDown' | 'tempDown2' | 'quartz' | 'note', ph = '', list?: string) => (
    <input className="input" value={f[k]} placeholder={ph} list={list} onChange={e => setF({ ...f, [k]: e.target.value })} />
  );
  const d = new Date(date + 'T00:00:00');
  return (
    <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="modal-box wf-edit wf-bedit">
        <h3>{eqCode}<small>{d.getMonth() + 1}/{d.getDate()} ({DOW[d.getDay()]}) {shift === '주' ? '주간' : '야간'}{round > 1 ? ` ${roundMark(round)}` : ''}</small></h3>
        <div className="wf-quick">
          {['', ...STATUS].map(s => (
            <button key={s || 'run'} type="button" className={`wf-seg ${f.status === s ? 'on' : ''}`} onClick={() => setStatus(s)}>{s || '가동'}</button>
          ))}
          <button type="button" className={`wf-seg ${other ? 'on' : ''}`} onClick={() => !other && setStatus('점검중')}>기타</button>
        </div>
        {other && <label>상태<input className="input" value={f.status} onChange={e => setF({ ...f, status: e.target.value })} /></label>}

        {running && (<>
          <div className="wf-edit-row">
            <label>TRACK IN<input className="input" type="datetime-local" value={f.trackIn ?? ''} onChange={e => setF({ ...f, trackIn: e.target.value || null })} /></label>
            <label>TRACK OUT<input className="input" type="datetime-local" value={f.trackOut ?? ''} onChange={e => setF({ ...f, trackOut: e.target.value || null })} /></label>
          </div>
          <label>품명{txt('item', '(C)BS_BOAT_144_MASKPOLY', 'wf-bake-items')}</label>
          <datalist id="wf-bake-items">{items.map(i => <option key={i} value={i} />)}</datalist>
          <label>S/N{txt('serialNo', 'SM-B65-…')}</label>
          <div className="wf-edit-row wf-keeprow">
            <label>그을음
              <span className="wf-inline">
                <button type="button" className={`wf-seg ${f.soot === 'X' ? 'on' : ''}`} onClick={() => setF({ ...f, soot: 'X' })}>X 없음</button>
                <button type="button" className={`wf-seg bad ${f.soot && f.soot !== 'X' ? 'on' : ''}`} onClick={() => setF({ ...f, soot: f.soot && f.soot !== 'X' ? f.soot : 'O' })}>있음</button>
              </span>
            </label>
            <label>Q'TZ 가루
              <span className="wf-inline">
                <button type="button" className={`wf-seg ${f.quartz === '無' ? 'on' : ''}`} onClick={() => setF({ ...f, quartz: '無' })}>無</button>
                <button type="button" className={`wf-seg bad ${f.quartz === '有' ? 'on' : ''}`} onClick={() => setF({ ...f, quartz: '有' })}>有</button>
              </span>
            </label>
          </div>
          {f.soot && f.soot !== 'X' && <label>그을음 내용{txt('soot', '예: 상판 테두리 그을음')}</label>}
          <div className="wf-edit-row wf-temps wf-keeprow">
            <label>온도 ↑{txt('tempUp')}</label>
            <label>온도 ↓{txt('tempDown')}</label>
            <label>온도 ↓ 2{txt('tempDown2')}</label>
          </div>
        </>)}
        <label>비고{txt('note')}</label>

        <div className="modal-actions">
          {!fresh && <button className="btn btn-ghost wf-danger" disabled={saving} onClick={() => save(true)}>지우기</button>}
          <span style={{ flex: 1 }} />
          <button className="btn btn-ghost" onClick={onClose}>취소</button>
          <button className="btn btn-primary" disabled={saving} onClick={() => save()}>{saving ? '저장 중…' : '저장'}</button>
        </div>
      </div>
    </div>
  );
}

// ───────── 보트 이력·이상 찾기 ─────────

function BakeFind({ q, setQ, onOpen }: { q: string; setQ: (s: string) => void; onOpen: (date: string) => void }) {
  const isMobile = useIsMobile();
  const [issues, setIssues] = useState(!q);
  const [res, setRes] = useState<BakeSearch | null>(null);
  const [err, setErr] = useState('');
  const [input, setInput] = useState(q);

  useEffect(() => {
    let alive = true;
    if (!q.trim() && !issues) { setRes(null); return; }
    setRes(null); setErr('');
    api.get<BakeSearch>(`/api/worklog/bake/search?q=${encodeURIComponent(q.trim())}&issues=${issues}`)
      .then(r => { if (alive) setRes(r); })
      .catch(e => { if (alive) setErr(e instanceof Error ? e.message : '찾지 못했습니다.'); });
    return () => { alive = false; };
  }, [q, issues]);

  return (
    <>
      <form className="wf-toolbar" onSubmit={e => { e.preventDefault(); setQ(input); }}>
        <input className="input wf-bsearch" value={input} placeholder="S/N 또는 품명 일부 (예: SM-B65-1BB12)" onChange={e => setInput(e.target.value)} />
        <button className="btn btn-primary wf-sm" type="submit">찾기</button>
        <label className="wf-chk"><input type="checkbox" checked={issues} onChange={e => setIssues(e.target.checked)} /> 그을음·Q'TZ 가루 있었던 칸만</label>
        {res && <span className="wf-stat">{res.total.toLocaleString()}건{res.total > res.rows.length && ` (최근 ${res.rows.length}건 표시)`}</span>}
      </form>
      {err ? <div className="wf-empty">{err}</div>
        : !q.trim() && !issues ? <div className="wf-empty">S/N 이나 품명을 입력하세요.</div>
        : !res ? <div className="wf-empty">찾는 중…</div>
        : res.rows.length === 0 ? <div className="wf-empty">기록이 없습니다.</div>
        : isMobile ? (
          <div className="wf-blist">
            {res.rows.map((r, i) => (
              <button key={i} className={`wf-brow ${r.hasSoot || r.hasQuartz ? 'bad' : ''}`} onClick={() => onOpen(r.date)}>
                <span className="wf-bcode">{r.date.slice(2).replace(/-/g, '.')} {r.shift}{r.round > 1 ? roundMark(r.round) : ''}</span>
                <span className="wf-btime">{r.eqCode}</span>
                <Checks r={r} />
                <span className="wf-bitem">{r.item} · <u>{r.serialNo}</u></span>
                <SootLine r={r} />
                {r.note && r.note !== '정상' && <span className="wf-bitem">{r.note}</span>}
              </button>
            ))}
          </div>
        ) : (
          <div className="wf-gridwrap">
            <table className="wf-wtable wf-btable">
              <thead><tr><th>날짜</th><th>교대</th><th>오븐</th><th>TRACK IN</th><th>TRACK OUT</th><th>품명</th><th>S/N</th><th>그을음</th><th>Q'TZ</th><th>비고</th></tr></thead>
              <tbody>
                {res.rows.map((r, i) => (
                  <tr key={i} className="editable" onClick={() => onOpen(r.date)} title="그날 기록 보기">
                    <td>{r.date}</td>
                    <td className={`wf-shift s${r.shift}`}>{r.shift}{r.round > 1 ? roundMark(r.round) : ''}</td>
                    <td className="wf-bcode">{r.eqCode}</td>
                    <td>{hm(r.trackIn, r.date)}</td><td>{hm(r.trackOut, r.date)}</td>
                    <td className="wf-bitemcol" title={r.item}>{r.item}</td>
                    <td>{r.serialNo}</td>
                    <td className={r.hasSoot ? 'wf-badcell' : 'wf-ok'} title={r.soot}>{r.soot}</td>
                    <td className={r.hasQuartz ? 'wf-badcell' : 'wf-ok'}>{r.quartz}</td>
                    <td className={r.note === '정상' ? 'wf-ok' : 'wf-wnote'} title={r.note}>{r.note}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
    </>
  );
}
