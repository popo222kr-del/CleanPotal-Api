import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { api } from '../../api/client';
import './Work.css';
import { useAccess } from '../../auth/useAccess';
import { useAuth } from '../../auth/AuthContext';
import { useIsMobile } from '../../hooks/useIsMobile';
import type {
  ScrapBatch, ScrapBatchSummary, ScrapCircle, ScrapImportResult, ScrapItem, ScrapItemSave, ScrapMaterial,
  ScrapSearch, ScrapTag, ScrapTagSave,
} from '../../api/types';
import { parseScrapWorkbook } from './scrapImport';

// 폐기품 관리 — 엑셀 "폐기품 LIST(.xlsm)" 을 옮긴 화면.
// 엑셀: 폐기품_생산 에 적고 → '생성' 매크로로 라인 순 물류용 LIST 출력 → 상차 후 '초기화' 로 이력에 붙여 쌓기.
// 웹: LIST 하나에 줄을 적고(엑셀에서 붙여넣기 가능) → 라인 순으로 보고/인쇄 → 폰에서 매칭·상차를 바로 체크 → '상차 완료' 로 닫기.
// 눈관리 요청: 실물 S/N 미확인·실물 상이 제품 — 분임조를 고르면 라인·담당자가 채워지고 라벨을 인쇄한다.

const pad = (n: number) => String(n).padStart(2, '0');
function todayYmd() { const t = new Date(); return `${t.getFullYear()}-${pad(t.getMonth() + 1)}-${pad(t.getDate())}`; }
const md = (s: string) => `${Number(s.slice(5, 7))}/${Number(s.slice(8, 10))}`;
/** 라인 이름 자연 정렬 — 7LINE < 16LINE < P1-1 LINE < S3 LINE */
const lineCmp = (a: string, b: string) => a.localeCompare(b, 'ko', { numeric: true, sensitivity: 'base' });
const isScrap = (remark: string) => remark.includes('폐기품');
const TAG_NOTES = ['실물 SN 미확인, 눈관리 부착후 출하 요망', '실물 상이함', '사용횟수 초과 - 부적합 반입'];

export default function Scrap() {
  const { canEditOffice: canEdit } = useAccess();
  const [tab, setTab] = useState<'list' | 'tags' | 'find'>('list');
  const [batchId, setBatchId] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);
  const fileRef = useRef<HTMLInputElement>(null);

  async function importFile(f: File) {
    setBusy(true);
    try {
      const p = await parseScrapWorkbook(f);
      const items = p.batches.reduce((s, b) => s + b.items.length, 0);
      if (p.batches.length === 0 && p.tags.length === 0) { alert('가져올 내용을 찾지 못했습니다. 폐기품 LIST 엑셀(.xlsm)인지 확인하세요.'); return; }
      if (!confirm(`폐기품 LIST ${p.batches.length}개(${items.toLocaleString()}줄) · 눈관리 ${p.tags.length}줄 · 분임조 ${p.circles.length}개를 가져옵니다.\n이미 웹에 있는 LIST(날짜·S/N 이 같은 것)와 같은 눈관리 줄은 건너뜁니다.`)) return;
      const r = await api.post<ScrapImportResult>('/api/worklog/scrap/import', p);
      alert(`LIST ${r.batches}개(${r.items.toLocaleString()}줄) · 눈관리 ${r.tags}줄 · 분임조 ${r.circles}개를 넣었습니다.`
        + (r.skippedBatches || r.skippedTags ? `\n(이미 있어 건너뜀: LIST ${r.skippedBatches}개 · 눈관리 ${r.skippedTags}줄)` : ''));
      setBatchId(null);
      setReloadKey(k => k + 1);
    } catch (e) {
      alert(e instanceof Error ? e.message : '엑셀을 가져오지 못했습니다.');
    } finally { setBusy(false); }
  }

  return (
    <div className="wf-page">
      <header className="pg-header">
        <div>
          <h2>폐기품 관리</h2>
          <p>폐기품 LIST(라인 순·매칭/상차 체크·이력) · 제품 눈관리 요청</p>
        </div>
        {canEdit && <button className="btn btn-ghost" disabled={busy} onClick={() => fileRef.current?.click()}>{busy ? '가져오는 중…' : '엑셀 가져오기'}</button>}
        <input ref={fileRef} type="file" accept=".xlsm,.xlsx" hidden onChange={e => { const f = e.target.files?.[0]; if (f) void importFile(f); e.target.value = ''; }} />
      </header>
      <div className="pg-body">
        <div className="wf-tabs">
          <button className={tab === 'list' ? 'on' : ''} onClick={() => setTab('list')}>폐기품 LIST</button>
          <button className={tab === 'tags' ? 'on' : ''} onClick={() => setTab('tags')}>눈관리 요청</button>
          <button className={tab === 'find' ? 'on' : ''} onClick={() => setTab('find')}>찾기</button>
        </div>
        {tab === 'list' && <ScrapLists key={reloadKey} canEdit={canEdit} batchId={batchId} setBatchId={setBatchId} />}
        {tab === 'tags' && <ScrapTags key={reloadKey} canEdit={canEdit} />}
        {tab === 'find' && <ScrapFind onOpen={id => { setBatchId(id); setTab('list'); }} />}
      </div>
    </div>
  );
}

