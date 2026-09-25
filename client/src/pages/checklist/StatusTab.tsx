import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../../api/client';
import type { CheckShiftStatus, CheckStatus } from '../../api/types';
import { dayLabel, timeLabel } from './common';

// 구역 × 주간/야간 점검 현황. 칸을 누르면 그 구역 점검 화면(QR 없이)으로 들어간다.

const STATE_LABEL: Record<string, string> = { none: '미점검', progress: '진행 중', submitted: '제출', na: '해당 없음' };

export default function StatusTab({ onOpenNg }: { onOpenNg: () => void }) {
  const nav = useNavigate();
  const [date, setDate] = useState('');
  const [status, setStatus] = useState<CheckStatus | null>(null);

  const load = useCallback(async () => {
    try {
      setStatus(await api.get<CheckStatus>(`/api/checklist/status${date ? `?date=${date}` : ''}`));
    } catch (e) {
      alert(e instanceof Error ? e.message : '현황을 불러오지 못했습니다.');
    }
  }, [date]);
  useEffect(() => { load(); }, [load]);

  if (!status) return <div className="ck-empty">불러오는 중…</div>;

  function open(code: string, shift: string) {
    nav(`/c/${encodeURIComponent(code)}?date=${status!.workDate}&shift=${encodeURIComponent(shift)}&from=hub`);
  }

  function Cell({ code, shift, s }: { code: string; shift: string; s: CheckShiftStatus }) {
    const isNow = status!.workDate === status!.currentWorkDate && status!.currentShift === shift;
    return (
      <button className={`ck-cell ${s.state} ${isNow ? 'now' : ''}`} onClick={() => open(code, shift)} disabled={s.state === 'na'}>
        <span className="ck-cstate">{STATE_LABEL[s.state]}</span>
        {s.state !== 'na' && <span className="ck-ccount">{s.done}/{s.total}</span>}
        {s.ng > 0 && <span className="ck-cng">NG {s.ng}</span>}
        {s.submittedAt && <span className="ck-cwho">{s.submittedByName} {timeLabel(s.submittedAt).split(' ')[1]}</span>}
      </button>
    );
  }

  return (
    <div>
      <div className="ck-bar-row">
        <input className="input ck-date" type="date" value={date || status.workDate} onChange={e => setDate(e.target.value)} />
        <span className="ck-now">
          지금: {dayLabel(status.currentWorkDate)} <b className={`ck-shift ${status.currentShift === '주간' ? 'day' : 'night'}`}>{status.currentShift}</b>
        </span>
        <button className="btn btn-ghost" onClick={load}>새로고침</button>
        {status.openNg > 0 && <button className="ck-ngbadge" onClick={onOpenNg}>미조치 NG {status.openNg}건</button>}
      </div>

      {status.lines.length === 0 && <div className="ck-empty">등록된 구역이 없습니다. 양식 관리에서 구역·항목을 넣어 주세요.</div>}
      {status.lines.map(l => (
        <section key={l.line} className="ck-card">
          <h3>{l.line}</h3>
          <table className="ck-status">
            <thead>
              <tr><th>구역</th><th>주간</th><th>야간</th><th>주 1회</th></tr>
            </thead>
            <tbody>
              {l.zones.map(z => (
                <tr key={z.code}>
                  <td><b>{z.name}</b><div className="ck-dim">{z.code}</div></td>
                  <td><Cell code={z.code} shift="주간" s={z.day} /></td>
                  <td><Cell code={z.code} shift="야간" s={z.night} /></td>
                  <td>
                    {z.weeklyDue > 0 && <span className="ck-tag warn">오늘 {z.weeklyDue}</span>}
                    {z.weeklyOverdue > 0 && <span className="ck-tag bad">밀림 {z.weeklyOverdue}</span>}
                    {z.weeklyDue === 0 && z.weeklyOverdue === 0 && <span className="ck-dim">-</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      ))}
      <p className="ck-hint">칸을 누르면 그 구역 점검 화면이 열립니다(QR 없이 들어온 것으로 기록). 지금 교대와 바로 앞 교대만 입력할 수 있습니다.</p>
    </div>
  );
}
