import { useCallback, useEffect, useRef, useState } from 'react';
import { api, getToken, ApiError } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { useMailAccess } from '../components/mailAccess';
import { MailReader, fmtSize, openMailWindow, type Detail } from './mail/mailShared';
import './MailArchive.css';

// OFFICE › 메일 확인 — 웹메일 용량이 모자라 못 보는 메일을 백업 ZIP 으로 올려 두고 웹메일처럼 찾아본다.
// 본인 메일만 보이고(관리자도 남의 메일은 못 본다), 사외(휴대폰 데이터)에서도 열린다. 접두사 ml-
//  - 검색: 항목(전체·제목·본문·보낸사람·받는사람·참조) 고르고 낱말
//  - 필터: 기간·첨부 있음·크기 — 걸린 조건은 칩으로 보이고 하나씩 끈다
//  - 보기: 좌우 나눔 · 상하 나눔 · 목록만(누르면 새 창)
//  - 메일 새 창: [새 창] 또는 두 번 누르기

interface Row {
  id: number; folder: 'inbox' | 'sent'; subject: string; fromName: string; fromAddr: string; to: string;
  date: string | null; preview: string; attachments: number; size: number; starred: boolean;
}
interface Page { total: number; page: number; size: number; inbox: number; sent: number; starred: number; items: Row[] }
interface ImportRow { id: number; fileName: string; importedAt: string; bytes: number; total: number; added: number; duplicates: number; failed: number }
interface Job {
  id: string; fileName: string; status: 'running' | 'done' | 'failed'; processed: number; error: string | null;
  result: { importId: number; total: number; added: number; duplicates: number; failed: number; restored?: number } | null;
}
interface UpTask { key: string; name: string; size: number; sent: number; phase: 'upload' | 'import' | 'done' | 'failed'; processed: number; msg: string }

type Folder = 'all' | 'inbox' | 'sent' | 'starred';
type Field = 'all' | 'subject' | 'body' | 'from' | 'to' | 'cc';
type View = 'right' | 'bottom' | 'list';
type Sort = 'date' | 'size' | 'from' | 'subject';
type Period = '' | 'today' | '1w' | '1m' | '3m' | '6m' | '1y' | 'custom';

const FIELDS: [Field, string][] = [['all', '전체'], ['subject', '제목'], ['body', '본문'], ['from', '보낸사람'], ['to', '받는사람'], ['cc', '참조']];
const PERIODS: [Period, string][] = [['', '전체 기간'], ['today', '오늘'], ['1w', '최근 1주'], ['1m', '최근 1개월'], ['3m', '최근 3개월'], ['6m', '최근 6개월'], ['1y', '최근 1년'], ['custom', '직접 입력']];
const SIZES: [number, string][] = [[0, '전체'], [1024 * 1024, '1MB 이상'], [5 * 1024 * 1024, '5MB 이상'], [10 * 1024 * 1024, '10MB 이상']];
const FOLDERS: [Folder, string][] = [['all', '전체메일함'], ['inbox', '받은메일함'], ['sent', '보낸메일함'], ['starred', '중요']];
const CHUNK = 6 * 1024 * 1024;   // 서버 한도(8MB)보다 작게
const VIEW_KEY = 'mail-view';

const ymd = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
function periodSince(p: Period): string {
  if (!p || p === 'custom') return '';
  const d = new Date();
  if (p === '1w') d.setDate(d.getDate() - 7);
  else if (p === '1m') d.setMonth(d.getMonth() - 1);
  else if (p === '3m') d.setMonth(d.getMonth() - 3);
  else if (p === '6m') d.setMonth(d.getMonth() - 6);
  else if (p === '1y') d.setFullYear(d.getFullYear() - 1);
  return ymd(d);
}
const shortDate = (d: string | null) => {
  if (!d) return '';
  const t = ymd(new Date());
  return d.startsWith(t) ? d.slice(11) : d.startsWith(t.slice(0, 4)) ? d.slice(5, 16).replace('-', '/') : d.slice(0, 10);
};
const newId = () => (crypto.randomUUID?.() ?? `${Date.now()}${Math.random()}`).replace(/[^0-9a-f]/gi, '').padEnd(32, '0').slice(0, 32).toLowerCase();
// 목록 칸 크기(좌우 보기는 폭, 위아래 보기는 높이) — 가운데 막대를 끌어 바꾸고, PC 에 기억한다.
const SPLIT_KEY = { right: 'mail-list-w', bottom: 'mail-list-h' } as const;
const SPLIT_DEFAULT = { right: 380, bottom: 300 };
const SPLIT_MIN = { right: 260, bottom: 140 };
const loadSplit = (v: 'right' | 'bottom') => {
  try { const n = Number(localStorage.getItem(SPLIT_KEY[v])); return n >= SPLIT_MIN[v] ? n : SPLIT_DEFAULT[v]; } catch { return SPLIT_DEFAULT[v]; }
};
const loadView = (): View => { try { const v = localStorage.getItem(VIEW_KEY); return v === 'bottom' || v === 'list' ? v : 'right'; } catch { return 'right'; } };

