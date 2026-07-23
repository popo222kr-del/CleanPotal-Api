import { useEffect, useRef, useState, useCallback } from 'react';
import { useAccess } from '../auth/useAccess';
import { api } from '../api/client';
import type { Report, ReportGroup } from '../api/types';
import './Weekly.css';

// ── WPF WeeklyReportView 이식: 주차 자동 생성·이월·상태 통계·전역 검색·보고표 ──

const STATUSES = ['진행 중', '보류', '종결'] as const;
type Status = typeof STATUSES[number];

type LBlock = { category: string; status: string; content: string; followUp: string; atts: string[] };
type HitBlock = { number: number; category: string; status: string; content: string; followUp: string; followUpAttachments: string };
type SearchHit = { reportId: number; reportShortTitle: string; reportTitle: string; dateRange: string; block: HitBlock };

// 이미지 축소 (인수인계와 동일)
const MAX_DIM = 1400;
function resizeDataUrl(dataUrl: string): Promise<string> {
  return new Promise(res => {
    const img = new Image();
    img.onload = () => {
      const scale = Math.min(1, MAX_DIM / Math.max(img.width, img.height));
      const w = Math.round(img.width * scale), h = Math.round(img.height * scale);
      const cv = document.createElement('canvas');
      cv.width = w; cv.height = h;
      const ctx = cv.getContext('2d');
      if (!ctx) { res(dataUrl); return; }
      ctx.drawImage(img, 0, 0, w, h);
      try { res(cv.toDataURL('image/jpeg', 0.72)); } catch { res(dataUrl); }
    };
    img.onerror = () => res(dataUrl);
    img.src = dataUrl;
  });
}
function fileToDataUrl(file: File): Promise<string> {
  return new Promise((res, rej) => {
    const fr = new FileReader();
    fr.onload = () => res(fr.result as string);
    fr.onerror = rej;
    fr.readAsDataURL(file);
  });
}
function parseAtts(s: string): string[] {
  if (!s) return [];
  try {
    const v = JSON.parse(s);
    return Array.isArray(v) ? v.filter((x): x is string => typeof x === 'string') : [];
  } catch { return []; }
}

// ── 주차 계산 (WPF GetCurrentWeekOfMonth / GetDateRangeForWeek 포트) ──
function weekOfMonth(d: Date): number {
  const first = new Date(d.getFullYear(), d.getMonth(), 1);
  let dow = first.getDay();
  if (dow === 0) dow = 7;
  return Math.floor((d.getDate() + dow - 2) / 7) + 1;
}
function rangeForWeek(y: number, m: number, w: number): string {
  const first = new Date(y, m - 1, 1);
  let dow = first.getDay();
  if (dow === 0) dow = 7;
  const mon = new Date(y, m - 1, 1 + (w - 1) * 7 - (dow - 1));
  const fri = new Date(mon); fri.setDate(mon.getDate() + 4);
  const f = (d: Date) => `${d.getFullYear()}.${String(d.getMonth() + 1).padStart(2, '0')}.${String(d.getDate()).padStart(2, '0')}`;
  return `${f(mon)} ~ ${f(fri)}`;
}

// 보고표/엑셀 표시용 — 구획 제목만 붙이고 본문은 작성한 그대로 (강제 •/→ 없음)
function formatted(content: string, followUp: string): string {
  const parts: string[] = [];
  if (content.trim()) parts.push(`【기존 내용】\n${content.trim()}`);
  if (followUp.trim()) parts.push(`【팔로업】\n${followUp.trim()}`);
  return parts.join('\n\n');
}

// 내용에 맞춰 늘어나는 textarea
function AutoTA({ value, onChange, placeholder, className }: {
  value: string; onChange: (v: string) => void; placeholder?: string; className?: string;
}) {
  const ref = useRef<HTMLTextAreaElement>(null);
  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    el.style.height = 'auto';
    el.style.height = `${el.scrollHeight + 2}px`;
  }, [value]);
  return <textarea ref={ref} rows={2} className={className} value={value} placeholder={placeholder}
    onChange={e => onChange(e.target.value)} />;
}

