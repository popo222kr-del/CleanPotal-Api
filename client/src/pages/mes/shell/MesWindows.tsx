import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import type { ReactNode } from 'react';
import { openMesPopup, prepareMesPopup } from './popupDocument';
import { renderMesWindow } from './windowRegistry';
import { CurrentMesWindowContext, MesWindowsContext } from './windowTypes';
import type { MesWindowArgs, MesWindowKey, MesWindowState, MesWindowsApi } from './windowTypes';

/**
 * MES 창 관리 — 데스크톱 MES Client 처럼 화면을 <b>진짜 새 창</b>으로 띄운다.
 *
 * 브라우저 안에 그린 가짜 창이 아니라 window.open 으로 연 실제 창이다. 그래야 본 화면 밖으로
 * 끌어낼 수 있고 다른 모니터에 올려 둘 수 있다. 최소화·최대화·닫기도 운영체제가 처리한다.
 *
 * 창은 클릭 처리 안에서 <b>바로</b> 연다 — 나중에(렌더 뒤에) 열면 브라우저가 팝업으로 보고 막는다.
 */
const WIDTH = 1180;
const HEIGHT = 760;

export function MesWindowsProvider({ children }: { children: ReactNode }) {
  const [windows, setWindows] = useState<MesWindowState[]>([]);
  const [blocked, setBlocked] = useState(false);
  const nextId = useRef(1);
  /** 창 손잡이와 그 안에 그릴 자리. 렌더 값이 아니라 손잡이라 ref 에 둔다. */
  const popups = useRef(new Map<number, { win: Window; container: HTMLDivElement }>());

  const forget = useCallback((id: number) => {
    popups.current.delete(id);
    setWindows(list => list.filter(w => w.id !== id));
  }, []);

  const close = useCallback((id: number) => {
    popups.current.get(id)?.win.close();
    forget(id);
  }, [forget]);

  const focus = useCallback((id: number) => {
    popups.current.get(id)?.win.focus();
  }, []);

  const open = useCallback((key: MesWindowKey, title: string, args: MesWindowArgs = {}) => {
    // 같은 화면이 이미 떠 있으면 새로 만들지 않고 그 창을 앞으로 가져온다(데스크톱과 같다).
    const existing = windows.find(w => w.key === key);
    const alive = existing ? popups.current.get(existing.id) : undefined;
    if (existing && alive && !alive.win.closed) {
      alive.win.focus();
      setWindows(list => list.map(w => (w.id === existing.id ? { ...w, args, nonce: w.nonce + 1 } : w)));
      return;
    }
    if (existing) { forget(existing.id); }   // 사용자가 이미 닫은 창

    const popup = openMesPopup(`mes-${key}`, WIDTH, HEIGHT);
    if (!popup) { setBlocked(true); return; }

    const id = nextId.current++;
    popups.current.set(id, { win: popup, container: prepareMesPopup(popup, `${title} — 세정 업무 통합 관리`) });
    setBlocked(false);
    setWindows(list => [...list, { id, key, title, args, nonce: 0 }]);
  }, [windows, forget]);

  // 사용자가 창을 직접 닫으면(제목줄 X) 목록에서도 지운다.
  // unload 를 놓치는 브라우저가 있어 주기적으로도 확인한다.
  useEffect(() => {
    if (windows.length === 0) return;
    const timer = window.setInterval(() => {
      for (const [id, handle] of popups.current) {
        if (handle.win.closed) forget(id);
      }
    }, 800);
    return () => window.clearInterval(timer);
  }, [windows.length, forget]);

  // 본 화면을 새로고침하거나 떠나면 열어 둔 창도 같이 닫는다 — 남겨 두면 그리는 쪽이 사라져 멈춘 채로 뜬다.
  useEffect(() => {
    const closeAll = () => { for (const { win } of popups.current.values()) win.close(); };
    window.addEventListener('beforeunload', closeAll);
    return () => {
      window.removeEventListener('beforeunload', closeAll);
      closeAll();
    };
  }, []);

  const api = useMemo<MesWindowsApi>(
    () => ({ windows, blocked, dismissBlocked: () => setBlocked(false), open, close, focus }),
    [windows, blocked, open, close, focus],
  );

  return (
    <MesWindowsContext.Provider value={api}>
      {children}
      {/* 각 창 안에 그 화면을 그린다. 창은 다른 문서지만 같은 React 나무 안이라
          로그인 정보·권한 같은 것이 그대로 이어진다. */}
      {windows.map(win => {
        const target = popups.current.get(win.id);
        if (!target) return null;
        return createPortal(
          <CurrentMesWindowProvider id={win.id} close={() => close(win.id)}>
            {/* nonce 가 바뀌면 안을 새로 그린다 — 같은 창을 다른 LOT 으로 다시 열었을 때다. */}
            <div key={win.nonce} className="mes-popup-page">{renderMesWindow(win.key, win.args)}</div>
          </CurrentMesWindowProvider>,
          target.container,
          `mes-window-${win.id}`,
        );
      })}
    </MesWindowsContext.Provider>
  );
}

export function CurrentMesWindowProvider(
  { id, close, children }: { id: number; close: () => void; children: ReactNode },
) {
  const value = useMemo(() => ({ id, close }), [id, close]);
  return <CurrentMesWindowContext.Provider value={value}>{children}</CurrentMesWindowContext.Provider>;
}
