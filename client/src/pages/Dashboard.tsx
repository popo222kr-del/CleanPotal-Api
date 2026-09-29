import { useEffect, useState, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAccess } from '../auth/useAccess';
import { useAuth } from '../auth/AuthContext';
import { api } from '../api/client';
import type { TodayStatus, TeamEvent, TeamToday, UpcomingEdu, Notice } from '../api/types';
import SiteSummary from './dashboard/SiteSummary';
import DashboardEditor from './dashboard/DashboardEditor';
import { DEFAULT_LAYOUT, isShown, loadLayout, orderOf, saveLayout, type DashLayout } from './dashboard/layout';
import './Dashboard.css';

const DOW = ['일', '월', '화', '수', '목', '금', '토'];
// 오늘의 근무 현황 — 서버 배지 종류(ScheduleService.GetTodayStatusAsync)별 이름
const KIND_LABEL: Record<string, string> = { day: '주간', night: '야간', off: '휴무', edu: '교육' };

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
  const [openTeam, setOpenTeam] = useState<TeamToday | null>(null);   // 명단 상세 창

  // 개인 카드 구성(켜고 끄기·순서) — 계정에 저장해 PC·폰이 같다. 못 읽으면 기본 구성.
  const [layout, setLayout] = useState<DashLayout>(DEFAULT_LAYOUT);
  const [editing, setEditing] = useState(false);
  const [siteKeys, setSiteKeys] = useState<string[]>([]);   // 권한으로 볼 수 있는 현장 칸
  useEffect(() => { let alive = true; loadLayout().then(l => { if (alive) setLayout(l); }); return () => { alive = false; }; }, []);
  const onSiteKeys = useCallback((keys: string[]) => setSiteKeys(keys), []);
  async function applyLayout(l: DashLayout) {
    await saveLayout(l);
    setLayout(l);
  }

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

  // 부서별 묶음 — 부서 이름이 묶음 제목, 그 아래 팀이 한 칸씩. 서버가 이미 본부·부서·팀 순서로 내려준다.
  // 부서가 없는 줄(부서 미등록 생산팀, 조직도에 부서를 안 쓴 DB)은 제목 없이 맨 앞에 둔다.
  // 부서 제목 옆에는 그 부서의 생산직(생산팀 인원)·사무직(그 밖의 팀 인원) 수를 붙인다.
  const deptGroups: { dept: string; teams: TeamToday[]; prod: number; office: number }[] = [];
  for (const t of dash?.teams ?? []) {
    const d = t.dept ?? '';
    let g = deptGroups[deptGroups.length - 1];
    if (!g || g.dept !== d) { g = { dept: d, teams: [], prod: 0, office: 0 }; deptGroups.push(g); }
    g.teams.push(t);
    if (t.production) g.prod += t.members ?? 0; else g.office += t.members ?? 0;
  }

  const topCards: Record<string, React.ReactNode> = {
    notice: handover >= 1 && (
            <Card key="notice" title="공지 & 일정" right={<button className="db-more" onClick={() => nav('/notice')}>공지 관리</button>}>
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
    ),
    today: (
          <Card key="today" title="오늘의 근무 현황" right={<button className="db-more" onClick={() => nav('/calendar')}>일정 달력</button>}>
            {(dash?.teams.length ?? 0) === 0 && <p className="db-empty">표시할 팀이 없습니다.</p>}
            {deptGroups.map((g, gi) => (
            <div key={`${g.dept}|${gi}`} className="db-dept">
              {g.dept && (
                <div className="db-div-head">
                  {/* 본부는 붙이지 않고 부서 이름부터 — 부서가 곧 묶음 단위다 */}
                  <span className="db-dh-name">{g.dept}</span>
                  {g.prod > 0 && <span className="db-dh-hc">생산직 <b>{g.prod}</b>명</span>}
                  {g.office > 0 && <span className="db-dh-hc">사무직 <b>{g.office}</b>명</span>}
                  <span className="db-dh-total">{g.prod + g.office}명</span>
                </div>
              )}
            <div className="db-teams">
              {g.teams.map(t => (
                // 한 줄 요약(야간 6명 · 휴무 2명)만 보이고, 누르면 이름이 담긴 상세 창을 띄운다.
                <button key={t.team} type="button" className="db-team" onClick={() => setOpenTeam(t)} title="눌러서 명단 보기">
                  <span className="db-team-n">{t.team}</span>
                  <span className="db-team-sum">
                    {t.badges.length === 0 && <span className="db-team-none">휴무·교육 없음</span>}
                    {t.badges.map(b => (
                      <span key={b.kind} className={`td-b k-${b.kind}`}>{KIND_LABEL[b.kind] ?? b.kind} {b.names.length}명</span>
                    ))}
                  </span>
                  <span className="db-team-more" aria-hidden>›</span>
                </button>
              ))}
            </div>
            </div>
            ))}
          </Card>
    ),
  };
  // 개인 구성 순서대로, 켜 둔 카드만. 권한이 없는 카드(false)는 켜 있어도 나오지 않는다.
  const shownTop = orderOf('top', layout).filter(k => isShown(k, layout) && topCards[k]);
  const available = new Set([...Object.keys(topCards).filter(k => topCards[k]), ...siteKeys]);

  return (
    <div className="db-page">
      <header className="pg-header">
        <div>
          <h2>대시보드</h2>
          <p className="db-hello">{user?.realName}{user?.jobTitle ? ` ${user.jobTitle}` : ''}님, 오늘도 행복하세요.</p>
        </div>
        <span className="db-date">{dash?.date}</span>
        <button type="button" className="btn btn-ghost db-edit-btn" onClick={() => setEditing(true)} title="보고 싶은 카드만 켜고 순서 바꾸기">
          화면 구성
        </button>
      </header>

      <div className="pg-body">
        {failed && (
          <div className="db-failed">현황을 불러오지 못했습니다. 새로고침해도 같으면 서버 상태를 확인해 주세요.</div>
        )}

        {shownTop.length > 0 && <div className="db-grid db-top">{shownTop.map(k => topCards[k])}</div>}

        <SiteSummary layout={layout} onAvailable={onSiteKeys} />

        {shownTop.length === 0 && !siteKeys.some(k => isShown(k, layout)) && (
          <p className="db-empty db-all-off">모든 카드를 껐습니다. 오른쪽 위 <b>화면 구성</b>에서 다시 켤 수 있습니다.</p>
        )}
      </div>

      {openTeam && <TeamDetail team={openTeam} date={dash?.date ?? ''} onClose={() => setOpenTeam(null)} />}
      {editing && (
        <DashboardEditor layout={layout} available={available} onSave={applyLayout} onClose={() => setEditing(false)} />
      )}
    </div>
  );
}

/** 팀 한 곳의 오늘 명단 — 근무(주간/야간)·휴무·교육별 이름. */
function TeamDetail({ team, date, onClose }: { team: TeamToday; date: string; onClose: () => void }) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);
  return (
    <div className="modal-bg" onClick={onClose}>
      <div className="modal-box db-tm" role="dialog" aria-label={`${team.team} 오늘 명단`} onClick={e => e.stopPropagation()}>
        <div className="db-tm-h">
          <h3>{team.team}</h3>
          <span>{fmtMd(date)} 근무 현황</span>
        </div>
        {team.badges.length === 0 && <p className="db-empty">오늘 근무표에 휴무·교육으로 찍힌 사람이 없습니다.</p>}
        {team.badges.map(b => (
          <div key={b.kind} className="db-tm-sec">
            <div className="db-tm-t"><span className={`td-b k-${b.kind}`}>{KIND_LABEL[b.kind] ?? b.kind}</span><b>{b.names.length}명</b></div>
            <div className="db-tm-names">{b.names.map(n => <span key={n}>{n}</span>)}</div>
          </div>
        ))}
        <div className="modal-actions"><button className="btn" onClick={onClose}>닫기</button></div>
      </div>
    </div>
  );
}
