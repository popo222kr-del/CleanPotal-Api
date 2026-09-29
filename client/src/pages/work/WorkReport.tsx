import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../../api/client';
import './Work.css';
import type { DailyReport } from '../../api/types';
import { addDays, buildDaily, cellText, cellTone, dow, mailHtml, mailText, md, todayYmd, type Block, type Section } from './dailyModel';
import { copyRich } from './icpmsCopy';

// Daily 업무 보고 — 각 메뉴에 적힌 기록(근무·인수인계·체크시트·스케줄 보드·세정 현황·약액·KOH·BAKE)을 하루치로 모은다.
// 하루 = 그날 주간 + 그날 야간(다음 날 아침까지). 여기서 새로 입력하는 것은 없고, 고칠 때는 섹션의 '열기' 로 원래 메뉴로 간다.
// 그래프 대신 표로 촘촘하게 — 짧은 섹션 둘은 나란히 놓는다. '메일로 복사' 는 같은 배치 그대로 메일 본문에 붙는다.

export default function WorkReport() {
  const nav = useNavigate();
  const [date, setDate] = useState(todayYmd());
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
  }, [date]);

  const model = useMemo(() => (data ? buildDaily(data, todayYmd()) : null), [data]);

  async function copyMail() {
    if (!model) return;
    const ok = await copyRich(mailHtml(model), mailText(model));
    setMsg(ok ? '복사했습니다 — 메일 본문에 붙여넣기(Ctrl+V) 하세요.' : '복사하지 못했습니다. 브라우저 권한을 확인하세요.');
    window.setTimeout(() => setMsg(''), 4000);
  }

  const jump = (key: string) => document.getElementById(`dr-${key}`)?.scrollIntoView({ behavior: 'smooth', block: 'start' });

  return (
    <div className="wf-page">
      <header className="pg-header">
        <div>
          <h2>Daily 업무 보고</h2>
          <p>{md(date)} 주간 + 야간({md(addDays(date, 1))} 아침까지) · 각 메뉴 기록을 모아 자동으로 만듭니다</p>
        </div>
      </header>
      <div className="pg-body">
        <div className="wf-toolbar">
          <div className="wf-monthnav">
            <button onClick={() => setDate(addDays(date, -1))} aria-label="전날">‹</button>
            <input className="input wf-date" type="date" value={date} onChange={e => e.target.value && setDate(e.target.value)} />
            <button onClick={() => setDate(addDays(date, 1))} aria-label="다음날">›</button>
          </div>
          <b className="wf-daylabel">{Number(date.slice(5, 7))}월 {Number(date.slice(8, 10))}일 ({dow(date)})</b>
          {date !== todayYmd() && <button className="btn btn-ghost wf-sm" onClick={() => setDate(todayYmd())}>오늘</button>}
          <button className="btn btn-primary dr-copy" onClick={copyMail} disabled={!model}>메일로 복사</button>
          {msg && <span className="dr-msg">{msg}</span>}
        </div>

        {err ? <div className="wf-empty">{err}</div> : !model ? <div className="wf-empty">불러오는 중…</div> : (
          <div className="dr-wrap">
            <div className="dr-top">
              <table className="dr-headline">
                <tbody><tr>{model.head.map(h => <td key={h.k}><span>{h.k}</span><b>{h.v}</b></td>)}</tr></tbody>
              </table>
              <div className="dr-alerts">
                {model.alerts.length === 0
                  ? <span className="dr-alert ok">특이 이상 없음</span>
                  : model.alerts.map((a, i) => <button key={i} className={`dr-alert ${a.tone}`} onClick={() => jump(a.key)}>{a.t}</button>)}
              </div>
            </div>

            <div className="dr-grid">
              {model.sections.map(s => <SectionView key={s.key} s={s} onOpen={() => s.link && nav(s.link)} />)}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

function SectionView({ s, onOpen }: { s: Section; onOpen: () => void }) {
  return (
    <section id={`dr-${s.key}`} className={`dr-sec ${s.half ? 'half' : ''}`}>
      <div className="dr-head">
        <h3>{s.title}</h3>
        {s.badge && <span className={`dr-badge ${s.badge.tone}`}>{s.badge.t}</span>}
        {s.link && <button className="dr-open" onClick={onOpen}>열기 ›</button>}
      </div>
      {s.blocks.map((b, j) => <BlockView key={j} b={b} />)}
    </section>
  );
}

function BlockView({ b }: { b: Block }) {
  if (b.kind === 'note') return <p className="dr-note">{b.text}</p>;
  if (b.kind === 'text') return <div className="dr-text"><b>{b.label}</b><div>{b.body}</div></div>;
  if (b.kind === 'cols') return (
    <div className="dr-cols">
      {b.cols.map((x, i) => <div key={i}><b>{x.label}</b><div>{x.body}</div></div>)}
    </div>
  );
  if (b.kind === 'facts') return (
    <div className="dr-facts">
      {b.items.map((f, i) => <span key={i}><b>{f.k}</b> <span className={`dr-t ${cellTone(f.v)}`}>{cellText(f.v)}</span></span>)}
    </div>
  );
  if (b.kind === 'chips') return (
    <div className="dr-chips">
      {b.items.map((x, i) => <span key={i} className={`dr-chip ${x.tone}`}><b>{x.t}</b>{x.sub}</span>)}
    </div>
  );
  return (
    <div className="dr-tblock">
      {b.caption && <div className="dr-cap">{b.caption}</div>}
      <div className="dr-tw">
        <table className={`dr-table ${b.stack ? 'stack' : ''} ${b.fill ? 'fill' : ''}`}>
          <thead>
            {b.groups && <tr className="dr-groups">{b.groups.map((g, i) => <th key={i} colSpan={g.span}>{g.t}</th>)}</tr>}
            <tr>{b.head.map((h, i) => <th key={i}>{h}</th>)}</tr>
          </thead>
          <tbody>
            {b.rows.map((row, ri) => (
              <tr key={ri}>
                {row.map((x, ci) => (
                  <td key={ci} data-label={b.head[ci]}
                    className={`${b.wide?.includes(ci) ? 'wide' : ''} ${b.rowHead && ci === 0 ? 'rh' : ''} dr-t ${cellTone(x)}`}>
                    {cellText(x) || ' '}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
