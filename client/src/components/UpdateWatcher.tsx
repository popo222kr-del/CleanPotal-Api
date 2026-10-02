import { useEffect, useState } from 'react';
import { loadAbout, type PortalAbout } from '../hooks/useAbout';

// 새 버전 배포 알아채기 — 휴대폰 '홈 화면에 추가' 앱은 닫지 않으면 메모리에 있던 예전 화면(옛 JS)을 그대로 이어 써서
// 배포해도 바뀌지 않았다. 서버 빌드(/api/about 의 커밋·빌드 시각)를 처음 연 때와 비교해:
//  - 앱을 다시 화면에 띄울 때(홈 화면에서 다시 열기·다른 앱에서 돌아오기) 바뀌었으면 바로 새로고침
//    (단, 입력칸에 커서가 있으면 쓰던 것을 날리지 않게 안내만)
//  - 화면을 켜 둔 채 쓰는 중이면 강제로 새로고침하지 않고 아래에 '새 버전' 안내만 띄운다.

const CHECK_MS = 5 * 60_000;
const key = (a: PortalAbout | null) => (a ? `${a.commit}|${a.builtAt}|${a.dirty}` : '');

async function fetchAbout(): Promise<PortalAbout | null> {
  try {
    const r = await fetch('/api/about', { cache: 'no-store' });
    return r.ok ? ((await r.json()) as PortalAbout) : null;
  } catch { return null; }
}

/** 입력 중인가 — 입력칸·글상자에 커서가 있으면 자동 새로고침하지 않는다. */
function typing() {
  const el = document.activeElement as HTMLElement | null;
  return !!el && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.isContentEditable);
}

export default function UpdateWatcher() {
  const [newer, setNewer] = useState(false);

  useEffect(() => {
    let base = '';
    let alive = true;
    void loadAbout().then(a => { base = key(a); });   // 이 화면을 연 때의 빌드

    async function check(resumed: boolean) {
      if (!base) return;
      const now = key(await fetchAbout());
      if (!alive || !now || now === base) return;
      if (resumed && !typing()) location.reload();
      else setNewer(true);
    }
    const onVisible = () => { if (document.visibilityState === 'visible') void check(true); };
    // pageshow: 아이폰이 앱을 되살릴 때(뒤로가기 캐시) visibilitychange 대신 오기도 한다
    const onShow = (e: PageTransitionEvent) => { if (e.persisted) void check(true); };
    document.addEventListener('visibilitychange', onVisible);
    window.addEventListener('pageshow', onShow);
    const t = window.setInterval(() => void check(false), CHECK_MS);
    return () => {
      alive = false;
      document.removeEventListener('visibilitychange', onVisible);
      window.removeEventListener('pageshow', onShow);
      window.clearInterval(t);
    };
  }, []);

  if (!newer) return null;
  return (
    <div className="upd-bar" role="status">
      <span>새 버전이 배포되었습니다.</span>
      <button type="button" onClick={() => location.reload()}>새로고침</button>
    </div>
  );
}
