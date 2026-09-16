import { useEffect, useState, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAccess } from '../auth/useAccess';
import { useAuth } from '../auth/AuthContext';
import { api } from '../api/client';
import type { TodayStatus, TeamEvent, UpcomingEdu, Notice } from '../api/types';
import './Dashboard.css';

const DOW = ['일', '월', '화', '수', '목', '금', '토'];

function fmtMd(s: string | null): string {
  if (!s) return '';
  const d = new Date(s + 'T00:00:00');
  if (isNaN(d.getTime())) return s ?? '';
  const p = (n: number) => String(n).padStart(2, '0');
  return `${p(d.getMonth() + 1)}-${p(d.getDate())} (${DOW[d.getDay()]})`;
}
function daysUntil(s: string | null): number {
  if (!s) return 9999;
  const d = new Date(s + 'T00:00:00');
  if (isNaN(d.getTime())) return 9999;
  const t = new Date(); t.setHours(0, 0, 0, 0);
  return Math.round((d.getTime() - t.getTime()) / 86400000);
}
// 팀 일정 D-day — 여러 날 일정은 시작=미래→D-n, 오늘 시작→오늘, 이미 시작·미종료→진행중, 종료→완료
function eventDday(startDate: string | null, endDate: string | null): { label: string; cls: string } {
  const s = daysUntil(startDate);
  if (s > 3) return { label: `D-${s}`, cls: 'd-far' };
  if (s > 0) return { label: `D-${s}`, cls: 'd-soon' };
  if (s === 0) return { label: '오늘', cls: 'd-today' };
  return daysUntil(endDate) < 0 ? { label: '완료', cls: 'd-done' } : { label: '진행중', cls: 'd-far' };
}
function eduDday(startDate: string | null): { label: string; cls: string } {
  const n = daysUntil(startDate);
  if (n === 0) return { label: 'D-Day', cls: 'd-today' };
  if (n <= 2) return { label: `D-${n}`, cls: 'd-soon' };
  if (n <= 5) return { label: `D-${n}`, cls: 'd-far' };
  return { label: `D-${n}`, cls: 'd-green' };
}

/**
 * 카드 한 장. 앞으로 카드를 늘릴 때 이 껍데기만 재사용하면 된다.
 * 한 카드가 실패해도 대시보드 전체가 비지 않도록, 각 카드는 자기 데이터만 책임진다.
 */
function Card({ title, right, children }: { title: string; right?: React.ReactNode; children: React.ReactNode }) {
  return (
    <section className="db-card">
      <div className="db-card-h">
        <h3>{title}</h3>
        {right}
      </div>
      <div className="db-card-b">{children}</div>
    </section>
  );
}

