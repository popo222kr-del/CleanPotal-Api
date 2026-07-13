import { useEffect, useState, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import type { CalendarMonth, CalendarDay } from '../api/types';
import './Calendar.css';

const DOW = ['일', '월', '화', '수', '목', '금', '토'];

export default function Calendar() {
  const today = new Date();
  const [year, setYear] = useState(today.getFullYear());
  const [month, setMonth] = useState(today.getMonth() + 1);
  const [predict, setPredict] = useState(true);
  const [data, setData] = useState<CalendarMonth | null>(null);
  const [detail, setDetail] = useState<CalendarDay | null>(null);
  const nav = useNavigate();

  const load = useCallback(async () => {
    setData(await api.get<CalendarMonth>(`/api/schedule/calendar?year=${year}&month=${month}&predict=${predict}`));
  }, [year, month, predict]);
  useEffect(() => { load(); }, [load]);

  function prev() { if (month === 1) { setYear(y => y - 1); setMonth(12); } else setMonth(m => m - 1); }
  function next() { if (month === 12) { setYear(y => y + 1); setMonth(1); } else setMonth(m => m + 1); }
  function goToday() { setYear(today.getFullYear()); setMonth(today.getMonth() + 1); }

  // 6주(42칸) 고정 그리드 — 앞뒤 빈칸으로 채워 WPF처럼 꽉 찬 달력
  const firstDow = new Date(year, month - 1, 1).getDay();
  const cells: (CalendarDay | null)[] = [];
  if (data) {
    for (let i = 0; i < firstDow; i++) cells.push(null);
    data.days.forEach(d => cells.push(d));
    while (cells.length % 7 !== 0) cells.push(null);
  }
  const isThisMonth = year === today.getFullYear() && month === today.getMonth() + 1;
  const todayDay = today.getDate();

  return (
    <div className="cal-page">
      <header className="pg-header">
        <div style={{ flex: 1 }}><h2>📅 세정팀 통합 일정 달력</h2><p>근무조 교대·연차·교육 등 부서 전체 일정을 한눈에</p></div>
        <button className="btn btn-ghost" onClick={() => nav('/roster')}>🗓️ 근무표 생성</button>
      </header>
      <div className="cal-nav">
        <button className="cal-btn" onClick={prev}>◀</button>
        <span className="cal-title">{year}년 {month}월</span>
        <button className="cal-btn" onClick={next}>▶</button>
        <button className="cal-btn today" onClick={goToday}>오늘</button>
        <button className={`cal-toggle ${predict ? 'on' : ''}`} onClick={() => setPredict(p => !p)}>
          🔄 자동 교대 {predict ? 'ON' : 'OFF'}
        </button>
      </div>

      <div className="cal-dow">
        {DOW.map((d, i) => <div key={d} className={`cal-h ${i === 0 ? 'sun' : i === 6 ? 'sat' : ''}`}>{d}</div>)}
      </div>

      <div className="cal-body">
        {cells.map((c, i) => {
          if (!c) return <div key={i} className="cal-cell empty" />;
          const dow = i % 7;
          const isToday = isThisMonth && c.day === todayDay;
          return (
            <div key={i} className={`cal-cell ${isToday ? 'today' : ''}`} onClick={() => setDetail(c)}>
              <div className="cal-cell-head">
                <span className={`cal-day ${isToday ? 'today-num' : dow === 0 || c.holiday ? 'sun' : dow === 6 ? 'sat' : ''}`}>{c.day}</span>
                {c.holiday && <span className="cal-hchip">{c.holiday}</span>}
                {!c.holiday && c.events[0] && <span className="cal-echip" title={c.events.map(e => e.content).join(', ')}>{c.events[0].content}{c.events.length > 1 ? ` 외 ${c.events.length - 1}` : ''}</span>}
              </div>
              <div className="cal-badges">
                {c.badges.map((b, bi) => (
                  <span key={bi} className={`cal-b k-${b.kind}`} title={b.names.join(', ')}>{b.text}</span>
                ))}
              </div>
            </div>
          );
        })}
      </div>

      {detail && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setDetail(null); }}>
          <div className="modal-box cal-detail">
            <h3>{detail.date} ({detail.dayOfWeek}) {detail.holiday && <span className="cal-hol-name">{detail.holiday}</span>}</h3>
            {detail.events.length > 0 && (
              <section><h4>팀 일정</h4>
                {detail.events.map(e => <div key={e.id} className="cal-d-event"><b>{e.content}</b>{e.detail && <span> — {e.detail}</span>}<i> ({e.registeredBy})</i></div>)}
              </section>
            )}
            <section><h4>주간 근무 ({detail.dayShift.length})</h4><p>{detail.dayShift.join(', ') || '-'}</p></section>
            <section><h4>야간 근무 ({detail.nightShift.length})</h4><p>{detail.nightShift.join(', ') || '-'}</p></section>
            {detail.offShift.length > 0 && <section><h4>휴무/연차/교육</h4><p>{detail.offShift.join(', ')}</p></section>}
            <div className="modal-actions"><button className="btn btn-ghost" onClick={() => setDetail(null)}>닫기</button></div>
          </div>
        </div>
      )}
    </div>
  );
}