// ───────── 폐기품 LIST ─────────

function ScrapLists({ canEdit, batchId, setBatchId }: { canEdit: boolean; batchId: number | null; setBatchId: (id: number | null) => void }) {
  const isMobile = useIsMobile();
  const [list, setList] = useState<ScrapBatchSummary[] | null>(null);
  const [batch, setBatch] = useState<ScrapBatch | null>(null);
  const [filter, setFilter] = useState<'all' | 'unloaded' | 'unmatched'>('all');
  const [byLine, setByLine] = useState(true);
  const [edit, setEdit] = useState<ScrapItem | 'new' | null>(null);
  const [paste, setPaste] = useState(false);
  const [head, setHead] = useState(false);

  const loadList = useCallback(async () => {
    const l = await api.get<ScrapBatchSummary[]>('/api/worklog/scrap/batches');
    setList(l);
    return l;
  }, []);
  useEffect(() => {
    loadList().then(l => { if (batchId === null && l.length) setBatchId(l[0].id); }).catch(() => setList([]));
  }, [loadList]);   // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => {
    let alive = true;
    if (batchId === null) { setBatch(null); return; }
    api.get<ScrapBatch>(`/api/worklog/scrap/batches/${batchId}`).then(b => { if (alive) setBatch(b); }).catch(() => { if (alive) setBatch(null); });
    return () => { alive = false; };
  }, [batchId]);

  async function reload() {
    if (batchId !== null) setBatch(await api.get<ScrapBatch>(`/api/worklog/scrap/batches/${batchId}`));
    await loadList();
  }
  /** 매칭·상차 체크 — 바로 저장(화면 먼저 바꾸고 실패하면 되돌림). */
  async function toggle(it: ScrapItem, k: 'matched' | 'loaded') {
    if (!canEdit || !batch) return;
    const next = { ...it, [k]: !it[k] };
    setBatch({ ...batch, items: batch.items.map(x => (x.id === it.id ? next : x)) });
    try {
      await api.put(`/api/worklog/scrap/items/${it.id}`, toSave(next));
      loadList().catch(() => {});
    } catch (e) {
      alert(e instanceof Error ? e.message : '저장하지 못했습니다.');
      setBatch(b => (b ? { ...b, items: b.items.map(x => (x.id === it.id ? it : x)) } : b));
    }
  }
  async function setClosed(closed: boolean) {
    if (!batch) return;
    const left = batch.items.filter(i => !i.loaded).length;
    if (closed && !confirm(left ? `상차 체크가 안 된 줄이 ${left}개 있습니다. 그래도 상차 완료로 닫을까요?` : '상차 완료로 닫고 이력으로 넘길까요?')) return;
    try {
      setBatch(await api.put<ScrapBatch>(`/api/worklog/scrap/batches/${batch.id}`, { date: batch.date, title: batch.title, note: batch.note, isClosed: closed }));
      await loadList();
    } catch (e) { alert(e instanceof Error ? e.message : '저장하지 못했습니다.'); }
  }
  async function removeBatch() {
    if (!batch || !confirm(`${batch.title || md(batch.date)} LIST(${batch.items.length}줄)를 지울까요? 되돌릴 수 없습니다.`)) return;
    try {
      await api.del(`/api/worklog/scrap/batches/${batch.id}`);
      const l = await loadList();
      setBatchId(l[0]?.id ?? null);
    } catch (e) { alert(e instanceof Error ? e.message : '지우지 못했습니다.'); }
  }

  const items = useMemo(() => {
    const all = (batch?.items ?? []).filter(i => filter === 'all' || (filter === 'unloaded' ? !i.loaded : !i.matched));
    return byLine ? [...all].sort((a, b) => lineCmp(a.line, b.line) || a.sortOrder - b.sortOrder) : all;
  }, [batch, filter, byLine]);
  const total = batch?.items.length ?? 0;
  const matched = batch?.items.filter(i => i.matched).length ?? 0;
  const loaded = batch?.items.filter(i => i.loaded).length ?? 0;
  const open = !!batch && !batch.isClosed;
  // 라인 순일 때 라인마다 머리 줄
  const groups: { line: string; rows: ScrapItem[] }[] = [];
  for (const it of items) {
    const g = groups[groups.length - 1];
    if (byLine && g && g.line === it.line) g.rows.push(it); else groups.push({ line: byLine ? it.line : '', rows: [it] });
  }
  const noOf = new Map(items.map((it, i) => [it.id, i + 1]));

  if (!list) return <div className="wf-empty">불러오는 중…</div>;
  return (
    <>
      <div className="wf-toolbar wf-noprint">
        <select className="input wf-bsel" value={batchId ?? ''} onChange={e => setBatchId(e.target.value ? Number(e.target.value) : null)}>
          {list.length === 0 && <option value="">LIST 없음</option>}
          {list.map(b => (
            <option key={b.id} value={b.id}>
              {b.isClosed ? '' : '● '}{b.date} {b.isClosed ? '상차 완료' : '작성 중'} · {b.count}줄
            </option>
          ))}
        </select>
        {canEdit && <button className="btn btn-ghost wf-sm" onClick={() => setHead(true)}>+ 새 LIST</button>}
        {batch && <>
          <span className="wf-stat">전체 <b>{total}</b></span>
          <span className="wf-stat">매칭 <b className={matched === total ? 'wf-good' : ''}>{matched}</b>/{total}</span>
          <span className="wf-stat">상차 <b className={loaded === total ? 'wf-good' : ''}>{loaded}</b>/{total}</span>
        </>}
      </div>

      {!batch ? <div className="wf-empty">{list.length ? '불러오는 중…' : '폐기품 LIST 가 없습니다. 새 LIST 를 만들거나 엑셀을 가져오세요.'}</div> : (<>
        <div className="wf-shead wf-noprint">
          <b>{batch.title || `${batch.date} 폐기품 LIST`}</b>
          <span className={`wf-sbadge ${batch.isClosed ? 'closed' : ''}`}>{batch.isClosed ? `상차 완료${batch.closedBy ? ` · ${batch.closedBy}` : ''}` : '작성 중'}</span>
          {batch.note && <span className="wf-dim">{batch.note}</span>}
          <span style={{ flex: 1 }} />
          {canEdit && open && <button className="btn btn-ghost wf-sm" onClick={() => setEdit('new')}>+ 줄</button>}
          {canEdit && open && !isMobile && <button className="btn btn-ghost wf-sm" onClick={() => setPaste(true)}>엑셀에서 붙여넣기</button>}
          {!isMobile && <button className="btn btn-ghost wf-sm" onClick={() => window.print()}>인쇄</button>}
          {canEdit && (open
            ? <button className="btn btn-primary wf-sm" onClick={() => setClosed(true)}>상차 완료</button>
            : <button className="btn btn-ghost wf-sm" onClick={() => setClosed(false)}>다시 열기</button>)}
          {canEdit && open && <button className="btn btn-ghost wf-sm wf-danger" onClick={removeBatch}>LIST 지우기</button>}
        </div>
        <div className="wf-toolbar wf-noprint">
          {(['all', 'unloaded', 'unmatched'] as const).map(k => (
            <button key={k} className={`wf-seg ${filter === k ? 'on' : ''}`} onClick={() => setFilter(k)}>
              {k === 'all' ? '전체' : k === 'unloaded' ? `상차 안 됨 ${total - loaded}` : `매칭 안 됨 ${total - matched}`}
            </button>
          ))}
          <label className="wf-chk"><input type="checkbox" checked={byLine} onChange={e => setByLine(e.target.checked)} /> 라인 순</label>
        </div>

        {items.length === 0 ? <div className="wf-empty wf-noprint">{total ? '해당하는 줄이 없습니다.' : '줄이 없습니다.'}</div> : isMobile ? (
          <div className="wf-slist">
            {groups.map((g, gi) => (
              <div key={gi} className="wf-sgroup">
                {g.line && <div className="wf-sline">{g.line} <span>{g.rows.length}</span></div>}
                {g.rows.map(it => (
                  <div key={it.id} className={`wf-scard ${it.loaded ? 'done' : ''}`}>
                    <button className="wf-sinfo" onClick={() => canEdit && setEdit(it)}>
                      <b>{noOf.get(it.id)}. {it.serialNo || '(S/N 없음)'}</b>
                      <span>{it.matDesc}</span>
                      <span className="wf-dim">{it.outNo}{!byLine && it.line ? ` · ${it.line}` : ''}</span>
                      {it.remark && <span className={isScrap(it.remark) ? 'wf-scrap' : 'wf-dim'}>{it.remark}</span>}
                    </button>
                    <div className="wf-stog">
                      <button className={it.matched ? 'on' : ''} disabled={!canEdit} onClick={() => toggle(it, 'matched')}>{it.matched ? '✓ ' : ''}매칭</button>
                      <button className={it.loaded ? 'on' : ''} disabled={!canEdit} onClick={() => toggle(it, 'loaded')}>{it.loaded ? '✓ ' : ''}상차</button>
                    </div>
                  </div>
                ))}
              </div>
            ))}
          </div>
        ) : (
          <div className="wf-gridwrap wf-print">
            <div className="wf-ptitle">
              <b>{batch.title || `${batch.date} 폐기품 LIST`}</b>
              <span>폐기품 상차 시 꼭 상차된 것만 Check 할 것!</span>
            </div>
            <table className="wf-wtable wf-stable">
              <thead><tr>
                <th>NO</th><th>LINE</th><th>MAT ID</th><th>MAT DESC</th><th>S/N</th><th>실물/전산<br />매칭 확인</th><th>OUT NO</th><th>상차</th><th>특이사항</th>
              </tr></thead>
              <tbody>
                {groups.map((g, gi) => g.rows.map((it, i) => (
                  <tr key={it.id} className={`${canEdit ? 'editable' : ''} ${i === 0 && gi > 0 && byLine ? 'first' : ''}`} onClick={() => canEdit && setEdit(it)}>
                    <td className="n">{noOf.get(it.id)}</td>
                    <td className="wf-bcode">{it.line}</td>
                    <td>{it.matId}</td>
                    <td className="wf-bitemcol" title={it.matDesc}>{it.matDesc}</td>
                    <td>{it.serialNo}</td>
                    <td className="c" onClick={e => { e.stopPropagation(); void toggle(it, 'matched'); }}>
                      <span className={`wf-box ${it.matched ? 'on' : ''}`}>{it.matched ? '✓' : ''}</span>
                    </td>
                    <td>{it.outNo}</td>
                    <td className="c" onClick={e => { e.stopPropagation(); void toggle(it, 'loaded'); }}>
                      <span className={`wf-box ${it.loaded ? 'on' : ''}`}>{it.loaded ? '✓' : ''}</span>
                    </td>
                    <td className={isScrap(it.remark) ? 'wf-scrap' : ''}>{it.remark}</td>
                  </tr>
                )))}
              </tbody>
            </table>
          </div>
        )}
      </>)}

      {edit && batch && (
        <ScrapItemEditor batch={batch} item={edit === 'new' ? null : edit}
          onClose={() => setEdit(null)} onSaved={async () => { setEdit(null); await reload(); }} />
      )}
      {paste && batch && <ScrapPaste batchId={batch.id} onClose={() => setPaste(false)} onSaved={async () => { setPaste(false); await reload(); }} />}
      {head && (
        <NewBatch onClose={() => setHead(false)} onSaved={async id => { setHead(false); setBatchId(id); await loadList(); }} />
      )}
    </>
  );
}

