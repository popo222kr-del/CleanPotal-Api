import { useEffect, useRef, useState } from 'react';
import { useLocation } from 'react-router-dom';
import './ScrollTop.css';

// 맨 위로 — 모든 화면 오른쪽 아래. 페이지(창)든 표 안 스크롤이든 마지막으로 굴린 곳이 많이 내려가 있으면 나타나고,
// 누르면 그곳을 맨 위로 올린다. 화면을 옮기면 숨는다.

const SHOW_AT = 400;   // px — 이만큼 내려가면 버튼이 보인다

export default function ScrollTop() {
  const [show, setShow] = useState(false);
  const target = useRef<Element | null>(null);   // null = 페이지(창)
  const loc = useLocation();

  useEffect(() => { target.current = null; setShow(false); }, [loc.pathname]);

  useEffect(() => {
    const onScroll = (e: Event) => {
      const el = e.target instanceof Element ? e.target : null;   // document 면 페이지
      // 자동 완성 목록·메뉴처럼 작은 스크롤 영역은 무시
      if (el && el.clientHeight < 240) return;
      const top = el ? el.scrollTop : window.scrollY;
      if (top >= SHOW_AT) { target.current = el; setShow(true); return; }
      if (target.current !== el) return;
      // 표 안을 다 올렸어도 페이지가 내려가 있으면 페이지 기준으로 남긴다
      if (el && window.scrollY >= SHOW_AT) target.current = null;
      else setShow(false);
    };
    document.addEventListener('scroll', onScroll, { capture: true, passive: true });
    return () => document.removeEventListener('scroll', onScroll, { capture: true });
  }, []);

  if (!show) return null;
  const up = () => {
    const el = target.current;
    if (el) el.scrollTo({ top: 0, behavior: 'smooth' });
    // 표 안 스크롤이 페이지 아래쪽에 있으면 페이지도 함께 올린다
    window.scrollTo({ top: 0, behavior: 'smooth' });
  };
  return (
    <button type="button" className="scroll-top" onClick={up} aria-label="맨 위로" title="맨 위로">
      <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
        <path d="M5 4h14" /><path d="M12 20V9" /><path d="M7 13l5-5 5 5" />
      </svg>
    </button>
  );
}
