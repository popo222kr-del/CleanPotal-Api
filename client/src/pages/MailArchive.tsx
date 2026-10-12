import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { api, download, getToken, ApiError } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { useMailAccess } from '../components/mailAccess';
import './MailArchive.css';

// OFFICE › 메일 확인 — 웹메일 용량이 모자라 못 보는 메일을 백업 ZIP 으로 올려 두고 Outlook 처럼 찾아본다.
// 본인 메일만 보이고(관리자도 남의 메일은 못 본다), 사내(회사 와이파이·PC)에서만 열린다. 접두사 ml-

interface Row {
  id: number; folder: 'inbox' | 'sent'; subject: string; fromName: string; fromAddr: string; to: string;
  date: string | null; preview: string; attachments: number; size: number;
}
interface Page { total: number; page: number; size: number; inbox: number; sent: number; items: Row[] }
interface Att { index: number; name: string; contentType: string; size: number }
interface Detail {
  id: number; folder: string; subject: string; from: string; to: string; cc: string; date: string | null;
  html: string | null; text: string | null; attachments: Att[]; remoteImagesBlocked: boolean;
}
interface ImportRow { id: number; fileName: string; importedAt: string; bytes: number; total: number; added: number; duplicates: number; failed: number }
interface Job {
  id: string; fileName: string; status: 'running' | 'done' | 'failed'; processed: number; error: string | null;
  result: { importId: number; total: number; added: number; duplicates: number; failed: number } | null;
}
/** 올리는 중인 파일 하나의 상태(화면 표시용) */
interface UpTask { key: string; name: string; size: number; sent: number; phase: 'upload' | 'import' | 'done' | 'failed'; processed: number; msg: string }

type Folder = 'all' | 'inbox' | 'sent';
const PAGE_SIZE = 50;
const CHUNK = 6 * 1024 * 1024;   // 서버 한도(8MB)보다 작게

const fmtSize = (n: number) => (n >= 1024 * 1024 * 1024 ? `${(n / 1024 / 1024 / 1024).toFixed(1)}GB`
  : n >= 1024 * 1024 ? `${(n / 1024 / 1024).toFixed(1)}MB` : n >= 1024 ? `${Math.round(n / 1024)}KB` : `${n}B`);
const shortDate = (d: string | null) => {
  if (!d) return '';
  const today = new Date();
  const p = (n: number) => String(n).padStart(2, '0');
  const t = `${today.getFullYear()}-${p(today.getMonth() + 1)}-${p(today.getDate())}`;
  return d.startsWith(t) ? d.slice(11) : d.startsWith(String(today.getFullYear())) ? d.slice(5, 10).replace('-', '/') : d.slice(0, 10);
};
const newId = () => (crypto.randomUUID?.() ?? `${Date.now()}${Math.random()}`).replace(/[^0-9a-f]/gi, '').padEnd(32, '0').slice(0, 32).toLowerCase();

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

/** 메일 본문을 격리된 칸에 — 스크립트는 못 돌고, 바깥 그림·글꼴은 불러오지 않는다(열람 추적 방지). 링크는 새 창. */
function MailBody({ html }: { html: string }) {
  const ref = useRef<HTMLIFrameElement>(null);
  const doc = useMemo(() => `<!doctype html><html><head><meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data:; style-src 'unsafe-inline'; font-src data:">
<base target="_blank"><style>body{margin:0;padding:16px 18px;font:14px/1.6 -apple-system,'Malgun Gothic',sans-serif;color:#1f2937;word-break:break-word}
img{max-width:100%;height:auto}table{max-width:100%}pre{white-space:pre-wrap}</style></head><body>${html}</body></html>`, [html]);
  const fit = () => {
    const f = ref.current;
    const h = f?.contentDocument?.documentElement?.scrollHeight;
    if (f && h) f.style.height = `${h + 8}px`;
  };
  return <iframe ref={ref} className="ml-frame" title="메일 본문" sandbox="allow-same-origin allow-popups allow-popups-to-escape-sandbox" srcDoc={doc} onLoad={fit} />;
}

