import { useEffect, useState } from 'react';
import { api } from '../../api/client';
import type { Report, ReportGroup, ShiftTeams } from '../../api/types';
import WriterPills from '../../components/WriterPills';

// 대시보드 '생산 미팅' — 생산팀 인수인계(회의록)의 가장 최근 보고서(오늘 이전·오늘 중 최신) 주간·야간·Office 메모를 그대로 보인다.
// 읽기 전용. 고치려면 [인수인계 열기]로 /meeting 에 간다.

const DOW = ['일', '월', '화', '수', '목', '금', '토'];
/** "2026.10.15" → Date */
function parseDR(s: string): Date | null {
  const m = /^(\d{4})\.(\d{2})\.(\d{2})$/.exec((s ?? '').trim());
  return m ? new Date(+m[1], +m[2] - 1, +m[3]) : null;
}
const ymd = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

export interface MeetingState { report: Report | null; teams: ShiftTeams | null; loaded: boolean; failed: boolean }

/** 가장 최근 회의록 — 오늘보다 뒤 날짜(미리 만든 것)는 건너뛴다 */
export function useLatestMeeting(enabled: boolean): MeetingState {
  const [s, setS] = useState<MeetingState>({ report: null, teams: null, loaded: false, failed: false });
  useEffect(() => {
    if (!enabled) return;
    let alive = true;
    (async () => {
      try {
        const gs = await api.get<ReportGroup[]>('/api/reports?type=meeting');
        const today = new Date(); today.setHours(23, 59, 59, 999);
        const all = gs.flatMap(g => g.reports).map(r => ({ r, d: parseDR(r.dateRange) }))
          .filter((x): x is { r: typeof x.r; d: Date } => !!x.d && x.d <= today)
          .sort((a, b) => b.d.getTime() - a.d.getTime());
        const top = all[0];
        if (!top) { if (alive) setS({ report: null, teams: null, loaded: true, failed: false }); return; }
        const report = await api.get<Report>(`/api/reports/${top.r.id}`);
        const teams = await api.get<ShiftTeams>(`/api/schedule/shift-teams?date=${ymd(top.d)}`).catch(() => null);
        if (alive) setS({ report, teams, loaded: true, failed: false });
      } catch {
        if (alive) setS({ report: null, teams: null, loaded: true, failed: true });
      }
    })();
    return () => { alive = false; };
  }, [enabled]);
  return s;
}

export function meetingTitle(r: Report | null) {
  const d = r ? parseDR(r.dateRange) : null;
  return d ? `${r!.dateRange} (${DOW[d.getDay()]})` : '';
}

export default function MeetingBody({ state }: { state: MeetingState }) {
  const { report: r, teams, loaded, failed } = state;
  if (!loaded) return <p className="db-empty">불러오는 중…</p>;
  if (failed) return <p className="db-empty">인수인계를 불러오지 못했습니다.</p>;
  if (!r) return <p className="db-empty">작성된 인수인계가 없습니다.</p>;
  const sec = (cls: string, label: string, text: string, by?: string[]) => (
    <div className={`db-mt-sec ${cls}`}>
      <div className="db-mt-h"><span>{label}</span><WriterPills names={by} /></div>
      {text.trim() ? <div className="db-mt-txt">{text.trim()}</div> : <div className="db-mt-none">내용 없음</div>}
    </div>
  );
  const team = (list?: string[]) => (list && list.length ? ` · ${list.join(', ')}` : '');
  return (
    <div className="db-mt">
      {sec('day', `주간${team(teams?.dayTeams)}`, r.mainContent ?? '', r.writers?.main)}
      {sec('night', `야간${team(teams?.nightTeams)}`, r.nightContent ?? '', r.writers?.night)}
      {(r.memo ?? '').trim() && sec('memo', 'Office 메모', r.memo, r.writers?.memo)}
    </div>
  );
}
