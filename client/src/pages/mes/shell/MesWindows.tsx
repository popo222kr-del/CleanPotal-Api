import { useCallback, useMemo, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import {
  CurrentMesWindowContext, MesWindowsContext,
} from './windowTypes';
import type { MesWindowArgs, MesWindowBounds, MesWindowKey, MesWindowState, MesWindowsApi } from './windowTypes';

/**
 * MES 창 관리 — 데스크톱 MES Client 처럼 화면을 "창" 으로 띄운다.
 *
 * 사이드바에는 Dash Board 와 OPER 만 두고, 나머지 화면은 상단 메뉴에서 창으로 연다.
 * 작업자는 OPER 화면을 띄워 둔 채 LOT 현황이나 성적서를 열어 보고 그대로 돌아온다 —
 * 화면을 갈아타면 고르던 LOT 과 입력하던 검사값이 사라지기 때문에, 데스크톱에서 그렇게 써 왔다.
 */
const DEFAULT_WIDTH = 1080;
const DEFAULT_HEIGHT = 620;
/** 창이 겹쳐 열릴 때 조금씩 밀어 놓는 간격 — 완전히 겹치면 뒤 창이 있는지도 모른다. */
const CASCADE = 26;

export function MesWindowsProvider({ children }: { children: ReactNode }) {
  const [windows, setWindows] = useState<MesWindowState[]>([]);
  const nextId = useRef(1);
  const nextZ = useRef(1);

  const focus = useCallback((id: number) => {
    setWindows(list => list.map(w => (w.id === id
      ? { ...w, z: ++nextZ.current, minimized: false }
      : w)));
  }, []);

  const open = useCallback((key: MesWindowKey, title: string, args: MesWindowArgs = {}) => {
    setWindows(list => {
      // 같은 화면이 이미 떠 있으면 새로 만들지 않고 그 창을 앞으로 가져온다(데스크톱과 같다).
      const existing = list.find(w => w.key === key);
      if (existing) {
        return list.map(w => (w.id === existing.id
          ? { ...w, args, nonce: w.nonce + 1, z: ++nextZ.current, minimized: false }
          : w));
      }

      const index = list.length;
      const id = nextId.current++;
      return [...list, {
        id, key, title, args, nonce: 0,
        x: 24 + (index % 6) * CASCADE,
        y: 16 + (index % 6) * CASCADE,
        width: DEFAULT_WIDTH,
        height: DEFAULT_HEIGHT,
        z: ++nextZ.current,
        maximized: false,
        minimized: false,
      }];
    });
  }, []);

  const close = useCallback((id: number) => {
    setWindows(list => list.filter(w => w.id !== id));
  }, []);

  const move = useCallback((id: number, bounds: MesWindowBounds) => {
    setWindows(list => list.map(w => (w.id === id ? { ...w, ...bounds } : w)));
  }, []);

  const toggleMaximize = useCallback((id: number) => {
    setWindows(list => list.map(w => (w.id === id
      ? { ...w, maximized: !w.maximized, minimized: false, z: ++nextZ.current }
      : w)));
  }, []);

  const minimize = useCallback((id: number) => {
    setWindows(list => list.map(w => (w.id === id ? { ...w, minimized: true } : w)));
  }, []);

  const api = useMemo<MesWindowsApi>(
    () => ({ windows, open, close, focus, move, toggleMaximize, minimize }),
    [windows, open, close, focus, move, toggleMaximize, minimize],
  );

  return <MesWindowsContext.Provider value={api}>{children}</MesWindowsContext.Provider>;
}

export function CurrentMesWindowProvider(
  { id, close, children }: { id: number; close: () => void; children: ReactNode },
) {
  const value = useMemo(() => ({ id, close }), [id, close]);
  return <CurrentMesWindowContext.Provider value={value}>{children}</CurrentMesWindowContext.Provider>;
}