export default function Dashboard() {
  const { handover } = useAccess();
  const { user } = useAuth();
  const nav = useNavigate();

  const [dash, setDash] = useState<TodayStatus | null>(null);
  const [notices, setNotices] = useState<Notice[]>([]);
  const [failed, setFailed] = useState(false);

  const load = useCallback(async () => {
    // 카드마다 따로 불러온다 — 하나가 막혀도(권한 없음 등) 나머지는 보여야 한다.
    try { setDash(await api.get<TodayStatus>('/api/schedule/today-status')); setFailed(false); }
    catch { setFailed(true); }
    if (handover >= 1) {
      try { setNotices(await api.get<Notice[]>('/api/notice')); } catch { /* 공지 권한이 없으면 비워 둔다 */ }
    }
  }, [handover]);
  useEffect(() => { load(); }, [load]);

  const hasEvents = (dash?.upcomingEvents.length ?? 0) > 0;
  const hasEdu = (dash?.upcomingEdu.length ?? 0) > 0;
  const hasNotice = notices.length > 0;

  return (
    <div>
      <header className="pg-header">
        <div>
          <h2>대시보드</h2>
          <p className="db-hello">{user?.realName}{user?.jobTitle ? ` ${user.jobTitle}` : ''}님, 오늘도 행복하세요.</p>
        </div>
        <span className="db-date">{dash?.date}</span>
      </header>

      <div className="pg-body">
        {failed && (
          <div className="db-failed">현황을 불러오지 못했습니다. 새로고침해도 같으면 서버 상태를 확인해 주세요.</div>
        )}

        <div className="db-grid">
          {handover >= 1 && (
            <Card title="공지 & 일정" right={<button className="db-more" onClick={() => nav('/notice')}>공지 관리</button>}>
              {!hasNotice && !hasEvents && !hasEdu && <p className="db-empty">표시할 공지와 일정이 없습니다.</p>}

              {notices.slice(0, 4).map(n => (
                <div key={n.id} className="db-notice"><span className="db-dot">•</span>{n.title || n.content}</div>
              ))}

              {hasEvents && (
                <div className="db-sub">
                  <h4>팀 일정</h4>
                  {dash!.upcomingEvents.map((e: TeamEvent) => {
                    const dd = eventDday(e.startDate, e.endDate);
                    return (
                      <div key={e.id} className="db-line">
                        <span className={`dday ${dd.cls}`}>{dd.label}</span>
                        <span className="db-line-d">{e.startDate === e.endDate ? fmtMd(e.startDate) : `${fmtMd(e.startDate)} ~ ${fmtMd(e.endDate)}`}</span>
                        <b>{e.content}</b>{e.detail && <span className="db-dim"> - {e.detail}</span>}
                      </div>
                    );
                  })}
                </div>
              )}

              {hasEdu && (
                <div className="db-sub">
                  <h4>교육 일정</h4>
                  <div className="db-edu-grid">
                    {dash!.upcomingEdu.map((e: UpcomingEdu, i) => {
                      const dd = eduDday(e.startDate);
                      return (
                        <div key={i} className="db-line db-edu-item">
                          <span className={`dday ${dd.cls}`}>{dd.label}</span>
                          <span className="db-line-d">{e.startDate === e.endDate ? fmtMd(e.startDate) : `${fmtMd(e.startDate)} ~ ${fmtMd(e.endDate)}`}</span>
                          <b>{e.memberName}</b> · {e.courseName}{e.eduMethod && <span className="db-dim"> ({e.eduMethod})</span>}
                        </div>
                      );
                    })}
                  </div>
                </div>
              )}
            </Card>
          )}

          <Card title="오늘의 근무 현황" right={<button className="db-more" onClick={() => nav('/calendar')}>일정 달력</button>}>
            {(dash?.teams.length ?? 0) === 0 && <p className="db-empty">표시할 팀이 없습니다.</p>}
            <div className="db-teams">
              {(dash?.teams ?? []).map(t => {
                // 위: 오늘 근무 인원(주간/야간 N명), 아래: 휴무·교육 명단
                const work = t.badges.find(b => b.kind === 'day' || b.kind === 'night');
                const offEdu = t.badges.filter(b => b.kind === 'dayoff' || b.kind === 'nightoff' || b.kind === 'off' || b.kind === 'edu');
                // 휴무·교육이 없는 팀은 한 줄로 축약 — 정보 있는 팀에 시선 집중
                if (offEdu.length === 0) {
                  return (
                    <div key={t.team} className="db-team compact">
                      <span className="db-team-n">{t.team}</span>
                      {work && <span className={`db-work k-${work.kind}`} title={work.names.join(', ')}>{work.kind === 'night' ? '야간' : '주간'} {work.names.length}명</span>}
                      <span className="db-team-none">휴무·교육 없음</span>
                    </div>
                  );
                }
                return (
                  <div key={t.team} className="db-team">
                    <div className="db-team-top">
                      <span className="db-team-n">{t.team}</span>
                      {work && <span className={`db-work k-${work.kind}`} title={work.names.join(', ')}>{work.kind === 'night' ? '야간' : '주간'} {work.names.length}명</span>}
                    </div>
                    <div className="db-team-badges">
                      {offEdu.map((b, i) => (
                        <div key={i} className="db-team-line">
                          <span className={`td-b k-${b.kind}`}>{b.text.replace(/:\s*\d+$/, '')}</span>
                          <span className="db-team-names">{b.names.join(', ')}</span>
                        </div>
                      ))}
                    </div>
                  </div>
                );
              })}
            </div>
          </Card>
        </div>
      </div>
    </div>
  );
}
