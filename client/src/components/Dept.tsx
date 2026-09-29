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

/**
 * 목록을 볼 부서. 관리자는 처음에 본인 부서 탭이 열리고(없으면 전체), 탭으로 다른 부서·전체를 고른다.
 * 관리자가 아니면 서버가 이미 본인 부서 자료만 주므로 늘 0(거르지 않음).
 * showTag — 표 줄마다 부서 이름표를 붙일지. 여러 부서가 섞여 보이는 '전체' 탭에서만 붙인다
 * (줄마다 같은 부서 이름이 붙으면 표가 난잡하다).
 */
export function useDeptView(isAdmin: boolean, depts: CalendarDept[]) {
  const [picked, setPicked] = useState<number | null>(null);
  const mine = depts.find(d => d.mine)?.id ?? 0;
  const value = isAdmin ? (picked ?? mine) : 0;
  return { value, set: setPicked, showTag: isAdmin && value === 0 };
}

/**
 * 목록 위 부서 탭 — 지금 어느 부서 자료를 보고 있는지. 관리자는 눌러서 부서를 바꾸고(마지막에 '전체'),
 * 그 밖에는 본인 부서 이름만 보인다. value 0 = 전체.
 */
export function DeptTabs({ isAdmin, depts, value, onChange, counts }: {
  isAdmin: boolean; depts: CalendarDept[]; value: number; onChange: (id: number) => void; counts?: (id: number) => number;
}) {
  if (!isAdmin) {
    const mine = depts.find(d => d.mine);
    return mine ? <div className="dept-tabs"><span className="dept-tab on solo">{mine.name}</span></div> : null;
  }
  if (depts.length === 0) return null;
  return (
    <div className="dept-tabs" role="tablist" aria-label="부서">
      {depts.map(d => (
        <button key={d.id} type="button" role="tab" aria-selected={value === d.id}
          className={`dept-tab ${value === d.id ? 'on' : ''}`} onClick={() => onChange(d.id)}>
          {d.name}{counts && <b>{counts(d.id)}</b>}
        </button>
      ))}
      {depts.length > 1 && (
        <button type="button" role="tab" aria-selected={value === 0}
          className={`dept-tab all ${value === 0 ? 'on' : ''}`} onClick={() => onChange(0)}>전체</button>
      )}
    </div>
  );
}
