import { useEffect, useState } from 'react';
import { api } from '../../api/client';
import './Work.css';
import type { BakeLog, WorkReport as Report } from '../../api/types';
import { contentTone, sortByLine } from './common';

// 업무보고(2. 세정/BAKE) — 설비·공정 목록에 그날 약액 교체 기록을 붙여 자동으로 만든다.
// 엑셀에서는 매일 손으로 옮겨 적던 '약액교체현황' 칸이다. 교체가 없는 설비는 마지막 교체일을 흐리게 보여 준다.
// BAKE 오븐은 약액이 없어 그날 'BAKE 그을음 기록' 을 붙인다(교대·회차별 가동/HOLD·품명·그을음 이상).

const DOW = ['일', '월', '화', '수', '목', '금', '토'];
function todayYmd() {
  const t = new Date(); const p = (n: number) => String(n).padStart(2, '0');
  return `${t.getFullYear()}-${p(t.getMonth() + 1)}-${p(t.getDate())}`;
}
function addDays(s: string, n: number) {
  const d = new Date(s + 'T00:00:00'); d.setDate(d.getDate() + n);
  const p = (x: number) => String(x).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}
const md = (s: string) => `${Number(s.slice(5, 7))}/${Number(s.slice(8, 10))}`;
const daysBetween = (a: string, b: string) => Math.round((new Date(b + 'T00:00:00').getTime() - new Date(a + 'T00:00:00').getTime()) / 86400000);

export default function WorkReport() {
  const [date, setDate] = useState(todayYmd());
  const [data, setData] = useState<Report | null>(null);
  const [onlyChanged, setOnlyChanged] = useState(false);

  useEffect(() => {
    let alive = true;
    setData(null);
    api.get<Report>(`/api/worklog/report?date=${date}`).then(r => { if (alive) setData(r); }).catch(() => {});
    return () => { alive = false; };
  }, [date]);

  // BAKE 오븐별 그날 기록
  const bakeBy = new Map<string, BakeLog[]>();
  for (const b of data?.bake ?? []) bakeBy.set(b.eqCode, [...(bakeBy.get(b.eqCode) ?? []), b]);
  const bakeRuns = (data?.bake ?? []).filter(b => !b.status).length;
  const bakeIssues = (data?.bake ?? []).filter(b => b.hasSoot || b.hasQuartz).length;
  // 서버는 설비 목록 순서로 준다 — 라인별로 모아서(나중에 추가된 설비도 제 라인에) 보여 준다.
  // '교체·가동한 설비만' — 세정은 약액 교체, BAKE 는 그날 가동한 오븐
  const rows = sortByLine((data?.rows ?? []).filter(r => !onlyChanged
    || (r.kind === 'BAKE' ? (bakeBy.get(r.code) ?? []).some(b => !b.status) : !!r.content)));
  const groups: { line: string; rows: typeof rows }[] = [];
  for (const r of rows) {
    const g = groups[groups.length - 1];
    if (g && g.line === r.line) g.rows.push(r); else groups.push({ line: r.line, rows: [r] });
  }
  const d = new Date(date + 'T00:00:00');

  return (
    <div className="wf-page">
      <header className="pg-header">
        <div>
          <h2>업무보고 · 세정/BAKE</h2>
          <p>설비·공정과 그날 약액 교체 현황(약액 교체 기록에서 자동으로 채워짐)</p>
        </div>
      </header>
      <div className="pg-body">
        <div className="wf-toolbar">
          <div className="wf-monthnav">
            <button onClick={() => setDate(addDays(date, -1))} aria-label="전날">‹</button>
            <input className="input wf-date" type="date" value={date} onChange={e => e.target.value && setDate(e.target.value)} />
            <button onClick={() => setDate(addDays(date, 1))} aria-label="다음날">›</button>
          </div>
          <b className="wf-daylabel">{d.getMonth() + 1}월 {d.getDate()}일 ({DOW[d.getDay()]})</b>
          {date !== todayYmd() && <button className="btn btn-ghost wf-sm" onClick={() => setDate(todayYmd())}>오늘</button>}
          <span className="wf-count">약액 교체 <b>{data?.changedCount ?? 0}</b>대</span>
          <span className="wf-count">BAKE 가동 <b>{bakeRuns}</b>회{bakeIssues > 0 && <> · 이상 <b className="wf-bad">{bakeIssues}</b></>}</span>
          <label className="wf-chk"><input type="checkbox" checked={onlyChanged} onChange={e => setOnlyChanged(e.target.checked)} /> 교체·가동한 설비만</label>
        </div>

        {!data ? <div className="wf-empty">불러오는 중…</div> : (
          <div className="wf-report">
            {groups.length === 0 && <div className="wf-empty">이날 약액 교체·BAKE 가동이 없습니다.</div>}
            {groups.map(g => (
              <section key={g.line} className="wf-rsec">
                <h3>{g.line} <small>{g.rows.length}대</small></h3>
                <div className="wf-rlist">
                  {g.rows.map(r => (
                    <div key={r.code} className={`wf-rrow ${r.content ? 'changed' : ''} ${r.kind === 'BAKE' ? 'bake' : ''}`}>
                      <span className="wf-rcode">{r.code}</span>
                      <span className="wf-rproc">{r.process || '-'}</span>
                      <span className="wf-rstat">
                        {r.kind === 'BAKE'
                          ? <BakeCell logs={bakeBy.get(r.code) ?? []} />
                          : r.content
                          ? <span className={`wf-chip ${contentTone(r.content)}`}>{r.content}</span>
                            : r.lastChangeDate
                              ? <span className="wf-dim">마지막 {md(r.lastChangeDate)} · {daysBetween(r.lastChangeDate, date)}일 전</span>
                              : <span className="wf-dim">-</span>}
                        {r.note && <em className="wf-rnote" title={r.note}>{r.note.split('\n').join(' · ')}</em>}
                      </span>
                    </div>
                  ))}
                </div>
              </section>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

const roundMark = (n: number) => '①②③④⑤⑥⑦⑧⑨'[n - 1] ?? String(n);
const hm = (at: string | null) => (at ? at.slice(11, 16) : '?');

/** BAKE 오븐 한 줄 — 그날 교대·회차별 기록. 가동은 시각·품명, 비가동·HOLD 는 상태만, 그을음·Q'TZ 가루는 빨갛게. */
function BakeCell({ logs }: { logs: BakeLog[] }) {
  if (logs.length === 0) return <span className="wf-dim">그을음 기록 없음</span>;
  const multi = logs.some(l => l.round > 1);
  return (
    <span className="wf-rbake">
      {logs.map((l, i) => (
        <span key={i} className={`wf-rb ${l.status ? 'idle' : ''}`}
          title={l.status ? `${l.shift} ${l.status}` : `${l.shift} ${hm(l.trackIn)}→${hm(l.trackOut)} ${l.item} ${l.serialNo}\n그을음 ${l.soot} · Q'TZ ${l.quartz}${l.note ? ` · ${l.note}` : ''}`}>
          <b>{l.shift}{multi ? roundMark(l.round) : ''}</b>
          {l.status || <>{hm(l.trackIn)}→{hm(l.trackOut)} {l.item}</>}
          {l.hasSoot && <em>그을음</em>}
          {l.hasQuartz && <em>Q'TZ 有</em>}
        </span>
      ))}
    </span>
  );
}
