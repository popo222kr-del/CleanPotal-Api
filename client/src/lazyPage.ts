import { lazy, type ComponentType } from 'react';

const RELOAD_KEY = 'cp_chunk_reload';

/**
 * 화면(페이지)을 처음 열 때 받아 온다 — 첫 화면에 엑셀·PDF·캡처 도구까지 한꺼번에 받지 않게.
 * (예전에는 QR 로 체크시트 한 구역을 열어도 2MB 넘는 파일을 통째로 받았다.)
 *
 * 배포 뒤 열려 있던 탭은 옛 파일 이름을 찾다가 실패한다(새 배포가 옛 파일을 지웠다).
 * 그때는 한 번만 새로고침해 새 index.html 을 받는다. 계속 실패하면 오류 화면(ErrorBoundary)으로 넘긴다.
 */
export function lazyPage<T extends ComponentType<object>>(load: () => Promise<{ default: T }>) {
  return lazy(async () => {
    try {
      const m = await load();
      try { sessionStorage.removeItem(RELOAD_KEY); } catch { /* 저장소를 못 써도 화면은 연다 */ }
      return m;
    } catch (err) {
      let reloaded = false;
      try { reloaded = sessionStorage.getItem(RELOAD_KEY) === '1'; } catch { /* 없으면 한 번 새로고침 */ }
      if (!reloaded) {
        try { sessionStorage.setItem(RELOAD_KEY, '1'); } catch { /* 무시 */ }
        location.reload();
        return new Promise<never>(() => { /* 새로고침되는 동안 기다린다 */ });
      }
      throw err;
    }
  });
}