const toSave = (i: ScrapItem): ScrapItemSave => ({
  line: i.line, matId: i.matId, matDesc: i.matDesc, serialNo: i.serialNo, outNo: i.outNo, matched: i.matched, loaded: i.loaded, remark: i.remark,
});

function NewBatch({ onClose, onSaved }: { onClose: () => void; onSaved: (id: number) => Promise<void> }) {
  const [date, setDate] = useState(todayYmd());
  const [note, setNote] = useState('');
  const [saving, setSaving] = useState(false);
  async function save() {
    setSaving(true);
    try {
      const b = await api.post<ScrapBatch>('/api/worklog/scrap/batches', { date, title: `${date} 폐기품 LIST`, note, isClosed: false });
      await onSaved(b.id);
    } catch (e) { alert(e instanceof Error ? e.message : '만들지 못했습니다.'); } finally { setSaving(false); }
  }
  return (
    <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="modal-box wf-edit">
        <h3>새 폐기품 LIST</h3>
        <label>상차 날짜<input className="input" type="date" value={date} onChange={e => setDate(e.target.value)} /></label>
        <label>메모<input className="input" value={note} placeholder="(선택)" onChange={e => setNote(e.target.value)} /></label>
        <div className="modal-actions">
          <span style={{ flex: 1 }} />
          <button className="btn btn-ghost" onClick={onClose}>취소</button>
          <button className="btn btn-primary" disabled={saving || !date} onClick={save}>만들기</button>
        </div>
      </div>
    </div>
  );
}

