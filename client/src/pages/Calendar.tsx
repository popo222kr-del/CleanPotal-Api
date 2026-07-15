import { useEffect, useState, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import type { CalendarMonth, CalendarDay, TeamEvent } from '../api/types';
import './Calendar.css';

const DOW = ['일', '월', '화', '수', '목', '금', '토'];

type EventForm = { id?: number; startDate: string; endDate: string; content: string; detail: string };

export default function Calendar() {
  const today = new Date();
  const [year, setYear] = useState(today.getFullYear());
  const [month, setMonth] = useState(today.getMonth() + 1);
  const [data, setData] = useState<CalendarMonth | null>(null);
  const [detail, setDetail] = useState<CalendarDay | null>(null);
  const [evForm, setEvForm] = useState<EventForm | null>(null);
  const nav = useNavigate();

  const load = useCallback(async () => {
    setData(await api.get<CalendarMonth>(`/api/schedule/calendar?year=${year}&month=${month}&predict=true`));
  }, [year, month]);
  useEffect(() => { load(); }, [load]);

  // 상세 모달이 열려 있으면 최신 데이터로 동기화
  useEffect(() => {
    if (detail && data) setDetail(data.days.find(d => d.date === detail.date) ?? null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [data]);

  function openAddEvent(date: string) {
    setEvForm({ startDate: date, endDate: date, content: '', detail: '' });
  }
  function openEditEvent(e: TeamEvent) {
    setEvForm({ id: e.id, startDate: e.startDate, endDate: e.endDate, content: e.content, detail: e.detail });
  }
  async function saveEvent(e: React.FormEvent) {
    e.preventDefault();
    if (!evForm || !evForm.content.trim()) return;
    const body = { startDate: evForm.startDate, endDate: evForm.endDate, content: evForm.content.trim(), detail: evForm.detail };
    if (evForm.id) await api.put(`/api/schedule/events/${evForm.id}`, body);
    else await api.post('/api/schedule/events', body);
    setEvForm(null);
    await load();
  }
  async function deleteEvent(id: number) {
    if (!confirm('이 일정을 삭제할까요?')) return;
    await api.del(`/api/schedule/events/${id}`);
    await load();
  }

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
        <div style={{ flex: 1 }}><h2>📅 세정팀 통합 일정 달력</h2></div>
        <button className="btn btn-ghost" onClick={() => nav('/roster')}>🗓️ 근무표 생성</button>
      </header>
      <div className="cal-nav">
        <button className="cal-btn" onClick={prev}>◀</button>
        <span className="cal-title">{year}년 {month}월</span>
        <button className="cal-btn" onClick={next}>▶</button>
        <button className="cal-btn today" onClick={goToday}>오늘</button>
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
            <section>
              <div className="cal-d-evhead">
                <h4>팀 일정</h4>
                <button className="btn btn-sm" onClick={() => openAddEvent(detail.date)}>＋ 일정 등록</button>
              </div>
              {detail.events.length > 0
                ? detail.events.map(e => (
                    <div key={e.id} className="cal-d-event">
                      <div className="cal-d-evinfo">
                        <b>{e.content}</b>{e.detail && <span> — {e.detail}</span>}
                        <i> ({e.registeredBy}{e.startDate !== e.endDate ? `, ${e.startDate}~${e.endDate}` : ''})</i>
                      </div>
                      <div className="cal-d-evact">
                        <button className="link-btn" onClick={() => openEditEvent(e)}>수정</button>
                        <button className="link-btn danger" onClick={() => deleteEvent(e.id)}>삭제</button>
                      </div>
                    </div>
                  ))
                : <p className="cal-d-empty">등록된 팀 일정이 없습니다.</p>}
            </section>
            <section><h4>주간 근무 ({detail.dayShift.length})</h4><p>{detail.dayShift.join(', ') || '-'}</p></section>
            <section><h4>야간 근무 ({detail.nightShift.length})</h4><p>{detail.nightShift.join(', ') || '-'}</p></section>
            {detail.offShift.length > 0 && <section><h4>휴무/연차/교육</h4><p>{detail.offShift.join(', ')}</p></section>}
            <div className="modal-actions"><button className="btn btn-ghost" onClick={() => setDetail(null)}>닫기</button></div>
          </div>
        </div>
      )}

      {evForm && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setEvForm(null); }}>
          <form className="modal-box cal-evform" onSubmit={saveEvent}>
            <h3>{evForm.id ? '팀 일정 수정' : '팀 일정 등록'}</h3>
            <label>내용 *<input autoFocus value={evForm.content} onChange={e => setEvForm(f => f && { ...f, content: e.target.value })} placeholder="예: 정기 안전교육" /></label>
            <label>세부내용<textarea rows={2} value={evForm.detail} onChange={e => setEvForm(f => f && { ...f, detail: e.target.value })} /></label>
            <div className="cal-evdates">
              <label>시작일<input type="date" value={evForm.startDate} onChange={e => setEvForm(f => f && { ...f, startDate: e.target.value })} /></label>
              <label>종료일<input type="date" value={evForm.endDate} min={evForm.startDate} onChange={e => setEvForm(f => f && { ...f, endDate: e.target.value })} /></label>
            </div>
            <div className="modal-actions">
              <button type="button" className="btn btn-ghost" onClick={() => setEvForm(null)}>취소</button>
              <button type="submit" className="btn btn-primary">{evForm.id ? '수정' : '등록'}</button>
            </div>
          </form>
        </div>
      )}
    </div>
  );
}
