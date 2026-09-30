import { useInRouterContext, useLocation } from 'react-router-dom';
import { useAuth } from './AuthContext';

// 사이드바에 따로 없는 하위 화면 → 그 화면을 여는 메뉴 경로
const SUB_PAGE_OF: Record<string, string> = {
  '/prodreq/options': '/prodreq',
  '/product-master': '/quotation',
};

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
  // 자재·물류(재고·폐기품·온습도). 옛 서버가 값을 안 보내면 현장 점검 등급을 따른다.
  const material = lv(user?.accessMaterial ?? user?.accessField);
  const mes = lv(user?.accessMes);
  const office = lv(user?.accessOffice);

  // 사용자별 숨김 하위 메뉴 / 조회만 메뉴 — 관리자는 항상 전부 표시·편집
  const parseSet = (json?: string): Set<string> => {
    if (user?.isAdmin || !json) return new Set();
    try {
      const arr = JSON.parse(json);
      return Array.isArray(arr) ? new Set(arr.filter((s): s is string => typeof s === 'string')) : new Set();
    } catch { return new Set(); }
  };
  const hidden = parseSet(user?.hiddenMenus);
  const readOnly = parseSet(user?.readOnlyMenus);
  // 지금 보고 있는 화면이 '조회만' 메뉴면 그 화면의 편집 버튼을 모두 끈다(서버도 편집 요청을 막는다).
  // 라우터 밖(로그인 전 틀 등)에서는 주소를 모르므로 끄지 않는다 — 같은 컴포넌트에서 라우터 유무는 바뀌지 않는다.
  // eslint-disable-next-line react-hooks/rules-of-hooks
  const path = useInRouterContext() ? useLocation().pathname : '';
  const menuOf = (route: string) => {
    const r = route.length > 1 ? route.replace(/\/+$/, '') : route;
    if (r.startsWith('/c/')) return '/checklist';
    if (r.startsWith('/e/')) return '/eq-check';   // 설비 호기 QR 화면은 체크시트(설비) 메뉴를 따른다
    return SUB_PAGE_OF[r] ?? r;
  };
  const pageReadOnly = path !== '' && readOnly.has(menuOf(path));
  const edit = (lvl: number) => lvl >= 2 && !pageReadOnly;

  return {
    isAdmin: !!user?.isAdmin,
    schedule, roster, handover, field, material, office, mes,
    canEditSchedule: edit(schedule),
    canEditRoster: edit(roster),
    canEditHandover: edit(handover),
    canEditField: edit(field),
    canEditMaterial: edit(material),
    canEditMes: edit(mes),
    canEditOffice: edit(office),
    hidden,
    readOnly,
    /** 이 메뉴가 이 사용자에게 '조회만'인가 */
    isReadOnly: (route: string) => readOnly.has(menuOf(route)),
    // 메뉴 안에서 버튼으로 들어가는 하위 화면은 그 메뉴를 따른다(주소를 직접 쳐도 숨김이 풀리지 않게).
    isHidden: (route: string) => {
      const r = route.length > 1 ? route.replace(/\/+$/, '') : route;   // '/broken/' 처럼 끝 빗금이 붙어도 같은 메뉴
      if (r.startsWith('/c/')) return hidden.has('/checklist');          // 구역 QR 화면은 체크시트 메뉴를 따른다
      if (r.startsWith('/e/')) return hidden.has('/eq-check');           // 설비 호기 QR 화면은 체크시트(설비) 메뉴를 따른다
      return hidden.has(r) || hidden.has(SUB_PAGE_OF[r] ?? '');
    },
  };
}