let materialCache: Promise<ScrapMaterial[]> | null = null;
function useMaterials() {
  const [m, setM] = useState<ScrapMaterial[]>([]);
  useEffect(() => {
    materialCache ??= api.get<ScrapMaterial[]>('/api/worklog/scrap/materials').catch(() => { materialCache = null; return []; });
    materialCache.then(setM);
  }, []);
  return m;
}

function ScrapItemEditor({ batch, item, onClose, onSaved }: {
  batch: ScrapBatch; item: ScrapItem | null; onClose: () => void; onSaved: () => Promise<void>;
}) {
  const mats = useMaterials();
  const lines = useMemo(() => [...new Set(batch.items.map(i => i.line).filter(Boolean))].sort(lineCmp), [batch]);
  const [f, setF] = useState<ScrapItemSave>(item ? toSave(item)
    : { line: '', matId: '', matDesc: '', serialNo: '', outNo: '', matched: false, loaded: false, remark: '폐기품' });
  const [saving, setSaving] = useState(false);
  const locked = batch.isClosed;   // 상차 완료 LIST 는 체크·특이사항만

  function setMatId(v: string) {
    const hit = mats.find(m => m.matId === v.trim());
    setF(x => ({ ...x, matId: v, matDesc: hit && !x.matDesc ? hit.matDesc : x.matDesc }));
  }
  async function save(del = false) {
    if (del && !confirm('이 줄을 지울까요?')) return;
    setSaving(true);
    try {
      if (del && item) await api.del(`/api/worklog/scrap/items/${item.id}`);
      else if (item) await api.put(`/api/worklog/scrap/items/${item.id}`, f);
      else await api.post(`/api/worklog/scrap/batches/${batch.id}/items`, { items: [f] });
      await onSaved();
    } catch (e) { alert(e instanceof Error ? e.message : '저장하지 못했습니다.'); } finally { setSaving(false); }
  }
  const inp = (k: 'line' | 'matDesc' | 'serialNo' | 'outNo' | 'remark', ph = '', list?: string) => (
    <input className="input" value={f[k]} placeholder={ph} list={list} disabled={locked && k !== 'remark'} onChange={e => setF({ ...f, [k]: e.target.value })} />
  );
  return (
    <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="modal-box wf-edit">
        <h3>{item ? '줄 고치기' : '줄 추가'}<small>{batch.title || batch.date}</small></h3>
        <div className="wf-edit-row">
          <label>LINE{inp('line', '예: P1-1 LINE', 'wf-scrap-lines')}</label>
          <label>MAT ID<input className="input" value={f.matId} disabled={locked} list="wf-scrap-mats" onChange={e => setMatId(e.target.value)} /></label>
        </div>
        <datalist id="wf-scrap-lines">{lines.map(l => <option key={l} value={l} />)}</datalist>
        <datalist id="wf-scrap-mats">{mats.map(m => <option key={m.matId} value={m.matId}>{m.matDesc}</option>)}</datalist>
        <label>MAT DESC{inp('matDesc', '(C)BQ_BOAT_ZRO_LASER')}</label>
        <div className="wf-edit-row">
          <label>S/N{inp('serialNo')}</label>
          <label>OUT NO{inp('outNo', 'C0204…-1')}</label>
        </div>
        <label>특이사항{inp('remark', '폐기품 / 부적합반입+눈관리', 'wf-scrap-remarks')}</label>
        <datalist id="wf-scrap-remarks">{['폐기품', '부적합반입', '부적합반입+눈관리'].map(r => <option key={r} value={r} />)}</datalist>
        <div className="wf-quick">
          <button type="button" className={`wf-seg ${f.matched ? 'on' : ''}`} onClick={() => setF({ ...f, matched: !f.matched })}>{f.matched ? '✓ ' : ''}매칭 확인</button>
          <button type="button" className={`wf-seg ${f.loaded ? 'on' : ''}`} onClick={() => setF({ ...f, loaded: !f.loaded })}>{f.loaded ? '✓ ' : ''}상차</button>
        </div>
        <div className="modal-actions">
          {item && !locked && <button className="btn btn-ghost wf-danger" disabled={saving} onClick={() => save(true)}>지우기</button>}
          <span style={{ flex: 1 }} />
          <button className="btn btn-ghost" onClick={onClose}>취소</button>
          <button className="btn btn-primary" disabled={saving} onClick={() => save()}>{saving ? '저장 중…' : '저장'}</button>
        </div>
      </div>
    </div>
  );
}

