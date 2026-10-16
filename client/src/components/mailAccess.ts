import { useEffect, useState } from 'react';
import { api } from '../api/client';

// 메일 확인 메뉴를 보여 줄지 — 시험 사용 중이라 허용된 계정(처음에는 1004)에만 메뉴가 보인다.
// 서버(MailGuardFilter)가 다시 확인하므로 이건 메뉴 표시용이다. 계정마다 한 번만 묻는다.
export interface MailAccess { allowed: boolean; internal: boolean; ip: string; internalOnly?: boolean }

const cache = new Map<number, Promise<MailAccess | null>>();
export function loadMailAccess(userId: number) {
  let p = cache.get(userId);
  if (!p) {
    // 실패(네트워크 끊김 등)는 기억하지 않는다 — 예전에는 실패도 기억해서, 홈 화면 앱이 깨어날 때 한 번 실패하면
    // 앱을 완전히 닫을 때까지 메일 메뉴가 숨어 있었다.
    p = api.get<MailAccess>('/api/mail/access').catch(() => { cache.delete(userId); return null; });
    cache.set(userId, p);
  }
  return p;
}

export function useMailAccess(userId: number | undefined) {
  const [v, setV] = useState<MailAccess | null>(null);
  useEffect(() => {
    if (!userId) { setV(null); return; }
    let alive = true;
    const load = () => void loadMailAccess(userId).then(a => { if (alive && a) setV(a); });
    load();
    // 앱을 다시 화면에 띄울 때(홈 화면 앱·다른 앱에서 돌아오기) 다시 묻는다 — 와이파이↔모바일 데이터가 바뀌었을 수도 있다
    const onVisible = () => { if (document.visibilityState === 'visible') { cache.delete(userId); load(); } };
    document.addEventListener('visibilitychange', onVisible);
    return () => { alive = false; document.removeEventListener('visibilitychange', onVisible); };
  }, [userId]);
  return v;
}
