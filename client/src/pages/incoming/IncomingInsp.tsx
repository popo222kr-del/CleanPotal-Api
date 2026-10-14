import { useCallback, useEffect, useRef, useState } from 'react';
import { api } from '../../api/client';
import { toKeys } from '../../components/koreanKeys';
import './IncomingInsp.css';

// 세정 작업 › 삼성 입고검사 이력 — 입고검사하면서 옆 모니터로 그 부품의 지난 회차를 바로 본다.
// 왼쪽: 지금 입고·입고검사 공정의 LOT(직전 회차 경고 배지) / 오른쪽: 고른 부품의 이번 + 이전 5회 비교.
// 바코드 스캐너는 키보드처럼 들어오니, 검색칸에 커서를 두고 라벨을 찍으면 바로 열린다(LOT ID · S/N · 반출번호 아무거나).

interface WaitRow { lotId: string; sn: string; matDesc: string; matId: string; customer: string; group: string; line: string; useCount: string; oper: string; operName: string; inTime: string | null; prevUse: string | null; flags: string[] }
interface Cycle {
  lotId: string; useCount: string; inDay: string | null; outDay: string | null; status: string; current: boolean;
  weightIn: string | null; weightOut: string | null; etch: string | null; inVisual: string[]; outVisual: string[];
  particle: string | null; rough: string | null; result: string | null; outOfSpec: string[]; comment: string;
}
interface Spec { param: string; oper: string; name: string; side: string; text: string }
interface History {
  configured: boolean; lotId: string; sn: string; matDesc: string; matId: string; customer: string; line: string; inLine: string;
  useCount: string; status: string; inTime: string | null; spec: Spec[]; cycles: Cycle[]; warnings: string[];
}

// 외관 항목은 MES 이름 그대로(CHIP·CRACK·SCR·STAIN·PIT·STRIP·UNETCH)
const vis = (v: string[]) => (v.length ? v.join('·') : '이상 없음');

/** 왼쪽 대기 목록 너비(끌어서 조절) — 이 PC 에만 기억 */
const WIDTH_KEY = 'ii-wait-width';
const readWidth = () => { try { const n = Number(localStorage.getItem(WIDTH_KEY)); return n >= 260 && n <= 900 ? n : 380; } catch { return 380; } };

