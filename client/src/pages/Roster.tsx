import { useEffect, useState, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import type { RosterMonth, StampedCell } from '../api/types';
import './Roster.css';

const STAMP_TYPES = ['주간', '야간', '반차', '휴무', '연차', '특근'];
const TEAMS = ['전체', '김팀', '장팀'];

// 근무 표시 → 짧은 라벨 + 색상 클래스
function display(shiftType: string): string {
  const v = shiftType.replace('예상:', '');
  if (v.includes('주간')) return '주';
  if (v.includes('야간')) return '야';
  if (v.includes('반반차')) return '반반';
  if (v.includes('반차')) return '반차';
  if (v.includes('휴무')) return '휴';
  if (v.includes('연차')) return '연차';
  if (v.includes('교육')) return '교육';
  if (v.includes('특근')) return '특근';
  return v;
}
function colorKey(shiftType: string): string {
  const v = shiftType.replace('예상:', '');
  if (v.includes('주간')) return 'day';
  if (v.includes('야간')) return 'night';
  if (v.includes('반')) return 'half';
  if (v.includes('휴무')) return 'off';
  if (v.includes('연차')) return 'annual';
  if (v.includes('교육')) return 'edu';
  if (v.includes('특근')) return 'extra';
  return '';
}

export default function Roster() {
  const today = new Date();
  const [year, setYear] = useState(today.getFullYear());
  const [month, setMonth] = useState(today.getMonth() + 1);
  const [team, setTeam] = useState('전체');
  const [predict, setPredict] = useState(false);
  const [data, setData] = useState<RosterMonth | null>(null);
  const [checked, setChecked] = useState<Set<string>>(new Set());
  const [stampType, setStampType] = useState('휴무');
  const [days, setDays] = useState(1);
  const [loading, setLoading] = useState(false);
  const nav = useNavigate();

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const q = `?year=${year}&month=${month}&team=${encodeURIComponent(team)}&predict=${predict}`;
      setData(await api.get<RosterMonth>(`/api/schedule/roster${q}`));
    } finally {
      setLoading(false);
    }
  }, [year, month, team, predict]);

  useEffect(() => { load(); }, [load]);

  function prevMonth() { if (month === 1) { setYear(y => y - 1); setMonth(12); } else setMonth(m => m - 1); }
  function nextMonth() { if (month === 12) { setYear(y => y + 1); setMonth(1); } else setMonth(m => m + 1); }

  function toggleMember(name: string) {
    setChecked(prev => {
      const s = new Set(prev);
      s.has(name) ? s.delete(name) : s.add(name);
      return s;
    });
  }
  function toggleTeamAll(names: string[], on: boolean) {
    setChecked(prev => {
      const s = new Set(prev);
      names.forEach(n => on ? s.add(n) : s.delete(n));
      return s;
    });
  }

  // 셀에 도장/지우기 적용 — 응답으로 로컬 상태만 패치 (재조회 없이 즉시 반영)
  function applyStamps(cells: StampedCell[]) {
    setData(prev => {
      if (!prev) return prev;
      const map = new Map(cells.map(c => [`${c.name}|${c.date}`, c.shiftType]));
      return {
        ...prev,
        teams: prev.teams.map(t => ({
          ...t,
          members: t.members.map(m => ({
            ...m,
            cells: m.cells.map(c => {
              const key = `${m.name}|${c.date}`;
              return map.has(key) ? { ...c, shiftType: map.get(key)!, isPredicted: false } : c;
            }),
          })),
        })),
      };
    });
  }

  async function stamp(memberName: string, date: string, isEdu: boolean) {
    if (isEdu) { alert('교육 일정은 직접 수정할 수 없습니다.'); return; }
    const members = checked.size > 0 ? [...checked] : [memberName];
    if (checked.size === 0) { alert('먼저 좌측 체크박스로 대상자를 선택하세요.'); return; }
    const res = await api.post<{ ok: boolean; cells: StampedCell[] }>('/api/schedule/stamp', {
      members, startDate: date, shiftType: stampType, days, clear: false,
    });
    applyStamps(res.cells);
  }
  async function clearCell(e: React.MouseEvent, memberName: string, date: string, isEdu: boolean) {
    e.preventDefault();
    if (isEdu) return;
    const members = checked.size > 0 ? [...checked] : [memberName];
    if (checked.size === 0) { alert('먼저 대상자를 선택하세요.'); return; }
    const res = await api.post<{ ok: boolean; cells: StampedCell[] }>('/api/schedule/stamp', {
      members, startDate: date, clear: true,
    });
    applyStamps(res.cells);
  }

  return (
    <div>
      <header className="rt-header">
        <button className="rt-back" onClick={() => nav('/calendar')}>← 일정 달력</button>
        <div>
          <h2>근무표</h2>
          <p>인원을 체크하고 근무 표시를 찍어 일괄 등록합니다</p>
        </div>
      </header>

      <div className="rt-body">
        <div className="rt-toolbar">
          <button className="rt-nav" onClick={prevMonth}>◀</button>
          <span className="rt-title">{year}년 {month}월</span>
          <button className="rt-nav" onClick={nextMonth}>▶</button>

          <div className="rt-filter">
            {TEAMS.map(t => (
              <button key={t} className={team === t ? 'active' : ''} onClick={() => setTeam(t)}>{t}</button>
            ))}
          </div>

          <button className={`rt-toggle ${predict ? 'on' : ''}`} onClick={() => setPredict(p => !p)}>
            🔄 근무 미리보기 {predict ? 'ON' : 'OFF'}
          </button>

          <div className="rt-stamp">
            <label>근무 표시</label>
            <select value={stampType} onChange={e => setStampType(e.target.value)}>
              {STAMP_TYPES.map(s => <option key={s} value={s}>{s}</option>)}
            </select>
            <input type="number" min={1} max={31} value={days}
              onChange={e => setDays(Math.max(1, parseInt(e.target.value) || 1))} />
            <span>일</span>
          </div>
        </div>

        <div className="rt-hint">
          💡 ① 근무 표시·일수 선택 → ② 좌측 체크박스로 대상자 선택 → ③ 시작 칸 <b>클릭</b>(찍기) / <b>우클릭</b>(지우기)
        </div>

        {loading && <div className="rt-loading">불러오는 중…</div>}

        {data && data.teams.length === 0 && (
          <div className="rt-empty">표시할 팀원이 없습니다. 사용자 계정 관리에서 소속팀(김팀/장팀)을 지정하세요.</div>
        )}

        {data && data.teams.length > 0 && (
          <div className="rt-wrap">
            <table className="rt-table">
              <thead>
                <tr>
                  <th className="c-chk"></th>
                  <th className="c-name">성명</th>
                  <th className="c-sum">합계</th>
                  {data.days.map(d => (
                    <th key={d.day} className={`day-h ${d.isHoliday || d.dayOfWeek === '일' ? 'sun' : d.dayOfWeek === '토' ? 'sat' : ''} ${d.isWeekend || d.isHoliday ? 'weekend' : ''}`}>
                      {d.day}<div className="dow">{d.dayOfWeek}</div>
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {data.teams.map(t => {
                  const teamNames = t.members.map(m => m.name);
                  const allChecked = teamNames.every(n => checked.has(n));
                  return (
                    <RosterTeamRows key={t.team}
                      team={t} allChecked={allChecked}
                      onTeamToggle={on => toggleTeamAll(teamNames, on)}
                      checked={checked} onMemberToggle={toggleMember}
                      onStamp={stamp} onClear={clearCell} numDays={data.days.length} />
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}

function RosterTeamRows({ team, allChecked, onTeamToggle, checked, onMemberToggle, onStamp, onClear, numDays }: {
  team: RosterMonth['teams'][0];
  allChecked: boolean;
  onTeamToggle: (on: boolean) => void;
  checked: Set<string>;
  onMemberToggle: (name: string) => void;
  onStamp: (name: string, date: string, isEdu: boolean) => void;
  onClear: (e: React.MouseEvent, name: string, date: string, isEdu: boolean) => void;
  numDays: number;
}) {
  return (
    <>
      <tr className="team-row">
        <td colSpan={numDays + 3}>
          <input type="checkbox" checked={allChecked} onChange={e => onTeamToggle(e.target.checked)} />
          {' '}{team.team} <span style={{ fontWeight: 400, opacity: 0.7, fontSize: 11 }}>(월 근무 {team.grandTotal}일)</span>
        </td>
      </tr>
      {team.members.map(m => (
        <tr key={m.name}>
          <td className="c-chk">
            <input type="checkbox" checked={checked.has(m.name)} onChange={() => onMemberToggle(m.name)} />
          </td>
          <td className="c-name">{m.name}</td>
          <td className="c-sum">{m.totalWorkDays}</td>
          {m.cells.map(c => {
            const isEdu = c.shiftType.includes('교육');
            return (
              <td key={c.date}
                className={`cell ${colorKey(c.shiftType)} ${c.isPredicted ? 'predicted' : ''}`}
                onClick={() => onStamp(m.name, c.date, isEdu)}
                onContextMenu={e => onClear(e, m.name, c.date, isEdu)}>
                {display(c.shiftType)}
              </td>
            );
          })}
        </tr>
      ))}
      <tr className="total-row">
        <td className="total-label" colSpan={3}>근무 인원</td>
        {team.dailyCounts.map((c, i) => <td key={i}>{c || ''}</td>)}
      </tr>
    </>
  );
}
