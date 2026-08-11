import { useAuth } from './AuthContext';

/**
 * 영역×등급 권한 헬퍼. 등급: 0=없음(메뉴 숨김), 1=조회 전용, 2=편집.
 * 관리자(isAdmin)는 모든 영역 편집으로 취급.
 */
export function useAccess() {
  const { user } = useAuth();
  const lv = (v: number | undefined) => (user?.isAdmin ? 2 : (v ?? 0));
  const schedule = lv(user?.accessSchedule);
  const roster = lv(user?.accessRoster);
  const handover = lv(user?.accessHandover);
  const field = lv(user?.accessField);
  const office = lv(user?.accessOffice);

  // 사용자별 숨김 하위 메뉴 — 관리자는 항상 전부 표시
  let hidden: Set<string> = new Set();
  if (!user?.isAdmin && user?.hiddenMenus) {
    try {
      const arr = JSON.parse(user.hiddenMenus);
      if (Array.isArray(arr)) hidden = new Set(arr.filter((s): s is string => typeof s === 'string'));
    } catch { /* ignore */ }
  }

  return {
    isAdmin: !!user?.isAdmin,
    schedule, roster, handover, field, office,
    canEditSchedule: schedule >= 2,
    canEditRoster: roster >= 2,
    canEditHandover: handover >= 2,
    canEditField: field >= 2,
    canEditOffice: office >= 2,
    hidden,
    isHidden: (route: string) => hidden.has(route),
  };
}