/** win = 새 창(사이드바 없이 화면 가득) */
export default function IncomingInsp({ win = false }: { win?: boolean }) {
  const [wait, setWait] = useState<WaitRow[] | null>(null);
  const [configured, setConfigured] = useState(true);
  const [waitErr, setWaitErr] = useState('');
  const [q, setQ] = useState('');
  const [h, setH] = useState<History | null>(null);
  const [err, setErr] = useState('');
  const [loading, setLoading] = useState(false);
  const [filter, setFilter] = useState('');
  const [warnOnly, setWarnOnly] = useState(false);
  const [width, setWidth] = useState(readWidth);
  const saveWidth = (n: number) => { setWidth(n); try { localStorage.setItem(WIDTH_KEY, String(n)); } catch { /* 기억 못 해도 된다 */ } };
  function startDrag(e: React.MouseEvent) {
    e.preventDefault();
    const x0 = e.clientX, w0 = width;
    let last = w0;
    const move = (ev: MouseEvent) => { last = Math.min(900, Math.max(260, w0 + ev.clientX - x0)); setWidth(last); };
    const up = () => { window.removeEventListener('mousemove', move); window.removeEventListener('mouseup', up); document.body.style.cursor = ''; saveWidth(last); };
    document.body.style.cursor = 'col-resize';
    window.addEventListener('mousemove', move); window.addEventListener('mouseup', up);
  }
  const box = useRef<HTMLInputElement>(null);

  const loadWait = useCallback(async () => {
    try {
      const r = await api.get<{ configured: boolean; rows?: WaitRow[] }>('/api/incoming-insp/wait');
      setConfigured(r.configured); setWait(r.rows ?? []); setWaitErr('');
    } catch (e) { setWaitErr(e instanceof Error ? e.message : '불러오지 못했습니다.'); }
  }, []);
  useEffect(() => {
    void loadWait();
    const t = setInterval(() => void loadWait(), 60_000);   // 1분마다 대기 목록 새로
    return () => clearInterval(t);
  }, [loadWait]);

  // 한글 입력기로 찍히면 마지막 글자가 아직 조합 중일 때 Enter 가 온다 — 조합이 끝난 뒤 찾고, 같은 값을 두 번 찾지 않는다.
  const composingEnter = useRef(false);
  const lastOpen = useRef({ key: '', at: 0 });

  async function open(key: string) {
    const t = toKeys(key);   // "1누261014004ㅔ" → "1SN261014004P"
    if (t.length < 3) return;
    const now = Date.now();
    if (lastOpen.current.key === t && now - lastOpen.current.at < 800) return;
    lastOpen.current = { key: t, at: now };
    setLoading(true); setErr('');
    try { setH(await api.get<History>(`/api/incoming-insp/history?q=${encodeURIComponent(t)}`)); }
    catch (e) { setErr(e instanceof Error ? e.message : '찾지 못했습니다.'); }
    finally {
      setLoading(false);
      // 다음 부품을 바로 찍을 수 있게 검색칸 비우고 커서
      setQ(''); box.current?.focus();
    }
  }

  if (!configured) {
    return <div className="ii-page"><div className="ii-empty">기존 MES DB 연결이 설정되지 않았습니다(ConnectionStrings:LegacyMes). 관리자에게 문의하세요.</div></div>;
  }
  const f = filter.trim().toLowerCase();
  const warned = (w: WaitRow) => w.flags.some(x => x !== '첫 입고');
  const all = wait ?? [];
  const rows = all.filter(w => (!warnOnly || warned(w)) && (!f || [w.lotId, w.sn, w.matDesc, w.matId, w.line].some(x => x.toLowerCase().includes(f))));
  // 라인별로 묶고(라인 이름 순), 안에서는 최근 입고부터
  const byLine = new Map<string, WaitRow[]>();
  rows.forEach(w => { const k = w.line || '(라인 없음)'; byLine.set(k, [...(byLine.get(k) ?? []), w]); });
  const lines = [...byLine.entries()].sort((a, b) => a[0].localeCompare(b[0], 'ko', { numeric: true }))
    .map(([k, list]) => [k, [...list].sort((x, y) => (y.inTime ?? '').localeCompare(x.inTime ?? ''))] as const);
  const warnCount = all.filter(warned).length;
  // 목록에는 '외관 이상 이력'·'NG 이력' 만 — 어느 항목·몇 회차인지는 마우스를 올리거나 눌러서 오른쪽에서
  const summary = (warn: string[]) => [warn.some(x => !x.startsWith('NG')) && '외관 이상 이력', warn.some(x => x.startsWith('NG')) && 'NG 이력']
    .filter(Boolean).join(' · ');
  const today = new Date();
  const todayKey = `${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;
  const when = (t: string | null) => (!t ? '' : t.startsWith(todayKey) ? t.slice(6) : t.slice(0, 5));

  return (
    <div className={`ii-page${win ? ' win' : ''}`}>
      <header className="ii-top">
        <h2>삼성 입고검사 이력</h2>
        <input ref={box} className="input ii-scan" autoFocus value={q} placeholder="LOT ID · S/N · 반출번호 — 바코드를 찍거나 입력 후 Enter"
          lang="en" autoComplete="off" spellCheck={false}
          onChange={e => setQ(e.target.value)}
          onKeyDown={e => {
            if (e.key !== 'Enter') return;
            e.preventDefault();
            if (e.nativeEvent.isComposing) { composingEnter.current = true; return; }
            void open(e.currentTarget.value);
          }}
          onCompositionEnd={() => {
            if (!composingEnter.current) return;
            composingEnter.current = false;
            setTimeout(() => void open(box.current?.value ?? ''), 0);
          }} />
        <button type="button" className="btn btn-primary" onClick={() => void open(q)} disabled={loading}>{loading ? '찾는 중…' : '조회'}</button>
        {win
          ? <button type="button" className="btn btn-ghost" onClick={() => window.close()}>닫기</button>
          : <button type="button" className="btn btn-ghost" onClick={() => window.open('/incoming-insp/window', 'incoming-insp',
              `width=${screen.availWidth - 20},height=${screen.availHeight - 40},left=10,top=10,resizable=yes,scrollbars=yes`)}>새 창으로 보기</button>}
      </header>

      <div className="ii-body" style={{ gridTemplateColumns: `${width}px 8px 1fr` }}>
        <aside className="ii-wait">
          <div className="ii-wait-h">
            <b>입고검사 대기</b><span className="ii-dim">삼성 · {wait ? `${rows.length}건` : ''}</span>
            <button type="button" className="ii-link" onClick={() => void loadWait()} title="새로고침(1분마다 자동)">↻</button>
          </div>
          <div className="ii-tools">
            <input className="input ii-filter" placeholder="찾기(품명·세정코드·S/N·라인)" value={filter} onChange={e => setFilter(e.target.value)} />
            <button type="button" className={`ii-warnbtn ${warnOnly ? 'on' : ''}`} onClick={() => setWarnOnly(v => !v)} disabled={warnCount === 0 && !warnOnly}>
              ⚠ 외관 이상 {warnCount > 0 && <b>{warnCount}</b>}
            </button>
          </div>
          {waitErr && <div className="ii-err">{waitErr}</div>}
          {!wait && !waitErr && <div className="ii-dim ii-pad">불러오는 중…</div>}
          <div className="ii-list">
            {lines.map(([line, list]) => (
              <section key={line}>
                <h4>{line}<span>{list.length}</span></h4>
                {list.map(w => {
                  const warn = w.flags.filter(x => x !== '첫 입고');
                  return (
                    <button type="button" key={w.lotId} className={`ii-row ${h?.lotId === w.lotId ? 'on' : ''}`} onClick={() => void open(w.lotId)}
                      title={[w.lotId, ...w.flags].filter(Boolean).join('\n')}>
                      <span className="tm">{when(w.inTime)}</span>
                      <span className="nm">{w.matDesc}</span>
                      <span className="use">{w.useCount ? `${w.useCount}회` : ''}</span>
                      <span className="mk">{warn.length > 0 ? <i className="warn">⚠</i> : w.flags.includes('첫 입고') ? <i className="new">NEW</i> : null}</span>
                      <span className="sub">
                        <span className="code">{w.matId}</span><span className="sn">{w.sn || '—'}</span>
                        {warn.length > 0 && <span className="why">{summary(warn)}</span>}
                      </span>
                    </button>
                  );
                })}
              </section>
            ))}
            {wait && rows.length === 0 && <div className="ii-dim ii-none">{warnOnly ? '경고가 있는 LOT 이 없습니다.' : '입고검사 대기 중인 LOT 이 없습니다.'}</div>}
          </div>
          <div className="ii-legend"><span><i className="warn">⚠</i> 최근 출하 5회 중 외관 이상·NG 이력</span><span><i className="new">NEW</i> 첫 입고</span></div>
        </aside>
        <div className="ii-split" role="separator" aria-orientation="vertical" title="끌어서 너비 조절(두 번 누르면 처음대로)"
          onMouseDown={startDrag} onDoubleClick={() => saveWidth(380)} />

        <section className="ii-main">
          {err && <div className="ii-err">{err}</div>}
          {!h && !err && <div className="ii-empty">왼쪽 목록에서 고르거나, 위 칸에 바코드를 찍으세요.<br />같은 부품(반출 S/N)의 <b>이번 + 이전 5회</b> 측정값·판정·메모가 나옵니다.</div>}
          {h && <HistoryView h={h} onOpen={open} />}
        </section>
      </div>
    </div>
  );
}

function HistoryView({ h, onOpen }: { h: History; onOpen: (k: string) => void }) {
  const rows: [string, (c: Cycle) => React.ReactNode, ((c: Cycle) => boolean)?][] = [
    ['입고일 → 출고일', c => <div className="ii-days"><span>{c.inDay ?? '—'}</span><span className="ii-dim">→ {c.outDay ?? (c.current ? '진행 중' : '—')}</span></div>],
    ['LOT ID', c => <button type="button" className="ii-link" onClick={() => onOpen(c.lotId)}>{c.lotId}</button>],
    ['상태', c => c.status],
    ['입고 외관', c => vis(c.inVisual), c => c.inVisual.length > 0],
    ['WEIGHT IN → OUT', c => (c.weightIn || c.weightOut ? <>{c.weightIn ?? '—'}<span className="ii-dim"> → </span>{c.weightOut ?? '—'}</> : '—'),
      c => c.outOfSpec.some(o => o.includes('WEIGHT'))],
    ['식각량', c => c.etch ?? '—'],
    ['PARTICLE', c => c.particle ?? '—', c => c.outOfSpec.some(o => o.includes('PARTICLE'))],
    ['출하 외관', c => vis(c.outVisual), c => c.outVisual.length > 0],
    ['RESULT (판정)', c => c.result ?? '—', c => (c.result ?? '').toUpperCase() === 'NG'],
    ['COMMENT', c => (c.comment ? <div className="ii-cmt" title={c.comment}>{c.comment}</div> : <span className="ii-dim">—</span>)],
  ];
  const inSpec = h.spec.filter(s => s.side === '입고');
  const outSpec = h.spec.filter(s => s.side === '출하');
  return (
    <>
      <div className="ii-card ii-head">
        <div className="t">
          <b>{h.matDesc}</b>
          <span className="ii-chip">{h.useCount ? `이번 ${h.useCount}회차` : '사용횟수 없음'}</span>
          <span className="ii-dim">{h.status}</span>
        </div>
        <div className="kv">
          <span><em>S/N</em>{h.sn || '—'}</span><span><em>LOT</em>{h.lotId}</span><span><em>세정코드</em>{h.matId}</span>
          <span><em>고객사</em>{h.customer}</span>
          <span><em>라인</em>{h.line}{h.inLine && h.inLine !== h.line ? <b className="ii-red"> → 반입 {h.inLine}</b> : ''}</span>
          <span><em>입고</em>{h.inTime ?? '—'}</span>
        </div>
        {(inSpec.length > 0 || outSpec.length > 0) && (
          <div className="spec">
            <em>검사 기준</em>
            {inSpec.length > 0 && <span>입고: {inSpec.map(s => `${s.name} ${s.text}`).join(' · ')}</span>}
            {outSpec.length > 0 && <span>출하: {outSpec.map(s => `${s.name} ${s.text}`).join(' · ')}</span>}
          </div>
        )}
      </div>

      {h.warnings.length > 0 ? (
        <div className="ii-card ii-warn">{h.warnings.map(w => <div key={w}>⚠ {w}</div>)}</div>
      ) : h.cycles.length > 1 ? <div className="ii-card ii-ok">✔ 지난 회차에 판정 NG·외관 이상·기준 밖 값이 없습니다.</div>
        : <div className="ii-card ii-ok new">이 S/N 의 지난 이력이 없습니다(첫 입고).</div>}

      <div className="ii-card ii-cmp-wrap">
        <table className="ii-cmp">
          <thead>
            <tr><th />{h.cycles.map(c => <th key={c.lotId} className={c.current ? 'cur' : ''}>{c.useCount ? `${c.useCount}회` : '—'}{c.current ? ' (이번)' : ''}</th>)}</tr>
          </thead>
          <tbody>
            {rows.map(([label, cell, bad]) => (
              <tr key={label}>
                <th>{label}</th>
                {h.cycles.map(c => <td key={c.lotId} className={`${c.current ? 'cur' : ''}${bad?.(c) ? ' bad' : ''}`}>{cell(c)}</td>)}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}
