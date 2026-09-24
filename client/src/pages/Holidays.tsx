import { useCallback, useEffect, useState } from 'react';
import { api } from '../api/client';
import './Holidays.css';

// 공휴일 관리(관리자) — 코드에 적힌 기본 목록 위에 임시공휴일·선거일·다음 해 공휴일을 덧붙이거나 뺀다.
// 근무표·통합 일정 달력의 휴일 표시와 근태 등록(연차 차감 계산)이 이 값을 따른다.
type Source = 'builtin' | 'added' | 'renamed' | 'removed';
type Row = { date: string; name: string; source: Source; builtInName: string | null; updatedBy: string | null; updatedAt: string | null };
type Page = { year: number; hasBuiltIn: boolean; rows: Row[] };

const SOURCE_LABEL: Record<Source, string> = { builtin: '기본', added: '추가', renamed: '이름 변경', removed: '평일로 변경' };
const DOW = ['일', '월', '화', '수', '목', '금', '토'];
const dow = (ymd: string) => DOW[new Date(`${ymd}T00:00:00`).getDay()];

export default function Holidays() {
  const [year, setYear] = useState(new Date().getFullYear());
  const [page, setPage] = useState<Page | null>(null);
  const [busy, setBusy] = useState(false);
  const [newDate, setNewDate] = useState('');
  const [newName, setNewName] = useState('');

  const load = useCallback((y: number) => {
    api.get<Page>(`/api/holidays/manage?year=${y}`).then(setPage)
      .catch(e => alert(e instanceof Error ? e.message : '공휴일을 불러오지 못했습니다.'));
  }, []);
  useEffect(() => { load(year); }, [year, load]);

  async function run(work: () => Promise<Page>) {
    if (busy) return;
    setBusy(true);
    try { setPage(await work()); }
    catch (e) { alert(e instanceof Error ? e.message : '저장하지 못했습니다.'); }
    finally { setBusy(false); }
  }

  const save = (date: string, name: string, isOff: boolean) =>
    run(() => api.put<Page>('/api/holidays/manage', { date, name, isOff }));
  const reset = (date: string) => run(() => api.del<Page>(`/api/holidays/manage/${date}`));

  function add() {
    const name = newName.trim();
    if (!newDate || !name) { alert('날짜와 이름을 모두 적어 주세요.'); return; }
    if (Number(newDate.slice(0, 4)) !== year) setYear(Number(newDate.slice(0, 4)));
    save(newDate, name, true).then(() => { setNewDate(''); setNewName(''); });
  }

  function rename(r: Row) {
    const name = prompt(`${r.date} 공휴일 이름`, r.name)?.trim();
    if (!name || name === r.name) return;
    save(r.date, name, true);
  }

  return (
    <div>
      <header className="pg-header">
        <div>
          <h2>공휴일 관리</h2>
          <p>임시공휴일·선거일·다음 해 공휴일을 넣거나 뺍니다. 근무표·통합 일정 달력·근태 등록(연차 차감)에 바로 반영됩니다.</p>
        </div>
      </header>

      <div className="pg-body">
        <div className="hol-bar">
          <button className="btn btn-ghost" onClick={() => setYear(y => y - 1)} disabled={busy}>◀</button>
          <b className="hol-year">{year}년</b>
          <button className="btn btn-ghost" onClick={() => setYear(y => y + 1)} disabled={busy}>▶</button>
        </div>

        {page && !page.hasBuiltIn && (
          <div className="hol-warn">
            {year}년은 기본 공휴일 목록이 없습니다. 한국천문연구원 월력요항(매년 6월 무렵 발표)을 보고
            설·추석·대체공휴일까지 아래에서 모두 넣어 주세요. 비어 있으면 그해 공휴일이 하나도 없는 것으로 계산됩니다.
          </div>
        )}

        <div className="hol-add">
          <input className="input" type="date" value={newDate} onChange={e => setNewDate(e.target.value)} />
          <input className="input" placeholder="이름 (예: 임시공휴일)" maxLength={40} value={newName}
            onChange={e => setNewName(e.target.value)}
            onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); add(); } }} />
          <button className="btn btn-primary" onClick={add} disabled={busy}>공휴일 추가</button>
        </div>

        <table className="hol-table">
          <thead>
            <tr><th>날짜</th><th>요일</th><th>이름</th><th>구분</th><th>수정</th><th /></tr>
          </thead>
          <tbody>
            {page?.rows.map(r => (
              <tr key={r.date} className={r.source === 'removed' ? 'hol-off' : ''}>
                <td>{r.date}</td>
                <td>{dow(r.date)}</td>
                <td>{r.name}{r.source === 'renamed' && r.builtInName && <span className="hol-orig"> (기본: {r.builtInName})</span>}</td>
                <td><span className={`hol-tag hol-${r.source}`}>{SOURCE_LABEL[r.source]}</span></td>
                <td className="hol-who">{r.updatedBy ? `${r.updatedBy} · ${r.updatedAt?.slice(0, 10)}` : ''}</td>
                <td className="hol-acts-cell"><div className="hol-acts">
                  {r.source !== 'removed' && <button className="btn btn-ghost" disabled={busy} onClick={() => rename(r)}>이름</button>}
                  {r.source === 'builtin' && (
                    <button className="btn btn-ghost" disabled={busy}
                      onClick={() => confirm(`${r.date} ${r.name}을(를) 평일로 바꿀까요?`) && save(r.date, r.name, false)}>평일로</button>
                  )}
                  {r.source === 'added' && (
                    <button className="btn btn-ghost" disabled={busy}
                      onClick={() => confirm(`${r.date} ${r.name}을(를) 지울까요?`) && reset(r.date)}>삭제</button>
                  )}
                  {(r.source === 'renamed' || r.source === 'removed') && (
                    <button className="btn btn-ghost" disabled={busy} onClick={() => reset(r.date)}>기본으로</button>
                  )}
                </div></td>
              </tr>
            ))}
            {page && page.rows.length === 0 && (
              <tr><td colSpan={6} className="hol-empty">등록된 공휴일이 없습니다.</td></tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
