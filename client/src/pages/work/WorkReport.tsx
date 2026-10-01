import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../../api/client';
import './Work.css';
import type { DailyBoardEq, DailyReport } from '../../api/types';
import { addDays, buildDaily, cellPill, cellText, cellTone, dow, isGroupRow, mailHtml, mailText, md, orderedSections, shiftTone, todayYmd, type Block, type Cell, type Section } from './dailyModel';
import { drawBoard, SHIFT_RANGE, type BoardShift } from './boardImage';
import { useAccess } from '../../auth/useAccess';
import { copyRich } from './icpmsCopy';

// Daily 업무 보고 — 각 메뉴에 적힌 기록(근무·인수인계·체크시트·스케줄 보드·세정 현황·약액·KOH·BAKE)을 하루치로 모은다.
// 하루 = 그날 주간 + 그날 야간(다음 날 아침까지). 여기서 새로 입력하는 것은 없고, 고칠 때는 섹션의 '열기' 로 원래 메뉴로 간다.
// '메일로 복사' 는 같은 내용을 메일용 모양(한 열·스타일 내장, 스케줄 보드는 그림)으로 만들어 클립보드에 넣는다.

const boardTitle = (date: string, s: BoardShift) => `${date} (${dow(date)}) 스케줄보드 (${SHIFT_RANGE[s][2]})`;

/** 지금 교대 — 07~19시는 오늘 주간, 19시 이후는 오늘 야간, 07시 전은 어제 야간(야간은 다음 날 아침까지). */
function currentShift(): { date: string; shift: BoardShift } {
  const h = new Date().getHours();
  if (h < 7) return { date: addDays(todayYmd(), -1), shift: 'night' };
  return { date: todayYmd(), shift: h < 19 ? 'day' : 'night' };
}

export default function WorkReport() {
  const nav = useNavigate();
  const acc = useAccess();
  const [ordering, setOrdering] = useState(false);
  const [reload, setReload] = useState(0);
  const [date, setDate] = useState(() => currentShift().date);
  // 스케줄 보드 그림은 보내는 시간대 것만 — 주간에 보내면 주간, 야간에 보내면 야간(바꿀 수 있다)
  const [boardShift, setBoardShift] = useState<BoardShift>(() => currentShift().shift);
  const [data, setData] = useState<DailyReport | null>(null);
  const [err, setErr] = useState('');
  const [msg, setMsg] = useState('');

  useEffect(() => {
    let alive = true;
    setData(null); setErr('');
    api.get<DailyReport>(`/api/worklog/daily?date=${date}`)
      .then(r => { if (alive) setData(r); })
      .catch(e => { if (alive) setErr(e instanceof Error ? e.message : '불러오지 못했습니다.'); });
    return () => { alive = false; };
  }, [date, reload]);

  const model = useMemo(() => (data ? buildDaily(data, todayYmd(), boardShift) : null), [data, boardShift]);

  async function copyMail() {
    if (!model) return;
    // 스케줄 보드 그림은 복사할 때 PNG 로 만들어 본문에 넣는다
    const images: Record<string, string> = {};
    const eq = data?.board?.equipment ?? [];
    if (eq.length) images[boardShift] = drawBoard(eq, boardShift, boardTitle(model.date, boardShift), 2);
    const ok = await copyRich(mailHtml(model, images), mailText(model));
    setMsg(ok ? '복사했습니다 — 메일 본문에 붙여넣기(Ctrl+V) 하세요.' : '복사하지 못했습니다. 브라우저 권한을 확인하세요.');
    window.setTimeout(() => setMsg(''), 4000);
  }

  return (
    <div className="wf-page">
      <header className="pg-header">
        <div>
          <h2>세정팀 Daily 업무 보고</h2>
          <p>{md(date)} 주간 + 야간({md(addDays(date, 1))} 아침까지) · 각 메뉴 기록을 모아 자동으로 만듭니다</p>
        </div>
      </header>
      <div className="pg-body">
        <div className="wf-toolbar dr-toolbar">
          <div className="wf-monthnav">
            <button onClick={() => setDate(addDays(date, -1))} aria-label="전날">‹</button>
            <input className="input wf-date" type="date" value={date} onChange={e => e.target.value && setDate(e.target.value)} />
            <button onClick={() => setDate(addDays(date, 1))} aria-label="다음날">›</button>
          </div>
          {date !== currentShift().date && <button className="btn btn-ghost wf-sm" onClick={() => setDate(currentShift().date)}>오늘</button>}
          <div className="dr-seg" title="설비 진행 현황(스케줄 보드)에 넣을 교대">
            <span>스케줄 보드</span>
            <button className={boardShift === 'day' ? 'on' : ''} onClick={() => setBoardShift('day')}>주간</button>
            <button className={boardShift === 'night' ? 'on' : ''} onClick={() => setBoardShift('night')}>야간</button>
          </div>
          {acc.isAdmin && <button className="btn btn-ghost wf-sm dr-order-btn" onClick={() => setOrdering(true)} disabled={!data}>섹션 순서</button>}
          <button className="btn btn-primary dr-copy" onClick={copyMail} disabled={!model}>메일로 복사</button>
          {msg && <span className="dr-msg">{msg}</span>}
        </div>

        {err ? <div className="wf-empty">{err}</div> : !model ? <div className="wf-empty">불러오는 중…</div> : (
          <article className="dr-paper">
            <header className="dr-cover">
              <h1>세정팀 Daily 업무 보고</h1>
              <p>{model.dateLabel}<i>|</i>{model.shiftLabel}</p>
            </header>

            {model.kpis.length > 0 && (
              <div className="dr-kpis">
                {model.kpis.map(k => (
                  <div key={k.label} className="dr-kpi">
                    <span>{k.label}</span>
                    <b>{k.value}</b>
                    {k.sub && <em className={k.tone ?? ''}>{k.sub}</em>}
                  </div>
                ))}
              </div>
            )}

            <div className="dr-grid">
              {model.sections.map((s, i) => <SectionView key={s.key} s={s} no={i + 1} onOpen={() => s.link && nav(s.link)} />)}
            </div>
          </article>
        )}
      </div>
      {ordering && data && (
        <OrderEditor saved={data.order ?? []} onClose={() => setOrdering(false)}
                     onSaved={() => { setOrdering(false); setReload(n => n + 1); }} />
      )}
    </div>
  );
}