export default function Weekly() {
  const { canEditHandover, canEditOffice } = useAccess();
  const canEdit = canEditHandover || canEditOffice;
  const [groups, setGroups] = useState<ReportGroup[]>([]);
  const [cur, setCur] = useState<Report | null>(null);
  const [blocks, setBlocks] = useState<LBlock[]>([]);
  const [memo, setMemo] = useState('');
  const [memoAtts, setMemoAtts] = useState<string[]>([]);
  // 자동 저장
  const [dirty, setDirty] = useState(false);
  const [saving, setSaving] = useState(false);
  const [saveErr, setSaveErr] = useState(false);
  // 상태 필터 (통계 알약 클릭)
  const [statusFilter, setStatusFilter] = useState<Set<string>>(new Set());
  // 전역 검색
  const [q, setQ] = useState('');
  const [hits, setHits] = useState<SearchHit[]>([]);
  // 새 보고서 모달 (년/월/주차)
  const now = new Date();
  const [newOpen, setNewOpen] = useState(false);
  const [nY, setNY] = useState(now.getFullYear());
  const [nM, setNM] = useState(now.getMonth() + 1);
  const [nW, setNW] = useState(weekOfMonth(now));
  const [creating, setCreating] = useState(false);
  // 보고표
  const [tableOpen, setTableOpen] = useState(false);
  const [zoom, setZoom] = useState(1);
  // 이미지 확대 / 첨부 대상
  const [preview, setPreview] = useState<string | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);
  const attTarget = useRef<number | 'memo'>('memo');
  const dragIdx = useRef<number | null>(null);
  const booted = useRef(false);

  const load = useCallback(async () => {
    const g = await api.get<ReportGroup[]>('/api/reports?type=weekly');
    setGroups(g);
    return g;
  }, []);

  // ── 저장 (자동) ──
  async function save() {
    if (!cur || !canEdit) return;
    setSaving(true);
    try {
      const body = {
        ...cur,
        reportType: 'weekly',
        memo,
        memoAttachments: JSON.stringify(memoAtts),
        blocks: blocks.map((b, i) => ({
          id: 0, number: i + 1, category: b.category, status: b.status,
          content: b.content, contentRich: '', followUp: b.followUp, followUpRich: '',
          kind: '', heading: '', isCollapsed: false, progressPercent: 0, importance: '',
          followUpAttachments: JSON.stringify(b.atts),
        })),
      };
      await api.put(`/api/reports/${cur.id}`, body);
      setDirty(false); setSaveErr(false);
      load();
    } catch {
      setSaveErr(true);
    } finally {
      setSaving(false);
    }
  }
  const saveRef = useRef(save);
  saveRef.current = save;

  useEffect(() => {
    if (!dirty) return;
    const t = window.setTimeout(() => { saveRef.current(); }, saveErr ? 3000 : 1000);
    return () => window.clearTimeout(t);
  }, [dirty, blocks, memo, memoAtts, saveErr]);

  useEffect(() => {
    const h = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') { e.preventDefault(); saveRef.current(); }
    };
    window.addEventListener('keydown', h);
    return () => window.removeEventListener('keydown', h);
  }, []);
  useEffect(() => {
    const h = (e: BeforeUnloadEvent) => { if (dirty) { e.preventDefault(); e.returnValue = ''; } };
    window.addEventListener('beforeunload', h);
    return () => window.removeEventListener('beforeunload', h);
  }, [dirty]);

  const open = useCallback(async (id: number) => {
    if (dirty) await saveRef.current();
    const r = await api.get<Report>(`/api/reports/${id}`);
    setCur(r);
    setBlocks(r.blocks.map(b => ({
      category: b.category, status: STATUSES.includes(b.status as Status) ? b.status : '진행 중',
      content: b.content, followUp: b.followUp, atts: parseAtts(b.followUpAttachments),
    })));
    setMemo(r.memo);
    setMemoAtts(parseAtts(r.memoAttachments));
    setDirty(false); setSaveErr(false); setStatusFilter(new Set()); setQ('');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [dirty]);

  // 첫 진입: 최신 주차 자동 열기
  useEffect(() => {
    load().then(g => {
      if (booted.current) return;
      booted.current = true;
      const flat = g.flatMap(x => x.reports);
      if (flat.length === 0) return;
      const latest = flat.reduce((a, b) => (b.dateRange > a.dateRange ? b : a));
      open(latest.id);
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // ── 전역 검색 (디바운스) ──
  useEffect(() => {
    const key = q.trim();
    if (!key) { setHits([]); return; }
    const t = window.setTimeout(async () => {
      try { setHits(await api.get<SearchHit[]>(`/api/reports/search?type=weekly&q=${encodeURIComponent(key)}`)); }
      catch { setHits([]); }
    }, 300);
    return () => window.clearTimeout(t);
  }, [q]);

  // ── 블록 편집 ──
  function mark() { setDirty(true); }
  function setBlock(i: number, patch: Partial<LBlock>) {
    setBlocks(bs => bs.map((b, idx) => idx === i ? { ...b, ...patch } : b)); mark();
  }
  function addBlock() {
    setBlocks(bs => [...bs, { category: '', status: '진행 중', content: '', followUp: '', atts: [] }]); mark();
  }
  function delBlock(i: number) {
    if (!confirm(`#${i + 1} 블록을 삭제할까요?`)) return;
    setBlocks(bs => bs.filter((_, idx) => idx !== i)); mark();
  }
  function moveBlock(from: number, to: number) {
    if (from === to) return;
    setBlocks(bs => {
      const next = [...bs];
      const [item] = next.splice(from, 1);
      next.splice(to, 0, item);
      return next;
    });
    mark();
  }

  // ── 첨부 ──
  async function addImages(target: number | 'memo', files: File[]) {
    const imgs = files.filter(f => f.type.startsWith('image/'));
    if (imgs.length === 0) return;
    const urls: string[] = [];
    for (const f of imgs) urls.push(await resizeDataUrl(await fileToDataUrl(f)));
    if (target === 'memo') setMemoAtts(a => [...a, ...urls]);
    else setBlocks(bs => bs.map((b, idx) => idx === target ? { ...b, atts: [...b.atts, ...urls] } : b));
    mark();
  }
  function onPasteTo(target: number | 'memo') {
    return (e: React.ClipboardEvent) => {
      const files = Array.from(e.clipboardData.files).filter(f => f.type.startsWith('image/'));
      if (files.length) { e.preventDefault(); addImages(target, files); }
    };
  }
  function pickImages(target: number | 'memo') {
    attTarget.current = target;
    fileRef.current?.click();
  }
  function removeAtt(target: number | 'memo', ai: number) {
    if (target === 'memo') setMemoAtts(a => a.filter((_, idx) => idx !== ai));
    else setBlocks(bs => bs.map((b, idx) => idx === target ? { ...b, atts: b.atts.filter((_, j) => j !== ai) } : b));
    mark();
  }

  // ── 새 보고서 (주차 자동 + 이월) ──
  async function confirmCreate() {
    if (creating) return;
    const title = `${nY % 100}년 ${nM}월 ${nW}주차`;
    const flat = groups.flatMap(g => g.reports);
    const existing = flat.find(r => r.title === title);
    if (existing) {
      alert(`이미 '${title}' 보고서가 있습니다. 해당 보고서로 이동합니다.`);
      setNewOpen(false);
      open(existing.id);
      return;
    }
    setCreating(true);
    try {
      if (dirty) await saveRef.current();
      // 직전 보고서의 미종결 블록 이월 (WPF: 종결/보류 제외, 첨부 포함)
      let carried: Report['blocks'] = [];
      if (flat.length > 0) {
        const latest = flat.reduce((a, b) => (b.dateRange > a.dateRange ? b : a));
        const full = await api.get<Report>(`/api/reports/${latest.id}`);
        carried = full.blocks.filter(b => b.status !== '종결' && b.status !== '보류');
      }
      const body = {
        reportType: 'weekly',
        monthTitle: `${nY}년 ${nM}월`, title, shortTitle: `${nW}주차`, dateRange: rangeForWeek(nY, nM, nW),
        memo: '', memoRich: '', mainContent: '', mainContentRich: '', nightContent: '', nightContentRich: '',
        attendees: '', summary: '', memoAttachments: '', mainAttachments: '',
        blocks: carried.map((b, i) => ({ ...b, id: 0, number: i + 1 })),
      };
      const r = await api.post<Report>('/api/reports', body);
      await load();
      setNewOpen(false);
      open(r.id);
    } catch (err) {
      alert(err instanceof Error ? err.message : '보고서 생성에 실패했습니다.');
    } finally {
      setCreating(false);
    }
  }

  async function delReport() {
    if (!cur || !canEdit) return;
    if (!confirm(`'${cur.title}' 보고서를 삭제할까요?\n블록 ${blocks.length}개가 함께 삭제됩니다.`)) return;
    await api.del(`/api/reports/${cur.id}`);
    setCur(null); setBlocks([]); setMemo(''); setMemoAtts([]); setDirty(false);
    load();
  }

  // ── 보고표 (Esc 닫기) ──
  useEffect(() => {
    if (!tableOpen) return;
    const h = (e: KeyboardEvent) => { if (e.key === 'Escape') setTableOpen(false); };
    window.addEventListener('keydown', h);
    return () => window.removeEventListener('keydown', h);
  }, [tableOpen]);

  async function exportTableExcel() {
    if (!cur) return;
    try {
      const ExcelJS = (await import('exceljs')).default;
      const wb = new ExcelJS.Workbook();
      const ws = wb.addWorksheet('주간보고');
      ws.getCell(1, 1).value = `${cur.title} 상세 내역`;
      ws.getCell(1, 1).font = { bold: true, size: 16 };
      ws.mergeCells(1, 1, 1, 3);
      const hd = ws.getRow(3);
      hd.values = ['No.', '분류(상태)', '세부 내용 및 팔로업'];
      hd.font = { bold: true };
      hd.eachCell(c => {
        c.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FFE6F0FF' } };
        c.alignment = { horizontal: 'center' };
        c.border = { top: { style: 'thin' }, bottom: { style: 'thin' }, left: { style: 'thin' }, right: { style: 'thin' } };
      });
      blocks.forEach((b, i) => {
        const row = ws.getRow(4 + i);
        row.values = [i + 1, `[${b.status}]\n${b.category}`, formatted(b.content, b.followUp)];
        row.getCell(1).alignment = { horizontal: 'center', vertical: 'middle' };
        row.getCell(2).alignment = { horizontal: 'center', vertical: 'middle', wrapText: true };
        row.getCell(3).alignment = { vertical: 'top', wrapText: true };
        row.eachCell(c => { c.border = { top: { style: 'thin' }, bottom: { style: 'thin' }, left: { style: 'thin' }, right: { style: 'thin' } }; });
      });
      ws.getColumn(1).width = 6;
      ws.getColumn(2).width = 28;
      ws.getColumn(3).width = 100;
      const buf = await wb.xlsx.writeBuffer();
      const a = document.createElement('a');
      a.href = URL.createObjectURL(new Blob([buf], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }));
      a.download = `주간보고_${cur.title.replaceAll(' ', '_')}.xlsx`;
      a.click();
      URL.revokeObjectURL(a.href);
    } catch (err) {
      alert(err instanceof Error ? err.message : '엑셀 내보내기에 실패했습니다.');
    }
  }

  // ── 통계 ──
  const stTotal = blocks.length;
  const stProg = blocks.filter(b => b.status === '진행 중').length;
  const stPend = blocks.filter(b => b.status === '보류').length;
  const stClosed = blocks.filter(b => b.status === '종결').length;
  const stAtt = blocks.reduce((s, b) => s + b.atts.length, 0) + memoAtts.length;
  function toggleFilter(st: string) {
    setStatusFilter(prev => {
      const next = new Set(prev);
      if (next.has(st)) next.delete(st); else next.add(st);
      return next;
    });
  }
  const visible = blocks
    .map((b, i) => ({ b, i }))
    .filter(({ b }) => statusFilter.size === 0 || statusFilter.has(b.status));

  const searching = q.trim() !== '';

  function attStrip(target: number | 'memo', atts: string[]) {
    return (
      <div className="wk-atts">
        {atts.map((src, ai) => (
          <div key={ai} className="wk-att">
            {src.startsWith('data:')
              ? <img src={src} alt="" onClick={() => setPreview(src)} />
              : <span className="wk-att-file" title={src}>{src.split(/[\\/]/).pop()}</span>}
            {canEdit && <button className="wk-att-x" onClick={() => removeAtt(target, ai)}>✕</button>}
          </div>
        ))}
        {canEdit && (
          <button className="wk-att-add" onClick={() => pickImages(target)} title="이미지 첨부 (붙여넣기 Ctrl+V 가능)">+ 사진</button>
        )}
      </div>
    );
  }

  return (
    <div>
      <header className="pg-header">
        <div>
          <h2>주간보고</h2>
          <p>주차별 진행 업무 관리 · 종결 전 항목은 새 주차로 자동 이월됩니다.</p>
        </div>
        {cur && (
          <span className={`wk-savestat ${saving ? 's-saving' : saveErr ? 's-err' : dirty ? 's-typing' : 's-ok'}`}>
            {saving ? '저장 중...' : saveErr ? '저장 실패 — 재시도 중' : dirty ? '입력 중...' : '저장됨'}
          </span>
        )}
        {cur && <button className="btn btn-ghost" onClick={() => { setZoom(1); setTableOpen(true); }}>보고표</button>}
        {cur && canEdit && <button className="btn btn-ghost wk-del" onClick={delReport}>삭제</button>}
        {canEdit && (
          <button className="btn btn-primary" onClick={() => {
            const d = new Date();
            setNY(d.getFullYear()); setNM(d.getMonth() + 1); setNW(weekOfMonth(d));
            setNewOpen(true);
          }}>+ 새 보고서</button>
        )}
      </header>

      <div className="pg-body wk-layout">
        {/* 좌측: 검색 + 월별 주차 목록 */}
        <aside className="wk-side">
          <input className="wk-search" placeholder="전체 주차 검색 (분류/내용/팔로업)" value={q} onChange={e => setQ(e.target.value)} />
          <div className="wk-list">
            {groups.length === 0 && <div className="wk-empty">보고서가 없습니다</div>}
            {groups.map(g => (
              <div key={g.monthTitle} className="wk-group">
                <div className="wk-month">{g.monthTitle}</div>
                {g.reports.map(r => (
                  <button key={r.id} className={`wk-item ${cur?.id === r.id ? 'on' : ''}`} onClick={() => open(r.id)}>
                    <span className="wk-item-t">{r.shortTitle || r.title}</span>
                    <span className="wk-item-m">{r.dateRange} · {r.blockCount}블록</span>
                  </button>
                ))}
              </div>
            ))}
          </div>
        </aside>

        {/* 우측: 검색 결과 or 현재 보고서 */}
        <div className="wk-main">
          {searching ? (
            <>
              <div className="wk-title-row">
                <h3 className="wk-title">'{q.trim()}' 검색 결과 <span className="wk-title-sub">{hits.length}건 · 전체 주차</span></h3>
                <button className="btn btn-ghost" onClick={() => setQ('')}>검색 닫기</button>
              </div>
              {hits.length === 0 && <div className="wk-none">검색 결과가 없습니다</div>}
              {hits.map((h, i) => (
                <button key={i} className="wk-hit" onClick={() => { setQ(''); open(h.reportId); }}>
                  <div className="wk-hit-top">
                    <span className="wk-hit-week">[{h.reportShortTitle || h.reportTitle}]</span>
                    <b>{h.block.category || '(분류 없음)'}</b>
                    <span className={`wk-st st-${h.block.status}`}>{h.block.status}</span>
                    <span className="wk-hit-range">{h.dateRange}</span>
                  </div>
                  {h.block.content && <p>{h.block.content}</p>}
                  {h.block.followUp && <p className="wk-hit-fu">▶ {h.block.followUp}</p>}
                </button>
              ))}
            </>
          ) : !cur ? (
            <div className="wk-none">
              <p>왼쪽에서 주차를 선택하거나 새 보고서를 만드세요.</p>
            </div>
          ) : (
            <>
              <div className="wk-title-row">
                <div>
                  <h3 className="wk-title">{cur.title}</h3>
                  <p className="wk-date">{cur.dateRange}</p>
                </div>
                {/* 통계 알약 — 클릭 시 해당 상태만 필터 */}
                <div className="wk-stats">
                  <button className={`wk-stat total ${statusFilter.size === 0 ? 'on' : ''}`} onClick={() => setStatusFilter(new Set())}>총 {stTotal}건</button>
                  <button className={`wk-stat prog ${statusFilter.has('진행 중') ? 'on' : ''}`} onClick={() => toggleFilter('진행 중')}>진행 {stProg}</button>
                  <button className={`wk-stat pend ${statusFilter.has('보류') ? 'on' : ''}`} onClick={() => toggleFilter('보류')}>보류 {stPend}</button>
                  <button className={`wk-stat closed ${statusFilter.has('종결') ? 'on' : ''}`} onClick={() => toggleFilter('종결')}>종결 {stClosed}</button>
                  <span className="wk-stat att">첨부 {stAtt}</span>
                </div>
              </div>

              {visible.length === 0 && blocks.length > 0 && <div className="wk-none">해당 상태의 블록이 없습니다</div>}
              {blocks.length === 0 && <div className="wk-none">블록이 없습니다. "+ 블록 추가"로 시작하세요.</div>}

              {visible.map(({ b, i }) => (
                <div key={i} className="wk-block" onPaste={onPasteTo(i)}
                  draggable={canEdit && statusFilter.size === 0}
                  onDragStart={e => { dragIdx.current = i; e.dataTransfer.effectAllowed = 'move'; }}
                  onDragOver={e => { e.preventDefault(); e.dataTransfer.dropEffect = 'move'; }}
                  onDrop={e => { e.preventDefault(); if (dragIdx.current != null) moveBlock(dragIdx.current, i); dragIdx.current = null; }}>
                  <div className="wk-block-top">
                    <span className="wk-bno" title="드래그로 순서 변경">{i + 1}</span>
                    <input className="wk-cat" placeholder="분류 및 항목" value={b.category} disabled={!canEdit}
                      onChange={e => setBlock(i, { category: e.target.value })} />
                    <div className="wk-stbtns">
                      {STATUSES.map(st => (
                        <button key={st} className={`wk-stbtn st-${st} ${b.status === st ? 'on' : ''}`} disabled={!canEdit}
                          onClick={() => setBlock(i, { status: st })}>{st}</button>
                      ))}
                    </div>
                    {canEdit && <button className="wk-bdel" onClick={() => delBlock(i)} title="블록 삭제">✕</button>}
                  </div>
                  <AutoTA className="wk-content" placeholder="내용" value={b.content} onChange={v => setBlock(i, { content: v })} />
                  <div className="wk-fu-row">
                    <span className="wk-fu-tag">팔로업</span>
                    <AutoTA className="wk-fu" placeholder="후속 조치 입력" value={b.followUp} onChange={v => setBlock(i, { followUp: v })} />
                  </div>
                  {attStrip(i, b.atts)}
                </div>
              ))}

              {canEdit && statusFilter.size === 0 && (
                <button className="wk-addblock" onClick={addBlock}>+ 블록 추가</button>
              )}

              {/* 추가 메모 */}
              <div className="wk-memo" onPaste={onPasteTo('memo')}>
                <h4>추가 메모</h4>
                <AutoTA className="wk-memo-ta" placeholder="이번 주 공유 메모" value={memo} onChange={v => { setMemo(v); mark(); }} />
                {attStrip('memo', memoAtts)}
              </div>
            </>
          )}
        </div>
      </div>

      {/* 숨김 파일 입력 (블록/메모 공용) */}
      <input ref={fileRef} type="file" accept="image/*" multiple style={{ display: 'none' }}
        onChange={e => {
          const files = Array.from(e.target.files ?? []);
          if (files.length) addImages(attTarget.current, files);
          e.target.value = '';
        }} />

      {/* 새 보고서 모달 (년/월/주차 자동) */}
      {newOpen && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setNewOpen(false); }}>
          <div className="modal-box wk-new">
            <h3>새 보고서 생성</h3>
            <p className="wk-new-hint">주차를 선택하면 제목·기간이 자동으로 만들어지고, 직전 보고서의 미종결 항목이 이월됩니다.</p>
            <div className="wk-new-row">
              <select className="input" value={nY} onChange={e => setNY(Number(e.target.value))}>
                {Array.from({ length: now.getFullYear() - 2024 + 2 }, (_, i) => 2024 + i).map(y => <option key={y} value={y}>{y}년</option>)}
              </select>
              <select className="input" value={nM} onChange={e => setNM(Number(e.target.value))}>
                {Array.from({ length: 12 }, (_, i) => i + 1).map(m => <option key={m} value={m}>{m}월</option>)}
              </select>
              <select className="input" value={nW} onChange={e => setNW(Number(e.target.value))}>
                {[1, 2, 3, 4, 5, 6].map(w => <option key={w} value={w}>{w}주차</option>)}
              </select>
            </div>
            <p className="wk-new-preview">자동 계산 날짜: {rangeForWeek(nY, nM, nW)}</p>
            <div className="modal-actions">
              <button className="btn btn-ghost" onClick={() => setNewOpen(false)}>취소</button>
              <button className="btn btn-primary" onClick={confirmCreate} disabled={creating}>{creating ? '생성 중...' : '생성'}</button>
            </div>
          </div>
        </div>
      )}

      {/* 보고표 (발표용) */}
      {tableOpen && cur && (
        <div className="wk-table-overlay">
          <div className="wk-table-head">
            <div>
              <h3>{cur.title} 주간보고 상세</h3>
              <p>확대 {Math.round(zoom * 100)}% · Esc로 닫기</p>
            </div>
            <div className="wk-table-btns">
              <button className="btn btn-ghost" onClick={() => setZoom(z => Math.max(0.7, Math.round((z - 0.1) * 10) / 10))}>−</button>
              <button className="btn btn-ghost" onClick={() => setZoom(1)}>{Math.round(zoom * 100)}%</button>
              <button className="btn btn-ghost" onClick={() => setZoom(z => Math.min(2.5, Math.round((z + 0.1) * 10) / 10))}>+</button>
              <button className="btn btn-ghost" onClick={() => setZoom(1.5)}>발표 모드</button>
              <button className="btn btn-ghost" onClick={exportTableExcel}>엑셀 내보내기</button>
              <button className="btn btn-primary" onClick={() => setTableOpen(false)}>닫기</button>
            </div>
          </div>
          <div className="wk-table-body" style={{ fontSize: `${15 * zoom}px` }}>
            <table className="wk-rtable">
              <thead>
                <tr><th className="c-no">No.</th><th className="c-cat">분류 및 항목 (상태)</th><th>세부 내용 및 팔로업</th><th className="c-att">첨부</th></tr>
              </thead>
              <tbody>
                {blocks.map((b, i) => (
                  <tr key={i}>
                    <td className="c-no">{i + 1}</td>
                    <td className="c-cat">
                      <span className={`wk-st st-${b.status}`}>{b.status}</span>
                      <div className="wk-rt-cat">{b.category}</div>
                    </td>
                    <td className="wk-rt-body">
                      {b.content.trim() && <div className="wk-rt-sec"><b>기존 내용</b><div>{b.content}</div></div>}
                      {b.followUp.trim() && <div className="wk-rt-sec fu"><b>팔로업</b><div>{b.followUp}</div></div>}
                      {!b.content.trim() && !b.followUp.trim() && <span className="wk-rt-none">-</span>}
                    </td>
                    <td className="c-att">
                      {b.atts.map((src, ai) => src.startsWith('data:')
                        ? <img key={ai} src={src} alt="" onClick={() => setPreview(src)} />
                        : <span key={ai} className="wk-att-file" title={src}>{src.split(/[\\/]/).pop()}</span>)}
                    </td>
                  </tr>
                ))}
                {blocks.length === 0 && <tr><td colSpan={4} className="wk-none">블록이 없습니다</td></tr>}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* 이미지 확대 */}
      {preview && (
        <div className="wk-preview" onClick={() => setPreview(null)}>
          <img src={preview} alt="" />
        </div>
      )}
    </div>
  );
}