async function sendChunk(id: string, offset: number, blob: Blob): Promise<number> {
  const token = getToken();
  for (let attempt = 0; ; attempt++) {
    try {
      const res = await fetch(`/api/mail/upload?id=${id}&offset=${offset}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/octet-stream', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
        body: blob,
      });
      const body = await res.json().catch(() => null);
      if (!res.ok) throw new ApiError(res.status, body?.error ?? `올리지 못했습니다 (${res.status})`);
      return body?.data as number;
    } catch (e) {
      // 네트워크가 잠깐 끊긴 것이면 두 번 더 — 서버는 같은 조각을 두 번 붙이지 않는다
      if (e instanceof ApiError || attempt >= 2) throw e;
      await new Promise(r => setTimeout(r, 1500 * (attempt + 1)));
    }
  }
}

/** 보기 방식 아이콘 */
function ViewIcon({ v }: { v: View }) {
  return (
    <svg viewBox="0 0 20 16" width="18" height="14" fill="none" stroke="currentColor" strokeWidth="1.6" aria-hidden>
      <rect x="1" y="1" width="18" height="14" rx="2" />
      {v === 'right' && <path d="M8 1v14" />}
      {v === 'bottom' && <path d="M1 7h18" />}
      {v === 'list' && <path d="M4 5h12M4 8h12M4 11h12" />}
    </svg>
  );
}

export default function MailArchive() {
  const { user } = useAuth();
  const access = useMailAccess(user?.id);
  const [folder, setFolder] = useState<Folder>('all');
  const [field, setField] = useState<Field>('all');
  const [q, setQ] = useState('');
  const [query, setQuery] = useState('');
  const [period, setPeriod] = useState<Period>('');
  const [since, setSince] = useState('');
  const [until, setUntil] = useState('');
  const [att, setAtt] = useState(false);
  const [minSize, setMinSize] = useState(0);
  const [sort, setSort] = useState<Sort>('date');
  const [dir, setDir] = useState<'desc' | 'asc'>('desc');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(50);
  const [view, setView] = useState<View>(loadView);
  const [listW, setListW] = useState(() => loadSplit('right'));
  const [listH, setListH] = useState(() => loadSplit('bottom'));
  const splitRef = useRef<HTMLDivElement>(null);
  const [showFilter, setShowFilter] = useState(false);
  const [data, setData] = useState<Page | null>(null);
  const [loading, setLoading] = useState(false);
  const [err, setErr] = useState('');
  const [sel, setSel] = useState<number | null>(null);
  const [detail, setDetail] = useState<Detail | null>(null);
  const [detailErr, setDetailErr] = useState('');
  const [tasks, setTasks] = useState<UpTask[]>([]);
  const [showImports, setShowImports] = useState(false);
  const [imports, setImports] = useState<ImportRow[] | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);
  const filterRef = useRef<HTMLDivElement>(null);
  const blockedOutside = !!access?.internalOnly && !access?.internal;
  const ready = !!access?.allowed && !blockedOutside;

  useEffect(() => { try { localStorage.setItem(VIEW_KEY, view); } catch { /* 저장 못 해도 보기는 바뀐다 */ } }, [view]);
  useEffect(() => { try { localStorage.setItem(SPLIT_KEY.right, String(listW)); localStorage.setItem(SPLIT_KEY.bottom, String(listH)); } catch { /* 기억 못 해도 된다 */ } }, [listW, listH]);

  /** 목록·읽기 사이 막대 끌기 — 읽기 칸도 최소 300px(위아래 보기는 160px)은 남긴다 */
  function clampSplit(v: 'right' | 'bottom', n: number) {
    const box = splitRef.current?.getBoundingClientRect();
    const max = box ? (v === 'right' ? box.width - 300 : box.height - 160) : 2000;
    return Math.round(Math.max(SPLIT_MIN[v], Math.min(max, n)));
  }
  function startDrag(e: React.PointerEvent) {
    if (view === 'list') return;
    e.preventDefault();
    const v = view;
    const box = splitRef.current!.getBoundingClientRect();
    const move = (ev: PointerEvent) => {
      if (v === 'right') setListW(clampSplit(v, ev.clientX - box.left));
      else setListH(clampSplit(v, ev.clientY - box.top));
    };
    const up = () => {
      document.removeEventListener('pointermove', move);
      document.removeEventListener('pointerup', up);
      document.body.classList.remove('ml-dragging', 'ml-dragging-y');
    };
    document.body.classList.add(v === 'right' ? 'ml-dragging' : 'ml-dragging-y');
    document.addEventListener('pointermove', move);
    document.addEventListener('pointerup', up);
  }
  function keySplit(e: React.KeyboardEvent) {
    if (view === 'list') return;
    const step = e.shiftKey ? 80 : 20;
    const d = (view === 'right' ? { ArrowLeft: -step, ArrowRight: step } : { ArrowUp: -step, ArrowDown: step } as Record<string, number>)[e.key as 'ArrowLeft'];
    if (!d) return;
    e.preventDefault();
    if (view === 'right') setListW(w => clampSplit('right', w + d)); else setListH(h => clampSplit('bottom', h + d));
  }
  // 검색어는 잠깐 멈췄을 때 찾는다
  useEffect(() => { const t = setTimeout(() => { setQuery(q.trim()); setPage(1); }, 300); return () => clearTimeout(t); }, [q]);
  // 필터 창 바깥을 누르면 닫는다
  useEffect(() => {
    if (!showFilter) return;
    const close = (e: MouseEvent) => { if (filterRef.current && !filterRef.current.contains(e.target as Node)) setShowFilter(false); };
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, [showFilter]);

  const sinceValue = period === 'custom' ? since : periodSince(period);
  const untilValue = period === 'custom' ? until : '';

  const load = useCallback(async () => {
    setLoading(true); setErr('');
    try {
      const p = new URLSearchParams({ page: String(page), size: String(pageSize), sort, dir });
      if (folder !== 'all') p.set('folder', folder);
      if (query) { p.set('q', query); p.set('field', field); }
      if (sinceValue) p.set('since', sinceValue);
      if (untilValue) p.set('until', untilValue);
      if (att) p.set('att', 'true');
      if (minSize) p.set('minSize', String(minSize));
      setData(await api.get<Page>(`/api/mail?${p}`));
    } catch (e) { setErr(e instanceof Error ? e.message : '목록을 불러오지 못했습니다.'); }
    finally { setLoading(false); }
  }, [folder, query, field, sinceValue, untilValue, att, minSize, sort, dir, page, pageSize]);
  useEffect(() => { if (ready) void load(); }, [ready, load]);

  useEffect(() => {
    if (sel === null || view === 'list') { setDetail(null); return; }
    let alive = true;
    setDetail(null); setDetailErr('');
    api.get<Detail>(`/api/mail/${sel}`)
      .then(d => { if (alive) setDetail(d); })
      .catch(e => { if (alive) setDetailErr(e instanceof Error ? e.message : '메일을 열지 못했습니다.'); });
    return () => { alive = false; };
  }, [sel, view]);

  // 새 창에서 중요 표시를 바꾸면 목록에도 반영
  useEffect(() => {
    const on = (e: MessageEvent) => {
      if (e.origin !== location.origin || e.data?.type !== 'mail-star') return;
      const { id, starred } = e.data as { id: number; starred: boolean };
      setData(d => d && { ...d, items: d.items.map(r => (r.id === id ? { ...r, starred } : r)) });
      setDetail(d => (d && d.id === id ? { ...d, starred } : d));
    };
    window.addEventListener('message', on);
    return () => window.removeEventListener('message', on);
  }, []);

  const loadImports = useCallback(() => api.get<ImportRow[]>('/api/mail/imports').then(setImports).catch(() => setImports([])), []);
  useEffect(() => { if (showImports && ready) void loadImports(); }, [showImports, ready, loadImports]);

  const reset = () => { setPage(1); setSel(null); };
  const patch = (key: string, p: Partial<UpTask>) => setTasks(ts => ts.map(t => (t.key === key ? { ...t, ...p } : t)));

  async function toggleStar(id: number, now: boolean) {
    try {
      const on = await api.put<boolean>(`/api/mail/${id}/star`, { starred: !now });
      setData(d => d && { ...d, starred: d.starred + (on ? 1 : -1), items: d.items.map(r => (r.id === id ? { ...r, starred: on } : r)) });
      setDetail(d => (d && d.id === id ? { ...d, starred: on } : d));
    } catch (e) { setErr(e instanceof Error ? e.message : '바꾸지 못했습니다.'); }
  }

  async function uploadOne(file: File) {
    const key = newId();
    setTasks(ts => [...ts, { key, name: file.name, size: file.size, sent: 0, phase: 'upload', processed: 0, msg: '' }]);
    try {
      let offset = 0;
      while (offset < file.size) {
        offset = await sendChunk(key, offset, file.slice(offset, Math.min(offset + CHUNK, file.size)));
        patch(key, { sent: offset });
      }
      patch(key, { phase: 'import' });
      let job = await api.post<Job>('/api/mail/import', { uploadId: key, fileName: file.name, size: file.size });
      while (job.status === 'running') {
        await new Promise(r => setTimeout(r, 1200));
        job = await api.get<Job>(`/api/mail/import/${job.id}`);
        patch(key, { processed: job.processed });
      }
      if (job.status === 'failed' || !job.result) throw new Error(job.error ?? '가져오지 못했습니다.');
      const r = job.result;
      patch(key, { phase: 'done', processed: r.total,
        msg: `${r.added.toLocaleString()}통 보관${r.duplicates ? ` · 중복 ${r.duplicates.toLocaleString()}통 건너뜀` : ''}${r.restored ? ` · 빠진 원본 ${r.restored.toLocaleString()}통 복구` : ''}${r.failed ? ` · 읽지 못함 ${r.failed}통` : ''}` });
    } catch (e) {
      patch(key, { phase: 'failed', msg: e instanceof Error ? e.message : '올리지 못했습니다.' });
    }
  }

  async function onFiles(files: FileList | null) {
    if (!files?.length) return;
    const list = [...files].filter(f => /\.(zip|eml)$/i.test(f.name));
    if (list.length < files.length) setErr('ZIP 또는 EML 파일만 올릴 수 있습니다.');
    for (const f of list) await uploadOne(f);   // 하나씩 차례로
    if (fileRef.current) fileRef.current.value = '';
    setPage(1);
    void load();
    if (showImports) void loadImports();
  }

  async function removeImport(r: ImportRow) {
    if (!confirm(`'${r.fileName}' 로 가져온 메일 ${r.added.toLocaleString()}통을 이 보관함에서 지울까요?\n\n웹메일 서버의 메일은 건드리지 않습니다. 다시 올리면 다시 들어옵니다.`)) return;
    try {
      await api.del(`/api/mail/imports/${r.id}`);
      setSel(null);
      void loadImports(); void load();
    } catch (e) { setErr(e instanceof Error ? e.message : '지우지 못했습니다.'); }
  }

  /** 목록 머리 누르기 — 같은 칸이면 순서만 뒤집는다 */
  function sortBy(s: Sort) {
    if (sort === s) setDir(d => (d === 'desc' ? 'asc' : 'desc'));
    else { setSort(s); setDir(s === 'date' || s === 'size' ? 'desc' : 'asc'); }
    setPage(1);
  }
  const arrow = (s: Sort) => (sort === s ? (dir === 'desc' ? ' ▼' : ' ▲') : '');

  function openRow(r: Row) {
    if (view === 'list') openMailWindow(r.id);
    else setSel(r.id);
  }
  /** 위·아래 화살표로 다음 메일 */
  function onListKey(e: React.KeyboardEvent) {
    if (!data || (e.key !== 'ArrowDown' && e.key !== 'ArrowUp')) return;
    e.preventDefault();
    const i = data.items.findIndex(r => r.id === sel);
    const next = data.items[Math.max(0, Math.min(data.items.length - 1, i + (e.key === 'ArrowDown' ? 1 : -1)))];
    if (next) setSel(next.id);
  }

  // ── 쓸 수 없을 때 ──
  if (!access) return <div className="page-loading">불러오는 중…</div>;
  if (!access.allowed || blockedOutside) {
    return (
      <div className="ml-page">
        <header className="pg-header"><div><h2>메일 확인</h2><p>웹메일 백업 보관함</p></div></header>
        <div className="pg-body">
          <div className="ml-blocked">
            {!access.allowed
              ? <><b>시험 사용 중인 메뉴입니다.</b><span>지금은 허용된 계정만 쓸 수 있습니다.</span></>
              : <><b>메일 확인은 사내에서만 열 수 있습니다.</b><span>회사 와이파이나 사내 PC 로 접속하세요. 지금 접속: {access.ip || '알 수 없음'} (사외)</span></>}
          </div>
        </div>
      </div>
    );
  }

  const pages = data ? Math.max(1, Math.ceil(data.total / pageSize)) : 1;
  const busy = tasks.some(t => t.phase === 'upload' || t.phase === 'import');
  const counts: Record<Folder, number> = { all: data ? data.inbox + data.sent : 0, inbox: data?.inbox ?? 0, sent: data?.sent ?? 0, starred: data?.starred ?? 0 };
  const chips: [string, () => void][] = [
    ...(period ? [[period === 'custom' ? `${since || '처음'} ~ ${until || '오늘'}` : PERIODS.find(p => p[0] === period)![1], () => { setPeriod(''); setSince(''); setUntil(''); reset(); }] as [string, () => void]] : []),
    ...(att ? [['첨부 있음', () => { setAtt(false); reset(); }] as [string, () => void]] : []),
    ...(minSize ? [[SIZES.find(s => s[0] === minSize)![1], () => { setMinSize(0); reset(); }] as [string, () => void]] : []),
  ];
  const filterCount = chips.length;
  // 목록 칸 번호: 현재 페이지 앞뒤로 10개
  const first = Math.max(1, Math.min(page - 4, pages - 9));
  const nums = Array.from({ length: Math.min(10, pages) }, (_, i) => first + i);
  const tableMode = view !== 'right';

  return (
    <div className={`ml-page v-${view}${sel !== null && view !== 'list' ? ' reading' : ''}`}>
      <div className="pg-body ml-body">
        {tasks.length > 0 && (
          <div className="ml-tasks">
            {tasks.map(t => {
              const pct = t.phase === 'upload' ? Math.round((t.sent / Math.max(t.size, 1)) * 100) : 100;
              return (
                <div key={t.key} className={`ml-task ${t.phase}`}>
                  <span className="ml-task-name" title={t.name}>{t.name}</span>
                  <span className="ml-task-size">{fmtSize(t.size)}</span>
                  <span className="ml-bar"><i style={{ width: `${pct}%` }} /></span>
                  <span className="ml-task-msg">
                    {t.phase === 'upload' && `올리는 중 ${pct}%`}
                    {t.phase === 'import' && `메일 정리 중… ${t.processed.toLocaleString()}통`}
                    {(t.phase === 'done' || t.phase === 'failed') && t.msg}
                  </span>
                </div>
              );
            })}
            {!busy && <button type="button" className="ml-task-clear" onClick={() => setTasks([])}>닫기</button>}
          </div>
        )}

        <div className="ml-app">
          {/* 왼쪽 — 메일함 */}
          <aside className="ml-side">
            <h2 className="ml-title">메일 확인</h2>
            <button type="button" className="btn btn-primary ml-upload" disabled={busy} onClick={() => fileRef.current?.click()}>
              {busy ? '올리는 중…' : '백업 ZIP 올리기'}
            </button>
            <input ref={fileRef} type="file" accept=".zip,.eml" multiple hidden onChange={e => void onFiles(e.target.files)} />
            <nav className="ml-folders" aria-label="메일함">
              {FOLDERS.map(([k, l]) => (
                <button key={k} type="button" className={folder === k ? 'on' : ''} onClick={() => { setFolder(k); reset(); }}>
                  <span>{k === 'starred' ? '★ ' : ''}{l}</span><em>{counts[k].toLocaleString()}</em>
                </button>
              ))}
            </nav>
            <button type="button" className={`ml-side-link${showImports ? ' on' : ''}`} onClick={() => setShowImports(v => !v)}>가져오기 기록</button>
            <p className="ml-side-note">본인 메일만 보입니다.</p>
          </aside>

          {/* 오른쪽 — 도구·목록·읽기 */}
          <section className="ml-main">
            {showImports && (
              <div className="ml-imports">
                <div className="ml-imports-h">가져오기 기록 <span>잘못 올린 것은 지울 수 있습니다(웹메일 서버의 메일은 그대로)</span>
                  <button type="button" className="ml-x" onClick={() => setShowImports(false)} aria-label="닫기">✕</button></div>
                {imports === null ? <div className="ml-empty">불러오는 중…</div> : imports.length === 0 ? <div className="ml-empty">아직 올린 백업이 없습니다.</div> : (
                  <div className="ml-imports-wrap">
                    <table>
                      <thead><tr><th>올린 시각</th><th>파일</th><th className="n">크기</th><th className="n">메일</th><th className="n">보관</th><th className="n">중복</th><th className="n">실패</th><th /></tr></thead>
                      <tbody>
                        {imports.map(r => (
                          <tr key={r.id}>
                            <td className="t">{r.importedAt}</td><td className="f" title={r.fileName}>{r.fileName}</td>
                            <td className="n">{fmtSize(r.bytes)}</td><td className="n">{r.total.toLocaleString()}</td>
                            <td className="n">{r.added.toLocaleString()}</td><td className="n">{r.duplicates.toLocaleString()}</td>
                            <td className={`n${r.failed ? ' bad' : ''}`}>{r.failed}</td>
                            <td><button type="button" className="ml-del" onClick={() => void removeImport(r)}>지우기</button></td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </div>
            )}

            <div className="ml-top">
              <h3 className="ml-folder-name">{FOLDERS.find(f => f[0] === folder)![1]} <em>{data ? data.total.toLocaleString() : ''}</em></h3>
              <div className="ml-searchbox">
                <select className="ml-field" value={field} onChange={e => { setField(e.target.value as Field); setPage(1); }} aria-label="검색 항목">
                  {FIELDS.map(([k, l]) => <option key={k} value={k}>{l}</option>)}
                </select>
                <input type="search" placeholder="검색어 입력" value={q} onChange={e => setQ(e.target.value)} aria-label="검색어" />
              </div>
            </div>

            <div className="ml-bar2">
              <div className="ml-filter" ref={filterRef}>
                <button type="button" className={`ml-tbtn${filterCount ? ' active' : ''}`} onClick={() => setShowFilter(v => !v)} aria-expanded={showFilter}>
                  필터{filterCount ? ` ${filterCount}` : ''} ▾
                </button>
                {showFilter && (
                  <div className="ml-fpanel" role="dialog" aria-label="필터">
                    <div className="ml-fgroup">
                      <div className="ml-flabel">기간</div>
                      <div className="ml-fopts">
                        {PERIODS.map(([k, l]) => (
                          <button key={k} type="button" className={period === k ? 'on' : ''} onClick={() => { setPeriod(k); reset(); }}>{l}</button>
                        ))}
                      </div>
                      {period === 'custom' && (
                        <div className="ml-frange">
                          <input type="date" className="input" value={since} onChange={e => { setSince(e.target.value); reset(); }} aria-label="시작일" />
                          <span>~</span>
                          <input type="date" className="input" value={until} onChange={e => { setUntil(e.target.value); reset(); }} aria-label="끝일" />
                        </div>
                      )}
                    </div>
                    <div className="ml-fgroup">
                      <div className="ml-flabel">첨부</div>
                      <label className="ml-fchk"><input type="checkbox" checked={att} onChange={e => { setAtt(e.target.checked); reset(); }} /> 첨부파일 있는 메일만</label>
                    </div>
                    <div className="ml-fgroup">
                      <div className="ml-flabel">크기</div>
                      <div className="ml-fopts">
                        {SIZES.map(([k, l]) => <button key={k} type="button" className={minSize === k ? 'on' : ''} onClick={() => { setMinSize(k); reset(); }}>{l}</button>)}
                      </div>
                    </div>
                    <div className="ml-factions">
                      <button type="button" className="ml-link" onClick={() => { setPeriod(''); setSince(''); setUntil(''); setAtt(false); setMinSize(0); reset(); }}>모두 지우기</button>
                      <button type="button" className="btn btn-primary" onClick={() => setShowFilter(false)}>닫기</button>
                    </div>
                  </div>
                )}
              </div>
              {chips.map(([label, off]) => (
                <span key={label} className="ml-chip">{label}<button type="button" onClick={off} aria-label={`${label} 끄기`}>✕</button></span>
              ))}
              {query && <span className="ml-chip q">{FIELDS.find(f => f[0] === field)![1]}: {query}<button type="button" onClick={() => setQ('')} aria-label="검색 지우기">✕</button></span>}
              <span className="ml-bar2-r">
                <select className="ml-psize" value={pageSize} onChange={e => { setPageSize(Number(e.target.value)); setPage(1); }} aria-label="한 쪽에 볼 메일 수">
                  {[50, 100, 200].map(n => <option key={n} value={n}>{n}개씩</option>)}
                </select>
                <span className="ml-views" role="radiogroup" aria-label="보기 방식">
                  {([['right', '좌우로 나눠 보기'], ['bottom', '위아래로 나눠 보기'], ['list', '목록만 보기(누르면 새 창)']] as [View, string][]).map(([v, l]) => (
                    <button key={v} type="button" role="radio" aria-checked={view === v} className={view === v ? 'on' : ''} title={l} onClick={() => setView(v)}>
                      <ViewIcon v={v} />
                    </button>
                  ))}
                </span>
              </span>
            </div>

            {err && <div className="ml-err">{err}</div>}

            <div className="ml-split" ref={splitRef}
              style={view === 'right' ? { gridTemplateColumns: `${listW}px 6px minmax(0, 1fr)` }
                : view === 'bottom' ? { gridTemplateRows: `${listH}px 6px minmax(0, 1fr)` } : undefined}>
              <div className="ml-list" tabIndex={0} onKeyDown={onListKey} aria-label="메일 목록">
                {tableMode && (
                  <div className="ml-thead">
                    <span className="c-star" /><span className="c-att" />
                    <button type="button" className="c-who" onClick={() => sortBy('from')}>{folder === 'sent' ? '받는사람' : '보낸사람'}{arrow('from')}</button>
                    <button type="button" className="c-subj" onClick={() => sortBy('subject')}>제목{arrow('subject')}</button>
                    <button type="button" className="c-date" onClick={() => sortBy('date')}>날짜{arrow('date')}</button>
                    <button type="button" className="c-size" onClick={() => sortBy('size')}>크기{arrow('size')}</button>
                  </div>
                )}
                {!tableMode && (
                  <div className="ml-sortbar">
                    정렬 <select value={`${sort}:${dir}`} onChange={e => { const [s, d] = e.target.value.split(':'); setSort(s as Sort); setDir(d as 'asc' | 'desc'); setPage(1); }} aria-label="정렬">
                      <option value="date:desc">최근 메일부터</option><option value="date:asc">오래된 메일부터</option>
                      <option value="size:desc">큰 메일부터</option><option value="from:asc">보낸사람 가나다</option><option value="subject:asc">제목 가나다</option>
                    </select>
                  </div>
                )}
                <div className="ml-rows">
                  {data?.items.map(m => {
                    const who = m.folder === 'sent' ? `→ ${m.to.split(',')[0] || '(받는 사람 없음)'}` : (m.fromName || m.fromAddr || '(보낸 사람 없음)');
                    const star = (
                      <span role="button" tabIndex={-1} className={`ml-star${m.starred ? ' on' : ''}`} title="중요 표시"
                        onClick={e => { e.stopPropagation(); void toggleStar(m.id, m.starred); }}>{m.starred ? '★' : '☆'}</span>
                    );
                    return tableMode ? (
                      <div key={m.id} role="button" tabIndex={-1} className={`ml-trow${sel === m.id ? ' on' : ''}`}
                        onClick={() => openRow(m)} onDoubleClick={() => openMailWindow(m.id)} title={`${m.subject || '(제목 없음)'}\n(두 번 누르면 새 창)`}>
                        <span className="c-star">{star}</span>
                        <span className="c-att">{m.attachments > 0 ? '📎' : ''}</span>
                        <span className="c-who" title={m.folder === 'sent' ? m.to : `${m.fromName} <${m.fromAddr}>`}>{who}</span>
                        <span className="c-subj">{m.subject || '(제목 없음)'}<i>{m.preview}</i></span>
                        <span className="c-date">{m.date ?? ''}</span>
                        <span className="c-size">{fmtSize(m.size)}</span>
                      </div>
                    ) : (
                      <div key={m.id} role="button" tabIndex={-1} className={`ml-row${sel === m.id ? ' on' : ''}`}
                        onClick={() => openRow(m)} onDoubleClick={() => openMailWindow(m.id)} title={`${m.subject || '(제목 없음)'}\n(두 번 누르면 새 창)`}>
                        <span className="ml-r1">
                          {star}
                          <b className="ml-who">{who}</b>
                          <span className="ml-date">{shortDate(m.date)}</span>
                        </span>
                        <span className="ml-subj">{m.attachments > 0 && <span className="ml-clip" title={`첨부 ${m.attachments}개`}>📎</span>}{m.subject || '(제목 없음)'}</span>
                        <span className="ml-prev">{m.preview}</span>
                      </div>
                    );
                  })}
                  {data && data.items.length === 0 && (
                    <div className="ml-empty">
                      {counts.all === 0 ? <>보관된 메일이 없습니다.<br />웹메일에서 받은 백업 ZIP 을 <b>[백업 ZIP 올리기]</b>로 올려 주세요.</> : '조건에 맞는 메일이 없습니다.'}
                    </div>
                  )}
                  {loading && !data && <div className="ml-empty">불러오는 중…</div>}
                </div>
                {pages > 1 && (
                  <div className="ml-pager">
                    <button type="button" disabled={page <= 1} onClick={() => setPage(1)} aria-label="처음">«</button>
                    <button type="button" disabled={page <= 1} onClick={() => setPage(p => p - 1)} aria-label="이전">‹</button>
                    {nums.map(n => <button key={n} type="button" className={n === page ? 'on' : ''} onClick={() => setPage(n)}>{n}</button>)}
                    <button type="button" disabled={page >= pages} onClick={() => setPage(p => p + 1)} aria-label="다음">›</button>
                    <button type="button" disabled={page >= pages} onClick={() => setPage(pages)} aria-label="끝">»</button>
                  </div>
                )}
              </div>

              {view !== 'list' && (
                <div className="ml-splitter" role="separator" tabIndex={0}
                  aria-orientation={view === 'right' ? 'vertical' : 'horizontal'}
                  aria-label="목록 칸 크기 — 끌거나 화살표 키로 조절, 두 번 누르면 처음 크기"
                  title="끌어서 목록 칸 크기 조절 · 두 번 누르면 처음 크기"
                  onPointerDown={startDrag} onKeyDown={keySplit}
                  onDoubleClick={() => (view === 'right' ? setListW(SPLIT_DEFAULT.right) : setListH(SPLIT_DEFAULT.bottom))} />
              )}
              {view !== 'list' && (
                <div className="ml-reader" aria-label="메일 내용">
                  {sel === null ? <div className="ml-empty ml-pick">메일을 고르면 여기에 내용이 보입니다.<br /><span className="ml-dim">두 번 누르면 새 창으로 열립니다.</span></div>
                    : detailErr ? <div className="ml-err">{detailErr}</div>
                    : !detail ? <div className="ml-empty">여는 중…</div>
                    : <MailReader d={detail}
                        onStar={() => void toggleStar(detail.id, detail.starred)}
                        onNewWindow={() => openMailWindow(detail.id)}
                        onBack={() => setSel(null)}
                        onSender={addr => { setField('from'); setQ(addr); setFolder('all'); setSel(null); }}
                        onError={setDetailErr} />}
                </div>
              )}
            </div>
          </section>
        </div>
      </div>
    </div>
  );
}