/** 관리자 — 섹션 순서 바꾸기. 모든 사람의 보고서·메일에 같은 순서로 적용된다. */
function OrderEditor({ saved, onClose, onSaved }: { saved: string[]; onClose: () => void; onSaved: () => void }) {
  const [list, setList] = useState(() => orderedSections(saved));
  const [busy, setBusy] = useState(false);
  const move = (i: number, d: number) => setList(l => {
    const j = i + d;
    if (j < 0 || j >= l.length) return l;
    const n = [...l]; [n[i], n[j]] = [n[j], n[i]]; return n;
  });
  async function save(order: string[]) {
    setBusy(true);
    try { await api.put('/api/worklog/daily/order', { order }); onSaved(); }
    catch (e) { alert(e instanceof Error ? e.message : '저장하지 못했습니다.'); setBusy(false); }
  }
  return (
    <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="modal-box dr-ord" role="dialog" aria-label="섹션 순서">
        <h3>섹션 순서</h3>
        <p className="dr-ord-hint">▲▼ 로 순서를 바꾸세요. 모든 사람의 보고서와 메일 복사에 같은 순서로 적용됩니다. 볼 권한이 없는 섹션은 그 사람에게 빠져서 보입니다.</p>
        <ol className="dr-ord-list">
          {list.map((x, i) => (
            <li key={x.key}>
              <span className="dr-ord-no">{String(i + 1).padStart(2, '0')}</span>
              <b>{x.label}</b>
              <span className="dr-ord-move">
                <button type="button" onClick={() => move(i, -1)} disabled={i === 0} aria-label="위로">▲</button>
                <button type="button" onClick={() => move(i, 1)} disabled={i === list.length - 1} aria-label="아래로">▼</button>
              </span>
            </li>
          ))}
        </ol>
        <div className="modal-actions">
          <button type="button" className="btn btn-ghost dr-ord-reset" onClick={() => save([])} disabled={busy}>기본 순서로</button>
          <button type="button" className="btn btn-ghost" onClick={onClose}>취소</button>
          <button type="button" className="btn btn-primary" onClick={() => save(list.map(x => x.key))} disabled={busy}>{busy ? '저장 중...' : '저장'}</button>
        </div>
      </div>
    </div>
  );
}

function SectionView({ s, no, onOpen }: { s: Section; no: number; onOpen: () => void }) {
  return (
    <section id={`dr-${s.key}`} className={`dr-sec ${s.half ? 'half' : ''}`}>
      <div className="dr-head">
        <h3><span>{String(no).padStart(2, '0')}</span>{s.title}</h3>
        {s.badge && <span className={`dr-badge ${s.badge.tone}`}>{s.badge.t}</span>}
        {s.link && <button className="dr-open" onClick={onOpen}>열기 ›</button>}
      </div>
      {s.blocks.map((b, j) => <BlockView key={j} b={b} />)}
    </section>
  );
}

function CellView({ x }: { x: Cell }) {
  if (cellPill(x)) return <span className={`dr-pill sm ${cellTone(x)}`}>{cellText(x)}</span>;
  return <span className={`dr-t ${cellTone(x)}`}>{cellText(x) || ' '}</span>;
}

