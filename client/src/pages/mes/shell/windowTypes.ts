import { createContext, useContext } from 'react';

/**
 * MES 창 관리의 타입과 통로. 화면(창을 여는 쪽)이 쓰는 것은 여기 있는 훅 둘뿐이다.
 * 만드는 쪽(Provider)은 MesWindows.tsx 에 있다 — 화면과 부품을 한 파일에 섞지 않는다.
 */
export type MesWindowKey =
  | 'scan' | 'register' | 'batch' | 'history-void'
  | 'history' | 'cleaning-history' | 'lot-inout' | 'tat' | 'certificates'
  | 'holds' | 'reworks' | 'setup';

/** 창을 열 때 같이 넘기는 값(LOT 번호로 바로 조회하는 식). */
export type MesWindowArgs = { lot?: string };

export type MesWindowState = {
  id: number;
  key: MesWindowKey;
  title: string;
  args: MesWindowArgs;
  /** 같은 창을 다른 값으로 다시 열었을 때 안을 새로 그리기 위한 번호. */
  nonce: number;
  x: number; y: number; width: number; height: number;
  z: number;
  maximized: boolean;
  minimized: boolean;
};

export type MesWindowBounds = Partial<Pick<MesWindowState, 'x' | 'y' | 'width' | 'height'>>;

export type MesWindowsApi = {
  windows: MesWindowState[];
  open: (key: MesWindowKey, title: string, args?: MesWindowArgs) => void;
  close: (id: number) => void;
  focus: (id: number) => void;
  move: (id: number, bounds: MesWindowBounds) => void;
  toggleMaximize: (id: number) => void;
  minimize: (id: number) => void;
};

export const MesWindowsContext = createContext<MesWindowsApi | null>(null);
export const CurrentMesWindowContext = createContext<{ id: number; close: () => void } | null>(null);

/** 창을 여는 쪽(메뉴·목록의 LOT 번호 등)이 쓴다. 셸 밖이면 null 이라 그냥 이동하면 된다. */
export function useMesWindows(): MesWindowsApi | null {
  return useContext(MesWindowsContext);
}

/** 지금 이 화면이 들어 있는 창. 창이 아니라 전체 화면으로 열렸으면 null. */
export function useCurrentMesWindow() {
  return useContext(CurrentMesWindowContext);
}
