import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../../api/client';
import type { CheckShiftStatus, CheckStatus } from '../../api/types';
import { dayLabel, timeLabel, ymdOf } from './common';

// 구역 × 주간/야간 점검 현황. 칸을 누르면 그 구역 점검 화면(QR 없이)으로 들어간다.

const STATE_LABEL: Record<string, string> = { none: '미점검', progress: '진행 중', submitted: '제출', na: '해당 없음' };

function shiftDate(ymd: string, days: number) {
  const d = new Date(`${ymd}T00:00:00`);
  d.setDate(d.getDate() + days);
  return ymdOf(d);
}

export default function StatusTab({ onOpenNg }: { onOpenNg: () => void }) {
  const nav = useNavigate();
  const [date, setDate] = useState('');
  const [status, setStatus] = useState<CheckStatus | null>(null);

  const load = useCallback(async () => {
    try { setStatus(await api.get<CheckStatus>(`/api/checklist/status${date ? `?date=${date}` : ''}`)); }
    catch (e) { alert(e instanceof Error ? e.message : '현황을 불러오지 못했습니다.'); }
  }, [date]);
  useEffect(() => { load(); }, [load]);

  if (!status) return <div className="ck-empty">불러오는 중…</div>;
  const s = status;
  const isToday = s.workDate === s.currentWorkDate;
  const zones = s.lines.flatMap(l => l.zones);

  // 요약: 교대별 제출 구역 수, 지금 교대 항목 진행률, 주 1회 밀림
  const count = (shift: 'day' | 'night') => {
    const target = zones.filter(z => z[shift].state !== 'na');
    return { done: target.filter(z => z[shift].state === 'submitted').length, total: target.length };
  };
  const day = count('day'), night = count('night');
  const cur = s.currentShift === '주간' ? 'day' : 'night';
  const items = zones.reduce((a, z) => ({ done: a.done + z[cur].done, total: a.total + z[cur].total }), { done: 0, total: 0 });
  const overdue = zones.reduce((n, z) => n + z.weeklyOverdue, 0);
  const dueToday = zones.reduce((n, z) => n + z.weeklyDue, 0);

  function open(code: string, shift: string) {
    nav(`/c/${encodeURIComponent(code)}?date=${s.workDate}&shift=${encodeURIComponent(shift)}&from=hub`);
  }

  // 아직 시작하지 않은 교대(오늘 주간 중의 야간, 내일 이후) — "미점검" 대신 "시작 전"으로 보인다.
  const isFuture = (shift: string) =>
    s.workDate > s.currentWorkDate || (s.workDate === s.currentWorkDate && s.currentShift === '주간' && shift === '야간');

  function Cell({ code, shift, st }: { code: string; shift: string; st: CheckShiftStatus }) {
    if (st.state === 'na') return <span className="ck-muted">—</span>;
    const pct = st.total ? Math.round((st.done / st.total) * 100) : 0;
    const future = st.state === 'none' && isFuture(shift);
    return (
      <button className={`ck-sc ${future ? 'future' : st.state}`} onClick={() => open(code, shift)}
        title={future ? '아직 시작하지 않은 교대입니다(보기만 가능)' : '눌러서 점검 화면 열기'}>
        <span className="ck-sc-state"><i />{future ? '시작 전' : STATE_LABEL[st.state]}</span>
        <span className="ck-sc-bar"><i style={{ width: `${pct}%` }} /></span>
        <span className="ck-sc-num">{st.done}/{st.total}</span>
        {st.ng > 0 && <span className="ck-pill bad">NG {st.ng}</span>}
        <span className="ck-sc-who">{st.submittedAt ? `${st.submittedByName} · ${timeLabel(st.submittedAt).split(' ')[1]}` : ''}</span>
      </button>
    );
  }

  return (
    <div>
      <div className="ck-toolbar">
        <div className="ck-datenav">
          <button onClick={() => setDate(shiftDate(s.workDate, -1))} aria-label="전날">‹</button>
          <input type="date" value={s.workDate} onChange={e => setDate(e.target.value)} />
          <button onClick={() => setDate(shiftDate(s.workDate, 1))} aria-label="다음날">›</button>
        </div>
        {!isToday && <button className="ck-link" onClick={() => setDate('')}>오늘로</button>}
        <span className="ck-now">
          지금 <b>{dayLabel(s.currentWorkDate)}</b>
          <span className={`ck-shift ${s.currentShift === '주간' ? 'day' : 'night'}`}>{s.currentShift}</span>
        </span>
        <button className="ck-iconbtn" onClick={load} title="새로고침">↻</button>
      </div>

      <div className="ck-kpis">
        <div className="ck-kpi">
          <span>주간 제출</span>
          <b>{day.done}<small>/{day.total} 구역</small></b>
        </div>
        <div className="ck-kpi">
          <span>야간 제출</span>
          <b>{night.done}<small>/{night.total} 구역</small></b>
        </div>
        <div className="ck-kpi">
          <span>{isToday ? `지금 교대(${s.currentShift}) 진행` : `${s.currentShift} 항목 진행`}</span>
          <b>{items.total ? Math.round((items.done / items.total) * 100) : 0}<small>% · {items.done}/{items.total}</small></b>
        </div>
        <div className={`ck-kpi ${overdue ? 'warn' : ''}`}>
          <span>주 1회</span>
          <b>{overdue}<small> 밀림 · 오늘 {dueToday}</small></b>
        </div>
        <button className={`ck-kpi link ${s.openNg ? 'bad' : ''}`} onClick={onOpenNg}>
          <span>미조치 NG</span>
          <b>{s.openNg}<small>건</small></b>
        </button>
      </div>

      {s.lines.length === 0 && <div className="ck-empty">등록된 구역이 없습니다. 양식 관리에서 구역·항목을 넣어 주세요.</div>}
      {s.lines.map(l => (
        <section key={l.line} className="ck-panel">
          <div className="ck-panel-head">
            <b>{l.line}</b>
            <span className="ck-muted">{l.zones.length}개 구역</span>
          </div>
          <table className="ck-table ck-status">
            <colgroup><col className="c-zone" /><col /><col /><col className="c-week" /></colgroup>
            <thead>
              <tr>
                <th>구역</th>
                <th className={isToday && s.currentShift === '주간' ? 'now' : ''}>주간{isToday && s.currentShift === '주간' && <em>지금</em>}</th>
                <th className={isToday && s.currentShift === '야간' ? 'now' : ''}>야간{isToday && s.currentShift === '야간' && <em>지금</em>}</th>
                <th>주 1회</th>
              </tr>
            </thead>
            <tbody>
              {l.zones.map(z => (
                <tr key={z.code}>
                  <td><span className="ck-zname">{z.name}</span><span className="ck-zcode">{z.code}</span></td>
                  <td><Cell code={z.code} shift="주간" st={z.day} /></td>
                  <td><Cell code={z.code} shift="야간" st={z.night} /></td>
                  <td>
                    {z.weeklyOverdue > 0 && <span className="ck-pill bad">밀림 {z.weeklyOverdue}</span>}
                    {z.weeklyDue > 0 && <span className="ck-pill warn">오늘 {z.weeklyDue}</span>}
                    {z.weeklyDue === 0 && z.weeklyOverdue === 0 && <span className="ck-muted">—</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      ))}
      <p className="ck-foot">칸을 누르면 그 구역 점검 화면이 열립니다(QR 없이 들어온 것으로 기록). 관리자가 아니면 지금 교대와 바로 앞 교대만 입력할 수 있고, 시작 전 교대는 관리자도 입력할 수 없습니다.</p>
    </div>
  );
}