/** 스케줄 보드 그림 — 들어갈 칸의 실제 폭을 재서 그 폭 그대로 그린다(늘리거나 줄이지 않아 글자 크기가 일정). */
function BoardImage({ equipment, date, shift }: { equipment: DailyBoardEq[]; date: string; shift: BoardShift }) {
  const box = useRef<HTMLDivElement>(null);
  const [w, setW] = useState(0);
  useEffect(() => {
    const el = box.current;
    if (!el) return;
    const ro = new ResizeObserver(() => setW(Math.round(el.clientWidth / 20) * 20));   // 20px 단위로만 다시 그린다
    ro.observe(el);
    return () => ro.disconnect();
  }, []);
  // 폰처럼 좁으면 720px 로 그리고 옆으로 밀어 본다(너무 좁게 그리면 칸이 안 읽힌다)
  const drawW = Math.max(720, w);
  const src = useMemo(() => (w ? drawBoard(equipment, shift, boardTitle(date, shift), 2, drawW) : ''), [equipment, date, shift, w, drawW]);
  return <div ref={box} className="dr-board">{src && <img src={src} style={{ width: drawW, maxWidth: 'none' }} alt={`스케줄 보드 ${shift === 'day' ? '주간' : '야간'}`} />}</div>;
}

function BlockView({ b }: { b: Block }) {
  if (b.kind === 'note') return <p className="dr-note">{b.text}</p>;
  if (b.kind === 'text') return <div className="dr-text"><b>{b.label}</b><div>{b.body}</div></div>;
  if (b.kind === 'board') return <BoardImage equipment={b.equipment} date={b.date} shift={b.shift} />;
  if (b.kind === 'cols') return (
    <div className="dr-cols">
      {b.cols.map((x, i) => <div key={i} className={`dr-col ${x.tone}`}><b>{x.label}</b><div>{x.body}</div></div>)}
    </div>
  );
  if (b.kind === 'facts') return (
    <div className="dr-facts">
      {b.items.map((f, i) => <div key={i}><b>{f.k}</b><CellView x={f.v} /></div>)}
    </div>
  );
  if (b.kind === 'teams') return (
    <div className="dr-teams-wrap">
      {/* 팀마다 한 줄 — 왼쪽 팀 머리, 오른쪽 이름(같은 폭 칸에 나란히, 근속은 옆에 작게) */}
      <div className="dr-crew">
        {b.teams.map(t => (
          <div key={t.name} className="dr-crew-row">
            <div className="dr-crew-h">
              <div><b>{t.name}</b><span className={`dr-pill sm ${shiftTone(t.shift)}`}>{t.shift}</span></div>
              <span>근무 <b>{t.working}</b> / {t.total}명</span>
            </div>
            <div className="dr-crew-b">
              {t.names.length === 0 ? <div className="dr-names"><span className="dr-t dim">-</span></div> : t.groups.map((g, gi) => (
                // 세정 줄·QA 줄 — 묶음마다 새 줄에서 시작
                <div key={gi} className="dr-names">
                  {g.map(x => (
                    <span key={x.n} className="dr-name">
                      <b>{x.n}</b>
                      {x.p && <em className={/장$/.test(x.p) ? 'lead' : ''}>{x.p}</em>}
                      {x.t && <small>{x.t}</small>}
                    </span>
                  ))}
                </div>
              ))}
              {t.off.length > 0 && <div className="dr-team-sub warn"><b>휴무</b>{t.off.join(', ')}</div>}
              {t.edu.length > 0 && <div className="dr-team-sub info"><b>교육</b>{t.edu.join(', ')}</div>}
            </div>
          </div>
        ))}
      </div>
      <div className="dr-tenure">
        <span className="dr-tenure-t">{b.tenureTitle}</span>
        <div>{b.tenure.map(x => <span key={x.label}><small>{x.label}</small><b>{x.n}<i>명</i></b></span>)}</div>
      </div>
      {b.others.length > 0 && <div className="dr-facts">{b.others.map(o => <div key={o.k}><b>{o.k}</b><span>{o.v}</span></div>)}</div>}
    </div>
  );
  const align = (i: number) => (b.center?.includes(i) ? 'c' : '');
  return (
    <div className="dr-tblock">
      {b.caption && <div className="dr-cap">{b.caption}</div>}
      <div className="dr-tw">
        {/* 칸이 많은 목록 표는 폰에서 줄마다 카드로 푼다 */}
        <table className={`dr-table ${b.stack ? 'stack' : ''} ${b.matrix ? 'matrix' : ''}`}>
          {b.matrix && <colgroup><col className="dr-mcol" />{b.head.slice(1).map((_, i) => <col key={i} />)}</colgroup>}
          <thead><tr>{b.head.map((h, i) => <th key={i} className={align(i)}>{h}</th>)}</tr></thead>
          <tbody>
            {b.rows.map((row, ri) => isGroupRow(row) ? (
              <tr key={ri} className="dr-grp"><td colSpan={b.head.length}>{cellText(row[0])}</td></tr>
            ) : (
              <tr key={ri}>
                {row.map((x, ci) => (
                  <td key={ci} data-label={b.head[ci]} className={`${b.wide?.includes(ci) ? 'wide' : ''} ${align(ci)}`}><CellView x={x} /></td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
