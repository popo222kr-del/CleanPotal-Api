import { useEffect, useState, useCallback } from 'react';
import { useAccess } from '../auth/useAccess';
import { useAuth } from '../auth/AuthContext';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import type { CalendarMonth, CalendarDay, TeamEvent } from '../api/types';
import './Calendar.css';

const DOW = ['일', '월', '화', '수', '목', '금', '토'];

type EventForm = { id?: number; startDate: string; endDate: string; content: string; detail: string };

// ── WPF 일정 등록 창(근태/휴가 + 팀 일정) 이식 ──
const SHIFT_TYPES = ['연차', '오전반차', '오후반차', '반반차', '휴무', '특근'];
const HALF_TIMES = ['08:30~10:30', '10:30~12:30', '13:30~15:30', '15:30~17:30'];
type Member = { realName: string; teamName: string };

function ymd(d: Date) {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

export default function Calendar() {
  const { canEditSchedule: canEdit } = useAccess();
  const { user } = useAuth();
  const today = new Date();
  const [year, setYear] = useState(today.getFullYear());
  const [month, setMonth] = useState(today.getMonth() + 1);
  const [data, setData] = useState<CalendarMonth | null>(null);
  const [detail, setDetail] = useState<CalendarDay | null>(null);
  const [evForm, setEvForm] = useState<EventForm | null>(null);
  const nav = useNavigate();

  // ── 일정 등록 모달 (WPF ScheduleRegisterWindow) ──
  const [regOpen, setRegOpen] = useState(false);
  const [regTab, setRegTab] = useState<'att' | 'event'>('att');
  const [members, setMembers] = useState<Member[]>([]);
  const [holidays, setHolidays] = useState<Set<string>>(new Set());
  const [holidayReady, setHolidayReady] = useState(false);
  const [att, setAtt] = useState({ member: '', start: ymd(new Date()), end: ymd(new Date()), type: '연차', halfTime: HALF_TIMES[0] });
  const [tev, setTev] = useState({ start: ymd(new Date()), end: ymd(new Date()), content: '', detail: '' });
  const [regBusy, setRegBusy] = useState(false);

  async function openRegister() {
    if (!canEdit) return;
    const base = ymd(isThisMonth ? today : new Date(year, month - 1, 1));
    setAtt({ member: user?.realName ?? '', start: base, end: base, type: '연차', halfTime: HALF_TIMES[0] });
    setTev({ start: base, end: base, content: '', detail: '' });
    setRegTab('att');
    setRegOpen(true);
    try {
      const [ms, h1, h2] = await Promise.all([
        api.get<Member[]>('/api/schedule/members'),
        api.get<string[]>(`/api/schedule/holidays?year=${today.getFullYear()}`),
        api.get<string[]>(`/api/schedule/holidays?year=${today.getFullYear() + 1}`),
      ]);
      setMembers(ms);
      setHolidays(new Set([...h1, ...h2]));
      setHolidayReady(true);
    } catch { setHolidayReady(true); }
  }

  // 미리보기: 선택 N일 → 반영 M일 (주말/공휴일 제외) — WPF CountBusinessDays
  function attPreview() {
    const s = new Date(att.start + 'T00:00:00'), e = new Date(att.end + 'T00:00:00');
    if (s > e) return '시작일이 종료일보다 늦습니다.';
    let total = 0, business = 0;
    for (const d = new Date(s); d <= e; d.setDate(d.getDate() + 1)) {
      total++;
      const dow = d.getDay();
      if (dow !== 0 && dow !== 6 && !holidays.has(ymd(d))) business++;
    }
    const name = att.member || '-';
    return holidayReady
      ? `대상: ${name} | 선택 ${total}일 → 반영 ${business}일 (주말/공휴일 제외)`
      : `대상: ${name} | 선택 ${total}일 (공휴일 계산 대기중)`;
  }

  async function saveRegister() {
    if (!canEdit || regBusy) return;
    setRegBusy(true);
    try {
      if (regTab === 'att') {
        if (!att.member) { alert('직원을 선택해주세요.'); return; }
        if (members.length > 0 && !members.some(m => m.realName === att.member)) {
          alert('직원 이름을 목록에서 선택해주세요.\n입력된 이름이 직원 목록에 없습니다.');
          return;
        }
        const shiftType = att.type === '반반차' ? `반반차 (${att.halfTime})` : att.type;
        const res = await api.post<{ count: number }>('/api/schedule/attendance', {
          memberName: att.member, startDate: att.start, endDate: att.end, shiftType,
        });
        alert(`주말/공휴일을 제외하고 총 ${res.count}일의 일정이 등록되었습니다.`);
      } else {
        if (!tev.content.trim()) { alert('일정 내용을 입력해주세요.'); return; }
        if (tev.start > tev.end) { alert('시작일이 종료일보다 늦을 수 없습니다.'); return; }
        await api.post('/api/schedule/events', {
          startDate: tev.start, endDate: tev.end, content: tev.content.trim(), detail: tev.detail,
        });
        alert('팀 일정이 등록되었습니다.');
      }
      setRegOpen(false);
      await load();
    } catch (e) {
      alert(e instanceof Error ? e.message : '등록 중 오류가 발생했습니다.');
    } finally {
      setRegBusy(false);
    }
  }

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
    if (!canEdit) return;
    setEvForm({ startDate: date, endDate: date, content: '', detail: '' });
  }
  function openEditEvent(e: TeamEvent) {
    if (!canEdit) return;
    setEvForm({ id: e.id, startDate: e.startDate, endDate: e.endDate, content: e.content, detail: e.detail });
  }
  async function saveEvent(e: React.FormEvent) {
    if (!canEdit) return;
    e.preventDefault();
    if (!evForm || !evForm.content.trim()) return;
    const body = { startDate: evForm.startDate, endDate: evForm.endDate, content: evForm.content.trim(), detail: evForm.detail };
    if (evForm.id) await api.put(`/api/schedule/events/${evForm.id}`, body);
    else await api.post('/api/schedule/events', body);
    setEvForm(null);
    await load();
  }
  async function deleteEvent(id: number) {
    if (!canEdit) return;
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
        <div style={{ flex: 1 }}><h2>세정팀 통합 일정 달력</h2></div>
        <button className="btn btn-ghost" onClick={() => nav('/roster')}>생산 근무표</button>
        {canEdit && <button className="btn btn-primary" onClick={openRegister}>+ 일정 등록</button>}
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
                {canEdit && <button className="btn btn-sm" onClick={() => openAddEvent(detail.date)}>＋ 일정 등록</button>}
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

      {regOpen && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setRegOpen(false); }}>
          <div className="modal-box cal-reg">
            <h3>새로운 일정 등록</h3>
            <p className="cal-reg-sub">시스템에 등록된 일정은 캘린더와 인수인계에 즉시 동기화됩니다.</p>
            <div className="cal-reg-tabs">
              <button className={regTab === 'att' ? 'on' : ''} onClick={() => setRegTab('att')}>근태/휴가 등록</button>
              <button className={regTab === 'event' ? 'on' : ''} onClick={() => setRegTab('event')}>팀 일정 등록</button>
            </div>

            {regTab === 'att' && (
              <div className="cal-reg-body">
                <label className="cal-reg-f">직원 이름
                  <select className="input" value={att.member} onChange={e => setAtt(a => ({ ...a, member: e.target.value }))}>
                    {members.length === 0 && <option value={att.member}>{att.member || '불러오는 중…'}</option>}
                    {members.map(m => (
                      <option key={m.realName} value={m.realName}>[{m.teamName}] {m.realName}</option>
                    ))}
                  </select>
                </label>
                <div className="cal-evdates">
                  <label className="cal-reg-f">시작일
                    <input className="input" type="date" value={att.start}
                      onChange={e => setAtt(a => ({ ...a, start: e.target.value, end: e.target.value > a.end ? e.target.value : a.end }))} />
                  </label>
                  <label className="cal-reg-f">종료일
                    <input className="input" type="date" value={att.end} min={att.start}
                      onChange={e => setAtt(a => ({ ...a, end: e.target.value }))} />
                  </label>
                </div>
                <label className="cal-reg-f">근태 구분
                  <select className="input" value={att.type} onChange={e => setAtt(a => ({ ...a, type: e.target.value }))}>
                    {SHIFT_TYPES.map(t => <option key={t}>{t}</option>)}
                  </select>
                </label>
                {att.type === '반반차' && (
                  <label className="cal-reg-f">시간대
                    <select className="input" value={att.halfTime} onChange={e => setAtt(a => ({ ...a, halfTime: e.target.value }))}>
                      {HALF_TIMES.map(t => <option key={t}>{t}</option>)}
                    </select>
                  </label>
                )}
                <div className="cal-reg-preview">
                  <b>💡 적용 미리보기</b>
                  <span>{attPreview()}</span>
                </div>
              </div>
            )}

            {regTab === 'event' && (
              <div className="cal-reg-body">
                <label className="cal-reg-f">등록자
                  <input className="input" value={user?.realName ?? ''} readOnly />
                </label>
                <div className="cal-evdates">
                  <label className="cal-reg-f">시작일
                    <input className="input" type="date" value={tev.start}
                      onChange={e => setTev(t => ({ ...t, start: e.target.value, end: e.target.value > t.end ? e.target.value : t.end }))} />
                  </label>
                  <label className="cal-reg-f">종료일
                    <input className="input" type="date" value={tev.end} min={tev.start}
                      onChange={e => setTev(t => ({ ...t, end: e.target.value }))} />
                  </label>
                </div>
                <label className="cal-reg-f">일정
                  <input className="input" value={tev.content} placeholder="예: Broken 회의"
                    onChange={e => setTev(t => ({ ...t, content: e.target.value }))} />
                </label>
                <label className="cal-reg-f">상세 내용
                  <textarea className="input cal-reg-ta" rows={3} value={tev.detail}
                    onChange={e => setTev(t => ({ ...t, detail: e.target.value }))} />
                </label>
              </div>
            )}

            <div className="cal-reg-foot">
              <span className="cal-reg-status">{holidayReady ? '공휴일 데이터 로딩 완료' : '공휴일 데이터 로딩 중…'}</span>
              <div className="modal-actions" style={{ margin: 0 }}>
                <button className="btn btn-ghost" onClick={() => setRegOpen(false)}>취소</button>
                <button className="btn btn-primary" disabled={regBusy || (regTab === 'att' && !holidayReady)} onClick={saveRegister}>일정 등록하기</button>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
