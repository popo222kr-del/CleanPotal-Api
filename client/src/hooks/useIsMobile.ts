import { useEffect, useState } from 'react';

/** 화면 폭이 좁으면(모바일) true. CSS 브레이크포인트(768px)와 동일 기준. */
export function useIsMobile(breakpoint = 768): boolean {
  const query = `(max-width: ${breakpoint}px)`;
  const [isMobile, setIsMobile] = useState<boolean>(
    () => typeof window !== 'undefined' && window.matchMedia(query).matches
  );
  useEffect(() => {
    const mq = window.matchMedia(query);
    const on = () => setIsMobile(mq.matches);
    on();
    mq.addEventListener('change', on);
    return () => mq.removeEventListener('change', on);
  }, [query]);
  return isMobile;
}

/**
 * 손가락으로 쓰는 기기(폰·태블릿 — 마우스가 없는 기기)면 true. 화면 폭과 달리 가로로 돌려도 바뀌지 않는다.
 * 체크시트는 이런 기기에서 QR 을 찍어야만 점검할 수 있게 한다(자리에서 목록을 눌러 체크하지 못하게).
 */
export function isTouchDevice(): boolean {
  return typeof window !== 'undefined' && window.matchMedia('(hover: none) and (pointer: coarse)').matches;
}
