import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../../api/client';
import './Work.css';
import type { DailyReport } from '../../api/types';
import { addDays, buildDaily, cellText, cellTone, dow, mailHtml, mailText, md, todayYmd, type Block } from './dailyModel';
import { copyRich } from './icpmsCopy';

// 데일리 업무보고 — 각 메뉴에 적힌 기록(체크시트·인수인계·스케줄 보드·약액·KOH·BAKE 등)을 하루치로 모은다.
// 하루 = 그날 주간 + 그날 야간(다음 날 아침까지). 여기서 새로 입력하는 것은 없고, 고칠 때는 섹션 제목을 눌러 원래 메뉴로 간다.
// '메일로 복사' 는 표 모양 그대로(서식 있는 복사) 메일 본문에 붙게 만든다.

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
    if (!data || !model) return;
    const ok = await copyRich(mailHtml(data, model), mailText(data, model));
    setMsg(ok ? '복사했습니다 — 메일 본문에 붙여넣기(Ctrl+V) 하세요.' : '복사하지 못했습니다. 브라우저 권한을 확인하세요.');
    window.setTimeout(() => setMsg(''), 4000);
  }

  const jump = (key: string) => document.getElementById(`dr-${key}`)?.scrollIntoView({ behavior: 'smooth', block: 'start' });

  return (
    <div className="wf-page">
      <header className="pg-header">
        <div>
          <h2>업무보고</h2>
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
            <div className="dr-alerts">
              {model.alerts.length === 0
                ? <span className="dr-alert ok">특이 이상 없음</span>
                : model.alerts.map((a, i) => (
                  <button key={i} className={`dr-alert ${a.tone}`} onClick={() => jump(a.key)}>{a.t}</button>
                ))}
            </div>
            <nav className="dr-toc">
              {model.sections.map((s, i) => (
                <button key={s.key} onClick={() => jump(s.key)}>{i + 1}. {s.title.replace(/ \(.*\)$/, '')}</button>
              ))}
            </nav>

            {model.sections.map((s, i) => (
              <section key={s.key} id={`dr-${s.key}`} className="dr-sec">
                <div className="dr-head">
                  <h3>{i + 1}. {s.title}</h3>
                  {s.badge && <span className={`dr-badge ${s.badge.tone}`}>{s.badge.t}</span>}
                  {s.link && <button className="dr-open" onClick={() => nav(s.link!)}>열기 ›</button>}
                </div>
                {s.blocks.map((b, j) => <BlockView key={j} b={b} />)}
              </section>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

function BlockView({ b }: { b: Block }) {
  if (b.kind === 'note') return <p className="dr-note">{b.text}</p>;
  if (b.kind === 'text') return (
    <div className="dr-text"><b>{b.label}</b><div>{b.body}</div></div>
  );
  if (b.kind === 'facts') return (
    <div className="dr-facts">
      {b.items.map((f, i) => <span key={i}><b>{f.k}</b> <span className={`dr-t ${cellTone(f.v)}`}>{cellText(f.v)}</span></span>)}
    </div>
  );
  return (
    <div className="dr-tblock">
      {b.caption && <div className="dr-cap">{b.caption}</div>}
      <div className="dr-tw">
        {/* 칸이 많은 표는 폰에서 줄마다 카드로 푼다(좁은 표는 표 그대로가 짧다) */}
        <table className={`dr-table ${b.head.length > 4 ? 'stack' : ''}`}>
          <thead><tr>{b.head.map((h, i) => <th key={i} className={b.wide?.includes(i) ? 'wide' : ''}>{h}</th>)}</tr></thead>
          <tbody>
            {b.rows.map((row, ri) => (
              <tr key={ri}>
                {row.map((x, ci) => (
                  <td key={ci} data-label={b.head[ci]} className={`${b.wide?.includes(ci) ? 'wide' : ''} dr-t ${cellTone(x)}`}>{cellText(x) || ' '}</td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
