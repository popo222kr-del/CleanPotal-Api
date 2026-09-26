import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../../api/client';
import type { DashboardSummary } from '../../api/types';

// 대시보드 위쪽 — "지금 문제 있는 것"(이상 알림 띠)과 현장 숫자 타일(체크시트·온·습도·기타세정·생산팀 요청).
// 서버가 권한·숨긴 메뉴에 맞춰 카드를 걸러 보내므로(볼 수 없으면 null) 여기서는 온 것만 그린다.
// 현장 PC 에 띄워 두는 경우를 생각해 1분마다 새로 받는다.

const REFRESH_MS = 60_000;

function ago(iso: string | null): string {
  if (!iso) return '수신 기록 없음';
  const min = Math.floor((Date.now() - new Date(iso).getTime()) / 60000);
  if (min < 1) return '방금 수신';
  if (min < 60) return `${min}분 전 수신`;
  const h = Math.floor(min / 60);
  return h < 24 ? `${h}시간 ${min % 60}분 전 수신` : `${Math.floor(h / 24)}일 전 수신`;
}
const hm = (iso: string) => new Date(iso).toLocaleTimeString('ko-KR', { hour: '2-digit', minute: '2-digit', hour12: false });
const md = (ymd: string) => `${Number(ymd.slice(5, 7))}/${Number(ymd.slice(8, 10))}`;
const n1 = (v: number | null) => (v === null ? '—' : v.toFixed(1));

export default function SiteSummary() {
  const nav = useNavigate();
  const [s, setS] = useState<DashboardSummary | null>(null);
  const [failed, setFailed] = useState(false);

  const load = useCallback(async () => {
    try { setS(await api.get<DashboardSummary>('/api/dashboard/summary')); setFailed(false); }
    catch { setFailed(true); }
  }, []);
  useEffect(() => {
    load();
    const t = setInterval(load, REFRESH_MS);
    return () => clearInterval(t);
  }, [load]);

  if (!s) return failed ? <div className="db-failed">현장 요약을 불러오지 못했습니다.</div> : null;
  const { checklist: c, sensors: t, handover: h, prodReq: p } = s;
  if (!c && !t && !h && !p) return null;   // 볼 수 있는 현장 메뉴가 없는 사용자

  return (
    <div className="db-site">
      <div className={`db-alerts ${s.alerts.length ? 'has' : 'ok'}`}>
        {s.alerts.length === 0
          ? <span className="db-alert-ok"><i />현장 이상 없음</span>
          : s.alerts.map((a, i) => (
            <button key={i} className={`db-alert ${a.level}`} onClick={() => nav(a.link)}>{a.text}</button>
          ))}
        <span className="db-alert-at">{hm(s.at)} 기준 · 1분마다 새로 고침</span>
      </div>

      <div className="db-tiles">
        {c && (
          <button className="db-tile" onClick={() => nav('/checklist')}>
            <span className="db-tile-h">체크시트 <em>{md(c.workDate)} {c.shift}</em></span>
            <span className="db-tile-big">{c.submitted}<small>/{c.zones} 구역 제출</small></span>
            <span className="db-tile-sub">
              {c.inProgress > 0 && <span>진행 중 {c.inProgress}</span>}
              <span className={c.openNg ? 'bad' : ''}>미조치 NG {c.openNg}</span>
              <span className={c.weeklyOverdue ? 'warn' : ''}>주 1회 {c.weeklyOverdue ? `밀림 ${c.weeklyOverdue}` : `오늘 ${c.weeklyDueToday}`}</span>
            </span>
          </button>
        )}
        {t && (
          <button className="db-tile wide" onClick={() => nav('/temp-humidity')}>
            <span className="db-tile-h">온·습도 <em className={!t.collecting || t.online < t.total ? 'bad' : ''}>
              {!t.collecting ? '수집 끊김' : `${t.online}/${t.total} 수신`}</em></span>
            <span className="db-sensors">
              {t.sensors.map(x => (
                <span key={x.name} className={`db-sensor ${x.status}`} title={x.reason ?? undefined}>
                  <b>{x.name}</b>
                  <span>{x.status === 'offline' ? '수신 없음' : `${n1(x.temperature)}℃ · ${n1(x.humidity)}%`}</span>
                </span>
              ))}
            </span>
            <span className="db-tile-sub"><span>{ago(t.lastReceivedAt)}</span></span>
          </button>
        )}
        {h && (
          <button className="db-tile" onClick={() => nav('/handover')}>
            <span className="db-tile-h">기타세정 현황</span>
            <span className="db-tile-big">{h.open}<small>건 진행</small></span>
            <span className="db-tile-sub">
              <span>오늘 출고 {h.dueToday}</span>
              <span>내일 {h.dueTomorrow}</span>
              <span className={h.overdue ? 'bad' : ''}>지연 {h.overdue}</span>
            </span>
          </button>
        )}
        {p && (
          <button className="db-tile" onClick={() => nav('/prodreq')}>
            <span className="db-tile-h">생산팀 요청사항</span>
            <span className="db-tile-big">{p.unread}<small>건 미확인</small></span>
            <span className="db-tile-sub">
              <span>진행 {p.open}</span>
              <span className={p.overdue ? 'warn' : ''}>마감 지남 {p.overdue}</span>
            </span>
          </button>
        )}
      </div>
    </div>
  );
}
