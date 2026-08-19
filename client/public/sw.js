// PWA 설치(홈 화면 추가)를 위한 최소 서비스 워커.
// API 응답은 항상 최신이어야 하므로 캐싱하지 않고, 정적 리소스만
// "네트워크 우선, 실패 시 캐시" 전략으로 오프라인/일시적 끊김에 대비한다.
// 새 배포 시 즉시 갱신되도록 설치와 동시에 활성화한다(구버전 캐시에 갇히지 않음).
const CACHE = 'cleanpotal-shell-v1';

self.addEventListener('install', (e) => {
  self.skipWaiting();
});

self.addEventListener('activate', (e) => {
  e.waitUntil(
    caches.keys().then((keys) =>
      Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k)))
    ).then(() => self.clients.claim())
  );
});

self.addEventListener('fetch', (e) => {
  const url = new URL(e.request.url);
  if (e.request.method !== 'GET' || url.pathname.startsWith('/api/')) return;

  e.respondWith(
    fetch(e.request)
      .then((res) => {
        const copy = res.clone();
        caches.open(CACHE).then((c) => c.put(e.request, copy));
        return res;
      })
      .catch(() => caches.match(e.request))
  );
});
