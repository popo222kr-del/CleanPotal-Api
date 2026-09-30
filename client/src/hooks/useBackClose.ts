import { useEffect, useRef, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';

/**
 * 뒤로가기(휴대폰 뒤로 버튼·브라우저 뒤로)는 열린 메뉴 창·팝업부터 닫는다.
 *
 * 예전에는 폰에서 메뉴(더보기)나 팝업을 연 채 뒤로가기를 누르면 창은 그대로 두고 이전 페이지로 넘어가 버렸다.
 * 창이 열리면 같은 주소로 기록을 하나 쌓아 두고(cpOverlay), 뒤로가기로 그 기록이 빠지면 맨 위 창을 닫는다.
 * 창을 버튼으로 닫으면 쌓아 둔 기록도 같이 걷어 낸다(그래야 다음 뒤로가기가 정말 이전 페이지로 간다).
 *
 * 팝업은 화면마다 따로 만들어져 있어, '.modal-bg'(팝업 바탕)가 생기고 사라지는 것을 지켜보고,
 * 닫을 때는 바탕을 누른 것처럼(바깥 클릭) + Esc 를 보낸다. 입력 보호 때문에 바깥 클릭으로 안 닫히는 팝업은
 * 그대로 남고 기록을 다시 쌓는다 — 뒤로가기로 입력하던 내용이 날아가거나 페이지를 떠나지 않게.
 *
 * 반환값 onOverlayEntry: 지금 기록이 창 때문에 쌓은 것인지 — 메뉴 창 안의 링크는 이때 기록을 바꿔치기(replace)해
 * 페이지를 옮긴 뒤 뒤로가기가 같은 페이지를 한 번 더 거치지 않게 한다.
 */
export function useBackClose(drawerOpen: boolean, closeDrawer: () => void): { onOverlayEntry: boolean } {
  const loc = useLocation();
  const nav = useNavigate();
  const [modals, setModals] = useState(0);
  const [pushed, setPushed] = useState<string | null>(null);   // 쌓아 둔 기록이 있는 주소
  const ignorePop = useRef(false);   // 버튼으로 닫아 우리가 기록을 걷어 낸 것 — 창 닫기로 처리하지 않는다
  const pending = useRef(false);     // 기록을 쌓았는데 아직 주소에 반영되기 전
  const onOverlay = !!(loc.state as { cpOverlay?: boolean } | null)?.cpOverlay;

  // 팝업 바탕(.modal-bg) 개수 지켜보기
  useEffect(() => {
    let raf = 0;
    const count = () => { raf = 0; setModals(document.querySelectorAll('.modal-bg').length); };
    const mo = new MutationObserver(() => { if (!raf) raf = requestAnimationFrame(count); });
    mo.observe(document.body, { childList: true, subtree: true });
    count();
    return () => { mo.disconnect(); if (raf) cancelAnimationFrame(raf); };
  }, []);

  const open = drawerOpen || modals > 0;
  const here = loc.pathname + loc.search;

  // 창이 열리면 기록 쌓기 / 버튼으로 닫히면 걷어 내기
  useEffect(() => {
    if (open && pushed === null) {
      pending.current = true;
      nav(here + loc.hash, { state: { cpOverlay: true } });
      setPushed(here);
    } else if (!open && pushed !== null) {
      setPushed(null);
      // 같은 화면에서 닫혔고 지금 기록이 창 기록이면 걷어 낸다(페이지를 옮기며 닫힌 경우는 두는 게 맞다)
      if (pushed === here && onOverlay) { ignorePop.current = true; nav(-1); }
    }
  }, [open, pushed, here, onOverlay, nav, loc.hash]);

  // 뒤로가기로 창 기록이 빠졌다 → 맨 위 창 닫기
  useEffect(() => {
    if (onOverlay) { pending.current = false; return; }
    if (ignorePop.current) { ignorePop.current = false; return; }
    if (pushed === null || pending.current) return;
    setPushed(null);   // 창이 그대로 남으면 위 효과가 기록을 다시 쌓는다
    if (pushed !== here) return;   // 다른 페이지로 옮겨 간 것 — 닫을 창이 없다
    const bgs = document.querySelectorAll<HTMLElement>('.modal-bg');
    const top = bgs[bgs.length - 1];
    if (top) {
      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
      for (const type of ['mousedown', 'mouseup', 'click'])
        top.dispatchEvent(new MouseEvent(type, { bubbles: true, cancelable: true }));
    } else if (drawerOpen) {
      closeDrawer();
    }
  }, [onOverlay, pushed, here, drawerOpen, closeDrawer]);

  return { onOverlayEntry: onOverlay };
}
