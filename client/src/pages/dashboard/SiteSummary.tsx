import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../../api/client';
import type { DashboardSummary } from '../../api/types';

// 대시보드 아래쪽 — 이상 알림 한 줄과 "현장 현황" 칸들.
// 칸은 모두 같은 모양(제목·오른쪽 보조 정보 / 큰 숫자 / 한 줄 요약)이고 같은 높이로 맞춘다.
// 열 수는 보이는 칸 수에 맞춰 고른다(9칸 3×3, 8칸 4×2 …) — 권한마다 칸 수가 달라도 빈칸이 덜 생기게.
// 기타세정과 주간세정은 같은 표를 업체 마스터로 나눈 두 메뉴다 — 둘 다 보여야 한다.
// 서버가 권한·숨긴 메뉴에 맞춰 카드를 걸러 보내므로(볼 수 없으면 null) 여기서는 온 것만 그린다.
// 현장 PC 에 띄워 두는 경우를 생각해 1분마다 새로 받는다.

const REFRESH_MS = 60_000;

/** 칸 수 → 열 수. 마지막 줄이 되도록 꽉 차게. */
const COLS: Record<number, number> = { 1: 1, 2: 2, 3: 3, 4: 4, 5: 3, 6: 3, 7: 4, 8: 4, 9: 3, 10: 5, 11: 4, 12: 4 };

const hm = (iso: string) => new Date(iso).toLocaleTimeString('ko-KR', { hour: '2-digit', minute: '2-digit', hour12: false });
const md = (ymd: string) => `${Number(ymd.slice(5, 7))}/${Number(ymd.slice(8, 10))}`;

/** 요약 줄의 한 조각. tone 이 있으면 색으로 강조(값이 0 이면 강조하지 않는다). */
function Stat({ label, value, tone }: { label: string; value: number | string; tone?: 'bad' | 'warn' }) {
  const on = tone && value !== 0 && value !== '0';
  return <span className={`db-stat ${on ? `db-${tone}` : ''}`}>{label} <b>{value}</b></span>;
}

function Tile({ title, meta, onClick, children }: {
  title: string; meta?: React.ReactNode; onClick?: () => void; children: React.ReactNode;
}) {
  const body = (
    <>
      <span className="db-tile-h"><span>{title}</span>{meta && <em>{meta}</em>}</span>
      {children}
    </>
  );
  return onClick
    ? <button className="db-tile" onClick={onClick}>{body}</button>
    : <div className="db-tile static">{body}</div>;
}

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

  if (!s) return failed ? <div className="db-failed">현장 현황을 불러오지 못했습니다.</div> : null;
  const { checklist: c, handover: h, weekly: w, prodReq: p, dispatch: d, broken: b } = s;
  const count = [c, h, w, p, d, b].filter(Boolean).length;
  if (count === 0) return null;   // 볼 수 있는 현장 메뉴가 없는 사용자

  const pct = c && c.zones ? Math.round((c.submitted / c.zones) * 100) : 0;

  return (
    <div className="db-site">
      <div className={`db-alerts ${s.alerts.length ? 'has' : 'ok'}`}>
        {s.alerts.length === 0
          ? <span className="db-alert-ok"><i />현장 이상 없음</span>
          : s.alerts.map((a, i) => (
            <button key={i} className={`db-alert ${a.level}`} onClick={() => nav(a.link)}>{a.text}</button>
          ))}
      </div>

      <section className="db-card">
        <div className="db-card-h">
          <h3>현장 현황</h3>
          <span className="db-dim">{hm(s.at)} 기준 · 1분마다 새로 고침</span>
        </div>
        <div className="db-tiles" style={{ '--cols': COLS[count] ?? 4 } as React.CSSProperties}>
          {c && (
            <Tile title="체크시트" meta={`${md(c.workDate)} ${c.shift}`} onClick={() => nav('/checklist')}>
              <span className="db-big">{c.submitted}<small>/ {c.zones} 구역 제출</small></span>
              <span className="db-bar"><i style={{ width: `${pct}%` }} /></span>
              <span className="db-sub">
                {c.inProgress > 0 && <Stat label="진행 중" value={c.inProgress} />}
                <Stat label="미조치 NG" value={c.openNg} tone="bad" />
                {c.weeklyOverdue > 0 ? <Stat label="주 1회 밀림" value={c.weeklyOverdue} tone="warn" /> : <Stat label="주 1회 오늘" value={c.weeklyDueToday} />}
              </span>
            </Tile>
          )}
          {([['기타세정 현황', '/handover', h], ['주간세정 현황', '/weekly', w]] as const).map(([title, link, x]) => x && (
            <Tile key={link} title={title} onClick={() => nav(link)}>
              <span className="db-big">{x.open}<small>건 진행</small></span>
              <span className="db-sub">
                <Stat label="오늘 출고" value={x.dueToday} />
                <Stat label="내일" value={x.dueTomorrow} />
                <Stat label="지연" value={x.overdue} tone="bad" />
              </span>
            </Tile>
          ))}
          {p && (
            <Tile title="생산팀 요청사항" onClick={() => nav('/prodreq')}>
              <span className="db-big">{p.unread}<small>건 미확인</small></span>
              <span className="db-sub">
                <Stat label="진행" value={p.open} />
                <Stat label="마감 지남" value={p.overdue} tone="warn" />
              </span>
            </Tile>
          )}
          {d && (
            <Tile title="오늘 배차" onClick={() => nav('/handover')}>
              <span className="db-big">{d.count}<small>건</small></span>
              <span className="db-sub"><span className="db-ellipsis">{d.vendors.length ? d.vendors.join(' · ') : '배차 없음'}</span></span>
            </Tile>
          )}
          {b && (
            <Tile title="BROKEN" meta={`올해 ${b.thisYear}건 · 공식 ${b.officialThisYear}`} onClick={() => nav('/broken')}>
              <span className="db-big">{b.thisMonth}<small>건 이번 달</small></span>
              <span className="db-sub">
                <Stat label="미완료" value={b.open} tone="warn" />
                {b.recent && (
                  <span className="db-ellipsis">
                    최근 {b.recent.occurDate ? md(b.recent.occurDate) : ''} {[b.recent.line, b.recent.productName].filter(Boolean).join(' ')} · {b.recent.status}
                  </span>
                )}
              </span>
            </Tile>
          )}
        </div>
      </section>
    </div>
  );
}
