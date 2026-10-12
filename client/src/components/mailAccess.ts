import { useEffect, useState } from 'react';
import { api } from '../api/client';

// 메일 확인 메뉴를 보여 줄지 — 시험 사용 중이라 허용된 계정(처음에는 1004)에만 메뉴가 보인다.
// 서버(MailGuardFilter)가 다시 확인하므로 이건 메뉴 표시용이다. 계정마다 한 번만 묻는다.
export interface MailAccess { allowed: boolean; internal: boolean; ip: string; internalOnly?: boolean }

const cache = new Map<number, Promise<MailAccess | null>>();
export function loadMailAccess(userId: number) {
  let p = cache.get(userId);
  if (!p) {
    p = api.get<MailAccess>('/api/mail/access').catch(() => null);
    cache.set(userId, p);
  }
  return p;
}

export function useMailAccess(userId: number | undefined) {
  const [v, setV] = useState<MailAccess | null>(null);
  useEffect(() => {
    if (!userId) { setV(null); return; }
    let alive = true;
    void loadMailAccess(userId).then(a => { if (alive) setV(a); });
    return () => { alive = false; };
  }, [userId]);
  return v;
}
