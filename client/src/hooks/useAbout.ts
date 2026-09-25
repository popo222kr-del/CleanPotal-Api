import { useEffect, useState } from 'react';

// 서버가 알려 주는 어느 서버(개발·테스트·운영)·어느 빌드인지(PortalAbout). 한 번만 받아 나눠 쓴다.

export interface PortalAbout { env: string; envLabel: string; commit: string; subject: string; builtAt: string; dirty: boolean }

let cached: Promise<PortalAbout | null> | null = null;
function loadAbout() {
  cached ??= fetch('/api/about')
    .then(r => (r.ok ? (r.json() as Promise<PortalAbout>) : null))
    .catch(() => null);
  return cached;
}

export function useAbout() {
  const [about, setAbout] = useState<PortalAbout | null>(null);
  useEffect(() => {
    let alive = true;
    loadAbout().then(a => { if (alive) setAbout(a); });
    return () => { alive = false; };
  }, []);
  return about;
}
