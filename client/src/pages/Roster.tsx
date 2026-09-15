import { useEffect, useState, useCallback } from 'react';
import { useAccess } from '../auth/useAccess';
import { api } from '../api/client';
import type { RosterMonth, StampedCell } from '../api/types';
import './Roster.css';

const STAMP_TYPES = ['주간', '야간', '반차', '휴무', '연차', '특근'];

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
  const { canEditRoster: canEdit } = useAccess();
  const today = new Date();
  const [year, setYear] = useState(today.getFullYear());
  const [month, setMonth] = useState(today.getMonth() + 1);
  const [team, setTeam] = useState('전체');
  // 팀 필터 버튼은 서버(조직도)에서 받아온다. 예전에는 ['전체','김팀','장팀'] 으로 박아 두어
  // 팀 이름을 바꾸면 눌러도 아무도 안 나오는 버튼만 남았다.
  const [teams, setTeams] = useState<string[]>(['전체']);
  const [predict, setPredict] = useState(false);
  const [data, setData] = useState<RosterMonth | null>(null);
  const [checked, setChecked] = useState<Set<string>>(new Set());
  const [stampType, setStampType] = useState('휴무');
  const [days, setDays] = useState(1);
  const [loading, setLoading] = useState(false);

  // silent=true 면 스피너 없이 조용히 다시 읽는다(도장 후 합계 동기화용).
  const load = useCallback(async (silent = false) => {
    if (!silent) setLoading(true);
    try {
      const q = `?year=${year}&month=${month}&team=${encodeURIComponent(team)}&predict=${predict}`;
      setData(await api.get<RosterMonth>(`/api/schedule/roster${q}`));
    } finally {
      if (!silent) setLoading(false);
    }
  }, [year, month, team, predict]);

  useEffect(() => { load(); }, [load]);

  useEffect(() => {
    api.get<string[]>('/api/schedule/teams')
      .then(names => {
        setTeams(['전체', ...names]);
        // 보고 있던 팀이 사라졌으면(이름 변경·해제) 전체로 되돌린다
        setTeam(cur => (cur === '전체' || names.includes(cur)) ? cur : '전체');
      })
      .catch(() => {});
  }, []);

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
  // 서버는 StampedCell 배열을 그대로 반환한다(api 클라이언트가 표준 봉투의 data 를 해제).
  function applyStamps(cells: StampedCell[]) {
    if (!Array.isArray(cells)) return;   // 예상과 다른 응답이 와도 화면이 죽지 않도록
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
    if (!canEdit) return;
    if (isEdu) { alert('교육 일정은 직접 수정할 수 없습니다.'); return; }
    const members = checked.size > 0 ? [...checked] : [memberName];
    if (checked.size === 0) { alert('먼저 좌측 체크박스로 대상자를 선택하세요.'); return; }
    const cells = await api.post<StampedCell[]>('/api/schedule/stamp', {
      members, startDate: date, shiftType: stampType, days, clear: false,
    });
    applyStamps(cells);      // 셀은 즉시 반영
    load(true);              // 개인·일별·팀별 합계는 서버 계산값으로 조용히 동기화
  }
  async function clearCell(e: React.MouseEvent, memberName: string, date: string, isEdu: boolean) {
    if (!canEdit) return;
    e.preventDefault();
    if (isEdu) return;
    const members = checked.size > 0 ? [...checked] : [memberName];
    if (checked.size === 0) { alert('먼저 대상자를 선택하세요.'); return; }
    // clear 여도 shiftType 은 채워 보낸다 — 서버 DTO 가 비-널 문자열이라 누락 시 null 이 들어간다.
    const cells = await api.post<StampedCell[]>('/api/schedule/stamp', {
      members, startDate: date, shiftType: stampType, days: 1, clear: true,
    });
    applyStamps(cells);
    load(true);
  }

  return (
    <div>
      <header className="rt-header">
        <div>
          <h2>근무표</h2>
        </div>
      </header>

      <div className="rt-body">
        <div className="rt-toolbar">
          <button className="rt-nav" onClick={prevMonth}>◀</button>
          <span className="rt-title">{year}년 {month}월</span>
          <button className="rt-nav" onClick={nextMonth}>▶</button>

          <div className="rt-filter">
            {teams.map(t => (
              <button key={t} className={team === t ? 'active' : ''} onClick={() => setTeam(t)}>{t}</button>
            ))}
          </div>

          <button className={`rt-toggle ${predict ? 'on' : ''}`} onClick={() => setPredict(p => !p)}>
            🔄 근무 미리보기 {predict ? 'ON' : 'OFF'}
          </button>

          <span className="rt-hint">
            💡 ① 근무 표시·일수 선택 → ② 좌측 체크박스로 대상자 선택 → ③ 시작 칸 <b>클릭</b>(찍기) / <b>우클릭</b>(지우기)
          </span>

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