/**
 * 엑셀에서 복사한 줄 붙여넣기. 엑셀 폐기품 LIST 의 NO~특이사항(9칸) 그대로, 또는
 * LINE·MAT ID·MAT DESC·S/N·OUT NO·특이사항(6칸 이하) 순서. 머리글 줄(NO/LINE)은 건너뛴다.
 */
function parsePaste(raw: string): ScrapItemSave[] {
  return raw.split(/\r?\n/).map(l => l.split('\t').map(c => c.trim())).filter(c => c.some(Boolean))
    .filter(c => !/^(NO|LINE)$/i.test(c[0]) && !/^LINE$/i.test(c[1] ?? ''))
    .map(c => {
      const nine = c.length >= 8 && /^\d*$/.test(c[0]);
      const [line, matId, matDesc, serialNo, outNo, remark] = nine
        ? [c[1], c[2], c[3], c[4], c[6], c[8]]
        : [c[0], c[1], c[2], c[3], c[4], c[5]];
      const ok = (v?: string) => /^(O|V|✓)$/i.test(v ?? '');
      return { line: line ?? '', matId: matId ?? '', matDesc: matDesc ?? '', serialNo: serialNo ?? '', outNo: outNo ?? '',
        matched: nine && ok(c[5]), loaded: nine && ok(c[7]), remark: remark ?? '' };
    })
    .filter(r => r.line || r.matId || r.matDesc || r.serialNo || r.outNo);
}

function ScrapPaste({ batchId, onClose, onSaved }: { batchId: number; onClose: () => void; onSaved: () => Promise<void> }) {
  const [raw, setRaw] = useState('');
  const [saving, setSaving] = useState(false);
  const rows = useMemo(() => parsePaste(raw), [raw]);
  async function save() {
    setSaving(true);
    try { await api.post(`/api/worklog/scrap/batches/${batchId}/items`, { items: rows }); await onSaved(); }
    catch (e) { alert(e instanceof Error ? e.message : '넣지 못했습니다.'); } finally { setSaving(false); }
  }
  return (
    <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="modal-box wf-eq">
        <h3>엑셀에서 붙여넣기<small>폐기품 LIST 의 NO~특이사항 칸, 또는 LINE·MAT ID·MAT DESC·S/N·OUT NO·특이사항 순서</small></h3>
        <textarea className="input wf-note" rows={8} value={raw} placeholder="엑셀에서 줄을 복사해 여기에 붙여넣으세요" onChange={e => setRaw(e.target.value)} />
        {rows.length > 0 && (
          <div className="wf-eq-wrap">
            <table className="wf-eq-table">
              <thead><tr><th>LINE</th><th>MAT ID</th><th>MAT DESC</th><th>S/N</th><th>OUT NO</th><th>특이사항</th></tr></thead>
              <tbody>{rows.slice(0, 200).map((r, i) => (
                <tr key={i}><td>{r.line}</td><td>{r.matId}</td><td>{r.matDesc}</td><td>{r.serialNo}</td><td>{r.outNo}</td><td>{r.remark}</td></tr>
              ))}</tbody>
            </table>
          </div>
        )}
        <div className="modal-actions">
          <span className="wf-dim">{rows.length}줄</span>
          <span style={{ flex: 1 }} />
          <button className="btn btn-ghost" onClick={onClose}>취소</button>
          <button className="btn btn-primary" disabled={saving || rows.length === 0} onClick={save}>{rows.length}줄 넣기</button>
        </div>
      </div>
    </div>
  );
}