export default function MailArchive() {
  const { user } = useAuth();
  const access = useMailAccess(user?.id);
  const [folder, setFolder] = useState<Folder>('all');
  const [q, setQ] = useState('');
  const [query, setQuery] = useState('');
  const [page, setPage] = useState(1);
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
  const ready = !!access?.allowed && !!access?.internal;

  // 검색어는 잠깐 멈췄을 때 찾는다
  useEffect(() => { const t = setTimeout(() => { setQuery(q.trim()); setPage(1); }, 300); return () => clearTimeout(t); }, [q]);

  const load = useCallback(async () => {
    setLoading(true); setErr('');
    try {
      const p = new URLSearchParams({ page: String(page), size: String(PAGE_SIZE) });
      if (folder !== 'all') p.set('folder', folder);
      if (query) p.set('q', query);
      setData(await api.get<Page>(`/api/mail?${p}`));
    } catch (e) { setErr(e instanceof Error ? e.message : '목록을 불러오지 못했습니다.'); }
    finally { setLoading(false); }
  }, [folder, query, page]);
  useEffect(() => { if (ready) void load(); }, [ready, load]);

  useEffect(() => {
    if (sel === null) { setDetail(null); return; }
    let alive = true;
    setDetail(null); setDetailErr('');
    api.get<Detail>(`/api/mail/${sel}`)
      .then(d => { if (alive) setDetail(d); })
      .catch(e => { if (alive) setDetailErr(e instanceof Error ? e.message : '메일을 열지 못했습니다.'); });
    return () => { alive = false; };
  }, [sel]);

  const loadImports = useCallback(() => api.get<ImportRow[]>('/api/mail/imports').then(setImports).catch(() => setImports([])), []);
  useEffect(() => { if (showImports && ready) void loadImports(); }, [showImports, ready, loadImports]);

  const patch = (key: string, p: Partial<UpTask>) => setTasks(ts => ts.map(t => (t.key === key ? { ...t, ...p } : t)));

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
        msg: `${r.added.toLocaleString()}통 보관${r.duplicates ? ` · 중복 ${r.duplicates.toLocaleString()}통 건너뜀` : ''}${r.failed ? ` · 읽지 못함 ${r.failed}통` : ''}` });
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

  async function saveFile(path: string, fallbackName: string) {
    try {
      const res = await download(path);
      const cd = res.headers.get('content-disposition') ?? '';
      const m = /filename\*=UTF-8''([^;]+)/i.exec(cd) ?? /filename="?([^";]+)"?/i.exec(cd);
      const name = m ? decodeURIComponent(m[1]) : fallbackName;
      const url = URL.createObjectURL(await res.blob());
      const a = document.createElement('a');
      a.href = url; a.download = name;
      document.body.appendChild(a); a.click(); a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 10_000);
    } catch (e) { setDetailErr(e instanceof Error ? e.message : '받지 못했습니다.'); }
  }

  async function removeImport(r: ImportRow) {
    if (!confirm(`'${r.fileName}' 로 가져온 메일 ${r.added.toLocaleString()}통을 이 보관함에서 지울까요?\n\n웹메일 서버의 메일은 건드리지 않습니다. 다시 올리면 다시 들어옵니다.`)) return;
    try {
      await api.del(`/api/mail/imports/${r.id}`);
      setSel(null);
      void loadImports(); void load();
    } catch (e) { setErr(e instanceof Error ? e.message : '지우지 못했습니다.'); }
  }

  // ── 쓸 수 없을 때 ──
  if (!access) return <div className="page-loading">불러오는 중…</div>;
  if (!access.allowed || !access.internal) {
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

  const pages = data ? Math.max(1, Math.ceil(data.total / PAGE_SIZE)) : 1;
  const busy = tasks.some(t => t.phase === 'upload' || t.phase === 'import');
  const all = data ? data.inbox + data.sent : 0;

  return (
    <div className={`ml-page${sel !== null ? ' reading' : ''}`}>
      <header className="pg-header">
        <div>
          <h2>메일 확인</h2>
          <p>웹메일 백업 ZIP 을 올려 두고 찾아봅니다 · 본인 메일만 보이고, 사내에서만 열립니다</p>
        </div>
        <div className="ml-head-r">
          <button type="button" className="btn btn-ghost" onClick={() => setShowImports(v => !v)} aria-expanded={showImports}>가져오기 기록</button>
          <button type="button" className="btn btn-primary" disabled={busy} onClick={() => fileRef.current?.click()}>
            {busy ? '올리는 중…' : '백업 ZIP 올리기'}
          </button>
          <input ref={fileRef} type="file" accept=".zip,.eml" multiple hidden onChange={e => void onFiles(e.target.files)} />
        </div>
      </header>

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

        {showImports && (
          <div className="ml-imports">
            <div className="ml-imports-h">가져오기 기록 <span>잘못 올린 것은 지울 수 있습니다(웹메일 서버의 메일은 그대로)</span></div>
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

        <div className="ml-shell">
          <nav className="ml-folders" aria-label="메일함">
            {([['all', '전체', all], ['inbox', '받은메일함', data?.inbox ?? 0], ['sent', '보낸메일함', data?.sent ?? 0]] as [Folder, string, number][]).map(([k, l, n]) => (
              <button key={k} type="button" className={folder === k ? 'on' : ''} onClick={() => { setFolder(k); setPage(1); setSel(null); }}>
                <span>{l}</span><em>{n.toLocaleString()}</em>
              </button>
            ))}
          </nav>

          <section className="ml-list" aria-label="메일 목록">
            <div className="ml-search">
              <input className="input" type="search" placeholder="제목·보낸 사람·받는 사람·본문 검색" value={q} onChange={e => setQ(e.target.value)} />
              {data && <span className="ml-count">{data.total.toLocaleString()}통</span>}
            </div>
            {err && <div className="ml-err">{err}</div>}
            <div className="ml-rows">
              {data?.items.map(m => (
                <button key={m.id} type="button" className={`ml-row${sel === m.id ? ' on' : ''}`} onClick={() => setSel(m.id)}>
                  <span className="ml-r1">
                    <b className="ml-who">{m.folder === 'sent' ? `→ ${m.to.split(',')[0] || '(받는 사람 없음)'}` : (m.fromName || m.fromAddr || '(보낸 사람 없음)')}</b>
                    <span className="ml-date">{shortDate(m.date)}</span>
                  </span>
                  <span className="ml-subj">{m.attachments > 0 && <span className="ml-clip" title={`첨부 ${m.attachments}개`}>📎</span>}{m.subject || '(제목 없음)'}</span>
                  <span className="ml-prev">{m.preview}</span>
                </button>
              ))}
              {data && data.items.length === 0 && (
                <div className="ml-empty">
                  {all === 0 ? <>보관된 메일이 없습니다.<br />웹메일에서 받은 백업 ZIP 을 <b>[백업 ZIP 올리기]</b>로 올려 주세요.</> : '찾는 메일이 없습니다.'}
                </div>
              )}
              {loading && !data && <div className="ml-empty">불러오는 중…</div>}
            </div>
            {pages > 1 && (
              <div className="ml-pager">
                <button type="button" disabled={page <= 1} onClick={() => setPage(p => p - 1)}>‹ 이전</button>
                <span>{page} / {pages}</span>
                <button type="button" disabled={page >= pages} onClick={() => setPage(p => p + 1)}>다음 ›</button>
              </div>
            )}
          </section>

          <section className="ml-reader" aria-label="메일 내용">
            {sel === null ? <div className="ml-empty ml-pick">왼쪽 목록에서 메일을 고르세요.</div>
              : detailErr ? <div className="ml-err">{detailErr}</div>
              : !detail ? <div className="ml-empty">여는 중…</div> : (
                <>
                  <div className="ml-rhead">
                    <button type="button" className="ml-back" onClick={() => setSel(null)}>‹ 목록</button>
                    <h3>{detail.subject || '(제목 없음)'}</h3>
                    <dl>
                      <dt>보낸 사람</dt><dd>{detail.from}</dd>
                      <dt>받는 사람</dt><dd>{detail.to || '—'}</dd>
                      {detail.cc && <><dt>참조</dt><dd>{detail.cc}</dd></>}
                      <dt>날짜</dt><dd>{detail.date ?? '—'}</dd>
                    </dl>
                    <div className="ml-acts">
                      {detail.attachments.map(a => (
                        <button key={a.index} type="button" className="ml-att" title={`${a.name} 받기`}
                          onClick={() => void saveFile(`/api/mail/${detail.id}/attachments/${a.index}`, a.name)}>
                          📎 {a.name} <em>{fmtSize(a.size)}</em>
                        </button>
                      ))}
                      <button type="button" className="ml-eml" onClick={() => void saveFile(`/api/mail/${detail.id}/eml`, 'mail.eml')}>원본(.eml) 받기</button>
                    </div>
                    {detail.remoteImagesBlocked && <div className="ml-note">보안을 위해 바깥 그림은 표시하지 않았습니다.</div>}
                  </div>
                  <div className="ml-content">
                    {detail.html !== null ? <MailBody html={detail.html} /> : <pre className="ml-text">{detail.text ?? '(본문 없음)'}</pre>}
                  </div>
                </>
              )}
          </section>
        </div>
      </div>
    </div>
  );
}
