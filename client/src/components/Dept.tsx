import { useEffect, useState } from 'react';
import { api } from '../api/client';
import type { CalendarDept } from '../api/types';
import { useAuth } from '../auth/AuthContext';

/**
 * 부서별로 따로 관리하는 자료(업체·견적서·단가표·체크시트·주간보고·교육·업무 분장)의 공용 부서 표시.
 *
 * - 관리자가 아니면 서버가 이미 본인 부서 자료만 내려준다. 화면은 '어느 부서 자료인지'만 보여 준다.
 * - 관리자는 모든 부서를 보므로 부서 거르기와 등록 부서 고르기를 쓴다.
 * 부서 목록(/api/depts — 조직도의 부서, 내 부서 표시 포함)은 계정마다 한 번만 불러 나눠 쓴다.
 */
let cache: { user: string; list: Promise<CalendarDept[]> } | null = null;

export function useDepts(): CalendarDept[] {
  const { user } = useAuth();
  const who = user?.username ?? '';
  const [depts, setDepts] = useState<CalendarDept[]>([]);
  useEffect(() => {
    let alive = true;
    // 같은 탭에서 다른 계정으로 로그인하면 '내 부서'가 달라지므로 다시 부른다.
    if (!cache || cache.user !== who) {
      const list = api.get<CalendarDept[]>('/api/depts').catch(() => { cache = null; return [] as CalendarDept[]; });
      cache = { user: who, list };
    }
    cache.list.then(d => { if (alive) setDepts(d); });
    return () => { alive = false; };
  }, [who]);
  return depts;
}

/** 부서 이름표(작은 색 칩). 부서가 없으면 '부서 미지정'. */
export function DeptTag({ id, name, depts }: { id: number | null | undefined; name?: string; depts: CalendarDept[] }) {
  const d = id == null ? undefined : depts.find(x => x.id === id);
  const label = d?.name || name || (id == null ? '부서 미지정' : '');
  if (!label) return null;
  return <span className="dept-tag" style={d ? { background: d.color } : undefined} title="등록 부서">{label}</span>;
}

/**
 * 등록 부서 칸. 관리자는 고르고(기본은 본인 부서), 그 밖에는 '내 부서로 등록됩니다'만 보인다.
 * value 가 null 이면 본인 부서로 맞춘다.
 */
export function DeptPick({ isAdmin, depts, value, onChange, what = '자료' }: {
  isAdmin: boolean; depts: CalendarDept[]; value: number | null; onChange: (id: number | null) => void; what?: string;
}) {
  const mine = depts.find(d => d.mine);
  useEffect(() => {
    if (value == null && mine) onChange(mine.id);
  }, [value, mine, onChange]);
  if (!isAdmin) {
    return (
      <div className="dept-fix">
        {mine
          ? <><DeptTag id={mine.id} depts={depts} /><em>내 부서 {what}로 등록됩니다</em></>
          : <em>소속 부서가 조직도에 없으면 등록할 수 없습니다</em>}
      </div>
    );
  }
  return (
    <select className="input" value={value ?? ''} onChange={e => onChange(e.target.value ? Number(e.target.value) : null)}>
      {depts.map(d => <option key={d.id} value={d.id}>{d.name}{d.mine ? ' (내 부서)' : ''}</option>)}
    </select>
  );
}

/** 관리자 목록 위의 부서 거르기 칩. 부서가 하나뿐이면 감춘다. value 0 = 전체. */
export function DeptFilter({ depts, value, onChange, counts }: {
  depts: CalendarDept[]; value: number; onChange: (id: number) => void; counts?: (id: number) => number;
}) {
  if (depts.length < 2) return null;
  return (
    <div className="dept-filter">
      <button type="button" className={value === 0 ? 'on' : ''} onClick={() => onChange(0)}>전체 부서</button>
      {depts.map(d => (
        <button key={d.id} type="button" className={value === d.id ? 'on' : ''} onClick={() => onChange(d.id)}>
          <i style={{ background: d.color }} />{d.name}{counts && <b>{counts(d.id)}</b>}
        </button>
      ))}
    </div>
  );
}