// ───────── 눈관리 요청 ─────────

function ScrapTags({ canEdit }: { canEdit: boolean }) {
  const isMobile = useIsMobile();
  const [tags, setTags] = useState<ScrapTag[] | null>(null);
  const [circles, setCircles] = useState<ScrapCircle[]>([]);
  const [openOnly, setOpenOnly] = useState(false);
  const [edit, setEdit] = useState<ScrapTag | 'new' | null>(null);
  const [label, setLabel] = useState<ScrapTag | null>(null);
  const [circleEdit, setCircleEdit] = useState(false);

  const load = useCallback(async () => {
    const [t, c] = await Promise.all([api.get<ScrapTag[]>('/api/worklog/scrap/tags'), api.get<ScrapCircle[]>('/api/worklog/scrap/circles')]);
    setTags(t); setCircles(c);
  }, []);
  useEffect(() => { load().catch(() => setTags([])); }, [load]);

  async function toggleDone(t: ScrapTag) {
    if (!canEdit) return;
    const next = { ...t, done: !t.done };
    setTags(ts => ts?.map(x => (x.id === t.id ? next : x)) ?? ts);
    try { await api.put(`/api/worklog/scrap/tags/${t.id}`, next); }
    catch (e) { alert(e instanceof Error ? e.message : '저장하지 못했습니다.'); setTags(ts => ts?.map(x => (x.id === t.id ? t : x)) ?? ts); }
  }

  const shown = (tags ?? []).filter(t => !openOnly || !t.done);
  const left = (tags ?? []).filter(t => !t.done).length;
  return (
    <>
      <div className="wf-toolbar wf-noprint">
        {canEdit && <button className="btn btn-primary wf-sm" onClick={() => setEdit('new')}>+ 눈관리 요청</button>}
        <button className="btn btn-ghost wf-sm" onClick={() => setCircleEdit(true)}>분임조 담당자 표</button>
        <label className="wf-chk"><input type="checkbox" checked={openOnly} onChange={e => setOpenOnly(e.target.checked)} /> 완료 안 된 것만</label>
        {tags && <span className="wf-stat">전체 <b>{tags.length}</b> · 미완료 <b className={left ? 'wf-bad' : ''}>{left}</b></span>}
      </div>
      {!tags ? <div className="wf-empty">불러오는 중…</div> : shown.length === 0 ? <div className="wf-empty">눈관리 요청이 없습니다.</div> : isMobile ? (
        <div className="wf-slist">
          {shown.map(t => (
            <div key={t.id} className={`wf-scard ${t.done ? 'done' : ''}`}>
              <button className="wf-sinfo" onClick={() => canEdit && setEdit(t)}>
                <b>{t.serialNo || t.item}</b>
                <span>{t.item}</span>
                <span className="wf-dim">{md(t.date)} · {t.writer} → {[t.line, t.circle, t.owner].filter(Boolean).join(' · ')}</span>
                {t.note && <span className="wf-dim">{t.note}</span>}
              </button>
              <div className="wf-stog">
                <button className={t.done ? 'on' : ''} disabled={!canEdit} onClick={() => toggleDone(t)}>{t.done ? '✓ ' : ''}완료</button>
                <button onClick={() => setLabel(t)}>라벨</button>
              </div>
            </div>
          ))}
        </div>
      ) : (
        <div className="wf-gridwrap">
          <table className="wf-wtable wf-stable">
            <thead><tr><th>작성일</th><th>작성자</th><th>품명</th><th>S/N</th><th>LINE</th><th>분임조</th><th>담당자</th><th>비고</th><th>완료</th><th></th></tr></thead>
            <tbody>
              {shown.map(t => (
                <tr key={t.id} className={`${canEdit ? 'editable' : ''} ${t.done ? 'wf-sdone' : ''}`} onClick={() => canEdit && setEdit(t)}>
                  <td>{t.date}</td><td>{t.writer}</td>
                  <td className="wf-bitemcol" title={t.item}>{t.item}</td>
                  <td className="wf-bcode">{t.serialNo}</td><td>{t.line}</td><td>{t.circle}</td><td>{t.owner}</td>
                  <td className="wf-wnote" title={t.note}>{t.note}</td>
                  <td className="c" onClick={e => { e.stopPropagation(); void toggleDone(t); }}><span className={`wf-box ${t.done ? 'on' : ''}`}>{t.done ? '✓' : ''}</span></td>
                  <td><button className="wf-link" onClick={e => { e.stopPropagation(); setLabel(t); }}>라벨</button></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {edit && <TagEditor tag={edit === 'new' ? null : edit} circles={circles} onClose={() => setEdit(null)} onSaved={async () => { setEdit(null); await load(); }} />}
      {label && <TagLabel tag={label} onClose={() => setLabel(null)} />}
      {circleEdit && <CircleEditor circles={circles} canEdit={canEdit} onClose={() => setCircleEdit(false)} onSaved={async () => { setCircleEdit(false); await load(); }} />}
    </>
  );
}

function TagEditor({ tag, circles, onClose, onSaved }: { tag: ScrapTag | null; circles: ScrapCircle[]; onClose: () => void; onSaved: () => Promise<void> }) {
  const { user } = useAuth();
  const [f, setF] = useState<ScrapTagSave>(tag
    ? { date: tag.date, writer: tag.writer, item: tag.item, serialNo: tag.serialNo, line: tag.line, circle: tag.circle, owner: tag.owner, note: tag.note, done: tag.done }
    : { date: todayYmd(), writer: user?.realName || user?.username || '', item: '', serialNo: '', line: '', circle: '', owner: '', note: TAG_NOTES[0], done: false });
  const [saving, setSaving] = useState(false);
  function pickCircle(name: string) {
    const c = circles.find(x => x.name === name);
    setF(x => ({ ...x, circle: name, line: c?.line ?? x.line, owner: c?.owner ?? x.owner }));
  }
  async function save(del = false) {
    if (del && !confirm('이 눈관리 요청을 지울까요?')) return;
    setSaving(true);
    try {
      if (del && tag) await api.del(`/api/worklog/scrap/tags/${tag.id}`);
      else if (tag) await api.put(`/api/worklog/scrap/tags/${tag.id}`, f);
      else await api.post('/api/worklog/scrap/tags', f);
      await onSaved();
    } catch (e) { alert(e instanceof Error ? e.message : '저장하지 못했습니다.'); } finally { setSaving(false); }
  }
  const inp = (k: 'writer' | 'item' | 'serialNo' | 'line' | 'owner' | 'note', ph = '', list?: string) => (
    <input className="input" value={f[k]} placeholder={ph} list={list} onChange={e => setF({ ...f, [k]: e.target.value })} />
  );
  return (
    <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="modal-box wf-edit">
        <h3>{tag ? '눈관리 요청 고치기' : '눈관리 요청'}</h3>
        <div className="wf-edit-row">
          <label>작성 일자<input className="input" type="date" value={f.date} onChange={e => setF({ ...f, date: e.target.value })} /></label>
          <label>작성자{inp('writer')}</label>
        </div>
        <label>품명{inp('item', '(C)300mm QTZ INNER TUBE_HFOX_TEL')}</label>
        <label>S/N{inp('serialNo')}</label>
        <div className="wf-edit-row">
          <label>분임조
            <input className="input" value={f.circle} list="wf-circles" onChange={e => pickCircle(e.target.value)} />
          </label>
          <label>LINE{inp('line')}</label>
          <label>담당자{inp('owner')}</label>
        </div>
        <datalist id="wf-circles">{circles.map(c => <option key={c.id} value={c.name}>{c.line} · {c.owner}</option>)}</datalist>
        <label>비고{inp('note', '', 'wf-tag-notes')}</label>
        <datalist id="wf-tag-notes">{TAG_NOTES.map(n => <option key={n} value={n} />)}</datalist>
        <label className="wf-chk"><input type="checkbox" checked={f.done} onChange={e => setF({ ...f, done: e.target.checked })} /> 완료</label>
        <div className="modal-actions">
          {tag && <button className="btn btn-ghost wf-danger" disabled={saving} onClick={() => save(true)}>지우기</button>}
          <span style={{ flex: 1 }} />
          <button className="btn btn-ghost" onClick={onClose}>취소</button>
          <button className="btn btn-primary" disabled={saving} onClick={() => save()}>{saving ? '저장 중…' : '저장'}</button>
        </div>
      </div>
    </div>
  );
}

/** 제품에 붙이는 눈관리 표 — 엑셀 눈관리 LIST 위쪽 출력 영역과 같은 모양. */
function TagLabel({ tag, onClose }: { tag: ScrapTag; onClose: () => void }) {
  const who = [tag.line, tag.circle, tag.owner].filter(Boolean).join('_');
  return (
    <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="modal-box wf-edit wf-labelbox">
        <div className="wf-label wf-print">
          <div className="wf-label-head">담당자 확인 제품<small>({tag.note || '실물 SN 미확인, 눈관리 부착후 출하 요망'})</small></div>
          <div className="wf-label-row">{tag.item}</div>
          <div className="wf-label-row">{tag.serialNo}</div>
          <div className="wf-label-row">{who}{tag.owner ? ' 님' : ''}</div>
        </div>
        <div className="modal-actions wf-noprint">
          <span style={{ flex: 1 }} />
          <button className="btn btn-ghost" onClick={onClose}>닫기</button>
          <button className="btn btn-primary" onClick={() => window.print()}>인쇄</button>
        </div>
      </div>
    </div>
  );
}

function CircleEditor({ circles, canEdit, onClose, onSaved }: { circles: ScrapCircle[]; canEdit: boolean; onClose: () => void; onSaved: () => Promise<void> }) {
  const [rows, setRows] = useState<ScrapCircle[]>(circles.length ? circles : [{ id: 0, name: '', line: '', owner: '' }]);
  const [saving, setSaving] = useState(false);
  const set = (i: number, k: 'name' | 'line' | 'owner', v: string) => setRows(r => r.map((x, j) => (j === i ? { ...x, [k]: v } : x)));
  async function save() {
    setSaving(true);
    try { await api.put('/api/worklog/scrap/circles', { items: rows.filter(r => r.name.trim()) }); await onSaved(); }
    catch (e) { alert(e instanceof Error ? e.message : '저장하지 못했습니다.'); } finally { setSaving(false); }
  }
  return (
    <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="modal-box wf-eq">
        <h3>분임조 담당자 표<small>눈관리 요청에서 분임조를 고르면 LINE·담당자가 채워집니다</small></h3>
        <div className="wf-eq-wrap">
          <table className="wf-eq-table">
            <thead><tr><th>분임조</th><th>LINE</th><th>담당자</th><th /></tr></thead>
            <tbody>
              {rows.map((r, i) => (
                <tr key={i}>
                  <td><input className="input" value={r.name} disabled={!canEdit} onChange={e => set(i, 'name', e.target.value)} /></td>
                  <td><input className="input" value={r.line} disabled={!canEdit} onChange={e => set(i, 'line', e.target.value)} /></td>
                  <td><input className="input" value={r.owner} disabled={!canEdit} onChange={e => set(i, 'owner', e.target.value)} /></td>
                  <td>{canEdit && <button className="wf-link wf-danger" onClick={() => setRows(x => x.filter((_, j) => j !== i))}>빼기</button>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <div className="modal-actions">
          {canEdit && <button className="btn btn-ghost" onClick={() => setRows(r => [...r, { id: 0, name: '', line: '', owner: '' }])}>+ 분임조</button>}
          <span style={{ flex: 1 }} />
          <button className="btn btn-ghost" onClick={onClose}>{canEdit ? '취소' : '닫기'}</button>
          {canEdit && <button className="btn btn-primary" disabled={saving} onClick={save}>저장</button>}
        </div>
      </div>
    </div>
  );
}

// ───────── 찾기 ─────────

function ScrapFind({ onOpen }: { onOpen: (batchId: number) => void }) {
  const isMobile = useIsMobile();
  const [input, setInput] = useState('');
  const [res, setRes] = useState<ScrapSearch | null>(null);
  const [err, setErr] = useState('');
  async function find() {
    setErr(''); setRes(null);
    try { setRes(await api.get<ScrapSearch>(`/api/worklog/scrap/search?q=${encodeURIComponent(input.trim())}`)); }
    catch (e) { setErr(e instanceof Error ? e.message : '찾지 못했습니다.'); }
  }
  return (
    <>
      <form className="wf-toolbar" onSubmit={e => { e.preventDefault(); void find(); }}>
        <input className="input wf-bsearch" value={input} placeholder="S/N · OUT NO · MAT DESC 일부" onChange={e => setInput(e.target.value)} />
        <button className="btn btn-primary wf-sm" type="submit">찾기</button>
        {res && <span className="wf-stat">폐기품 <b>{res.items.length}</b>줄 · 눈관리 <b>{res.tags.length}</b>줄</span>}
      </form>
      {err && <div className="wf-empty">{err}</div>}
      {res && (res.items.length + res.tags.length === 0 ? <div className="wf-empty">찾은 기록이 없습니다.</div> : (
        <div className="wf-blocks">
          {res.items.length > 0 && (isMobile ? (
            <div className="wf-slist">
              {res.items.map(h => (
                <button key={h.item.id} className="wf-scard wf-sinfo" onClick={() => onOpen(h.batchId)}>
                  <b>{h.item.serialNo}</b>
                  <span>{h.item.matDesc}</span>
                  <span className="wf-dim">{h.batchDate} LIST · {h.item.line} · {h.item.outNo} · {h.item.loaded ? '상차 ✓' : '상차 안 됨'}</span>
                </button>
              ))}
            </div>
          ) : (
            <div className="wf-gridwrap">
              <table className="wf-wtable wf-stable">
                <thead><tr><th>LIST</th><th>LINE</th><th>MAT DESC</th><th>S/N</th><th>OUT NO</th><th>매칭</th><th>상차</th><th>특이사항</th></tr></thead>
                <tbody>
                  {res.items.map(h => (
                    <tr key={h.item.id} className="editable" onClick={() => onOpen(h.batchId)} title="이 LIST 열기">
                      <td>{h.batchDate}{h.batchClosed ? '' : ' (작성 중)'}</td><td>{h.item.line}</td>
                      <td className="wf-bitemcol">{h.item.matDesc}</td><td className="wf-bcode">{h.item.serialNo}</td><td>{h.item.outNo}</td>
                      <td className="c">{h.item.matched ? '✓' : ''}</td><td className="c">{h.item.loaded ? '✓' : ''}</td>
                      <td className={isScrap(h.item.remark) ? 'wf-scrap' : ''}>{h.item.remark}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ))}
          {res.tags.length > 0 && (
            <div className="wf-bblock">
              <div className="wf-bhead"><b>눈관리 요청</b></div>
              {res.tags.map(t => (
                <div key={t.id} className="wf-brow">
                  <span className="wf-bcode">{t.serialNo}</span><span>{t.item}</span>
                  <span className="wf-bitem">{t.date} · {t.writer} → {[t.line, t.circle, t.owner].filter(Boolean).join(' · ')} · {t.note} {t.done ? '· 완료' : ''}</span>
                </div>
              ))}
            </div>
          )}
        </div>
      ))}
    </>
  );
}
