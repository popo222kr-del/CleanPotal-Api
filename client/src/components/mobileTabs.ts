import { useEffect, useState } from 'react';
import { api } from '../api/client';

// 모바일 하단 메뉴 구성 — 관리자가 정하고(관리자 영역 › 모바일 하단 메뉴) 모든 사람에게 같게 보인다.
// 맨 앞 '홈' 과 맨 끝 '더보기' 는 늘 있고, 그 사이 칸(최대 4개)을 고른다. 'qr' 은 QR 스캔 버튼.
// 볼 권한이 없거나 숨긴 메뉴는 그 사람에게는 빠져서 보인다(칸이 줄어든다).

export interface MobileTab { to: string; label?: string }
export const MAX_MOBILE_TABS = 4;
/** 정한 적이 없을 때 — 예전 하단 메뉴 그대로 */
export const DEFAULT_MOBILE_TABS: MobileTab[] = [
  { to: 'qr', label: 'QR 스캔' }, { to: '/calendar', label: '일정' }, { to: '/prodreq', label: '요청사항' }, { to: '/roster', label: '근무표' },
];
const EVENT = 'mobile-tabs-changed';

function parse(v: unknown): MobileTab[] | null {
  const items = (v && typeof v === 'object' ? (v as { items?: unknown }).items : null);
  if (!Array.isArray(items)) return null;
  return items
    .filter((x): x is MobileTab => !!x && typeof x === 'object' && typeof (x as MobileTab).to === 'string')
    .map(x => ({ to: x.to, label: typeof x.label === 'string' ? x.label.slice(0, 8) : undefined }))
    .slice(0, MAX_MOBILE_TABS);
}

export async function loadMobileTabs(): Promise<MobileTab[]> {
  try { return parse(await api.get<unknown>('/api/site-settings/mobile-tabs')) ?? DEFAULT_MOBILE_TABS; }
  catch { return DEFAULT_MOBILE_TABS; }
}

/** 저장(관리자). null 이면 기본값으로 되돌린다. 저장하면 열려 있는 하단 메뉴가 바로 바뀐다. */
export async function saveMobileTabs(tabs: MobileTab[] | null) {
  await api.put('/api/site-settings/mobile-tabs', tabs ? { items: tabs.slice(0, MAX_MOBILE_TABS) } : null);
  window.dispatchEvent(new Event(EVENT));
}

export function useMobileTabs(): MobileTab[] {
  const [tabs, setTabs] = useState<MobileTab[]>(DEFAULT_MOBILE_TABS);
  useEffect(() => {
    let alive = true;
    const load = () => loadMobileTabs().then(t => { if (alive) setTabs(t); });
    load();
    window.addEventListener(EVENT, load);
    return () => { alive = false; window.removeEventListener(EVENT, load); };
  }, []);
  return tabs;
}
