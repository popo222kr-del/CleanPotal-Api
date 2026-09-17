import { useRef } from 'react';
import type { PointerEvent as ReactPointerEvent, ReactNode } from 'react';
import type { MesWindowState } from './windowTypes';

/**
 * 창 하나의 틀 — 제목줄을 잡아 옮기고, 오른쪽 아래 모서리로 크기를 바꾼다.
 * 좁은 화면(태블릿·휴대폰)에서는 옮기고 키우는 것이 의미가 없어 영역을 가득 채운다(CSS).
 */
const MIN_WIDTH = 420;
const MIN_HEIGHT = 240;

type Props = {
  win: MesWindowState;
  onFocus: () => void;
  onClose: () => void;
  onMinimize: () => void;
  onToggleMaximize: () => void;
  onMove: (bounds: Partial<Pick<MesWindowState, 'x' | 'y' | 'width' | 'height'>>) => void;
  children: ReactNode;
};

export default function MesWindow(
  { win, onFocus, onClose, onMinimize, onToggleMaximize, onMove, children }: Props,
) {
  const start = useRef<{ px: number; py: number; x: number; y: number; w: number; h: number } | null>(null);

  function beginDrag(e: ReactPointerEvent<HTMLElement>) {
    if (win.maximized) return;                      // 최대화 중에는 옮기지 않는다
    if (e.button !== 0) return;
    onFocus();
    start.current = { px: e.clientX, py: e.clientY, x: win.x, y: win.y, w: win.width, h: win.height };
    e.currentTarget.setPointerCapture(e.pointerId);
  }

  function drag(e: ReactPointerEvent<HTMLElement>) {
    const s = start.current;
    if (!s) return;
    onMove({
      // 제목줄이 영역 밖으로 완전히 나가면 다시 잡을 수 없어, 위·왼쪽은 0 에서 멈춘다.
      x: Math.max(0, s.x + (e.clientX - s.px)),
      y: Math.max(0, s.y + (e.clientY - s.py)),
    });
  }

  function beginResize(e: ReactPointerEvent<HTMLElement>) {
    if (win.maximized) return;
    if (e.button !== 0) return;
    e.stopPropagation();
    onFocus();
    start.current = { px: e.clientX, py: e.clientY, x: win.x, y: win.y, w: win.width, h: win.height };
    e.currentTarget.setPointerCapture(e.pointerId);
  }

  function resize(e: ReactPointerEvent<HTMLElement>) {
    const s = start.current;
    if (!s) return;
    onMove({
      width: Math.max(MIN_WIDTH, s.w + (e.clientX - s.px)),
      height: Math.max(MIN_HEIGHT, s.h + (e.clientY - s.py)),
    });
  }

  function end(e: ReactPointerEvent<HTMLElement>) {
    start.current = null;
    if (e.currentTarget.hasPointerCapture(e.pointerId)) e.currentTarget.releasePointerCapture(e.pointerId);
  }

  const style = win.maximized
    ? undefined
    : { left: win.x, top: win.y, width: win.width, height: win.height, zIndex: win.z };

  return (
    <div className={`mes-win ${win.maximized ? 'max' : ''} ${win.minimized ? 'min' : ''}`}
         style={win.maximized ? { zIndex: win.z } : style}
         onPointerDown={onFocus}>
      <header className="mes-win-bar"
              onPointerDown={beginDrag} onPointerMove={drag} onPointerUp={end} onPointerCancel={end}
              onDoubleClick={onToggleMaximize}>
        <span className="mes-win-title">{win.title}</span>
        <div className="mes-win-buttons">
          <button type="button" title="내려놓기" onClick={onMinimize}>ㅡ</button>
          <button type="button" title={win.maximized ? '이전 크기로' : '최대화'} onClick={onToggleMaximize}>
            {win.maximized ? '❐' : '□'}
          </button>
          <button type="button" className="close" title="닫기" onClick={onClose}>✕</button>
        </div>
      </header>

      <div className="mes-win-body">{children}</div>

      {!win.maximized && (
        <span className="mes-win-grip" title="크기 조절"
              onPointerDown={beginResize} onPointerMove={resize} onPointerUp={end} onPointerCancel={end} />
      )}
    </div>
  );
}
