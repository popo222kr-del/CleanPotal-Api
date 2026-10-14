import { useCallback, useEffect, useRef, useState } from 'react';
import { api } from '../../api/client';
import { toKeys } from '../../components/koreanKeys';
import './IncomingInsp.css';

// 세정 작업 › 입고검사 이력 — 입고검사하면서 옆 모니터로 그 부품의 지난 회차를 바로 본다.
// 왼쪽: 지금 입고·입고검사 공정의 LOT(직전 회차 경고 배지) / 오른쪽: 고른 부품의 이번 + 이전 5회 비교.
// 바코드 스캐너는 키보드처럼 들어오니, 검색칸에 커서를 두고 라벨을 찍으면 바로 열린다(LOT ID · S/N · 반출번호 아무거나).

interface WaitRow { lotId: string; sn: string; matDesc: string; matId: string; customer: string; line: string; useCount: string; oper: string; operName: string; inTime: string | null; prevUse: string | null; flags: string[] }
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

const VIS_NAME: Record<string, string> = { CHIP: '깨짐', CRACK: 'CRACK', SCR: '긁힘', STAIN: '얼룩', PIT: '패임', STRIP: '벗겨짐', UNETCH: 'UNETCH' };
const vis = (v: string[]) => (v.length ? v.map(p => VIS_NAME[p] ?? p).join('·') : '이상 없음');

export default function IncomingInsp() {
  const [wait, setWait] = useState<WaitRow[] | null>(null);
  const [configured, setConfigured] = useState(true);
  const [waitErr, setWaitErr] = useState('');
  const [q, setQ] = useState('');
  const [h, setH] = useState<History | null>(null);
  const [err, setErr] = useState('');
  const [loading, setLoading] = useState(false);
  const [filter, setFilter] = useState('');
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
  const rows = (wait ?? []).filter(w => !f || [w.lotId, w.sn, w.matDesc, w.matId, w.line].some(x => x.toLowerCase().includes(f)));

  return (
    <div className="ii-page">
      <header className="ii-top">
        <h2>입고검사 이력</h2>
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
      </header>

      <div className="ii-body">
        <aside className="ii-wait">
          <div className="ii-wait-h">
            <b>입고검사 대기</b><span className="ii-dim">{wait ? `${rows.length}건` : ''}</span>
            <button type="button" className="ii-link" onClick={() => void loadWait()}>새로고침</button>
          </div>
          <input className="input ii-filter" placeholder="목록에서 찾기(품명·S/N·라인)" value={filter} onChange={e => setFilter(e.target.value)} />
          {waitErr && <div className="ii-err">{waitErr}</div>}
          {!wait && !waitErr && <div className="ii-dim ii-pad">불러오는 중…</div>}
          <ul>
            {rows.map(w => (
              <li key={w.lotId} className={h?.lotId === w.lotId ? 'on' : ''} onClick={() => void open(w.lotId)}>
                <div className="r1"><b>{w.matDesc}</b><span className={`ii-oper o${w.oper}`}>{w.operName}</span></div>
                <div className="r2"><span>{w.sn || '—'}</span><span>{w.line}</span><span>{w.useCount ? `${w.useCount}회차` : ''}</span><span className="ii-dim">{w.inTime}</span></div>
                {w.flags.length > 0 && (
                  <div className="r3">{w.flags.map(x => <span key={x} className={`ii-flag ${x === '첫 입고' ? 'new' : 'warn'}`}>{x === '첫 입고' ? x : `⚠ ${x}`}</span>)}</div>
                )}
              </li>
            ))}
            {wait && rows.length === 0 && <li className="ii-dim ii-none">대기 중인 LOT 이 없습니다.</li>}
          </ul>
        </aside>

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
    ['무게 IN → OUT', c => (c.weightIn || c.weightOut ? <>{c.weightIn ?? '—'}<span className="ii-dim"> → </span>{c.weightOut ?? '—'}</> : '—'),
      c => c.outOfSpec.some(o => o.includes('무게'))],
    ['식각량', c => c.etch ?? '—'],
    ['표면먼지', c => c.particle ?? '—', c => c.outOfSpec.some(o => o.includes('표면먼지'))],
    ['출하 외관', c => vis(c.outVisual), c => c.outVisual.length > 0],
    ['판정', c => c.result ?? '—', c => (c.result ?? '').toUpperCase() === 'NG'],
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
