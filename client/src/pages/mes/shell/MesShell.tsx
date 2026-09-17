import { useEffect, useRef, useState } from 'react';
import { Outlet } from 'react-router-dom';
import MesWindow from './MesWindow';
import { CurrentMesWindowProvider, MesWindowsProvider } from './MesWindows';
import { useMesWindows } from './windowTypes';
import type { MesWindowKey } from './windowTypes';
import { MES_MENUS, MES_WINDOW_TITLES, renderMesWindow } from './windowRegistry';
import './MesShell.css';
import '../Mes.css';

/**
 * MES 셸 — 상단 메뉴바 + 지금 보고 있는 화면(Dash Board 또는 OPER) + 그 위에 뜬 창들.
 *
 * 데스크톱 MES Client 의 배치를 그대로 옮긴 것이다. 사이드바에는 Dash Board 와 OPER 만 두고,
 * 나머지 화면은 메뉴에서 창으로 연다 — OPER 에서 LOT 을 고르고 검사값을 넣는 도중에 다른 화면으로
 * 갈아타면 그 상태가 사라지기 때문에, 현장에서는 창을 띄워 두고 오가는 방식으로 써 왔다.
 */
export default function MesShell() {
  return (
    <MesWindowsProvider>
      <div className="mes-shell">
        <MesMenuBar />
        <div className="mes-shell-body">
          <div className="mes-shell-page"><Outlet /></div>
          <WindowLayer />
        </div>
        <Taskbar />
      </div>
    </MesWindowsProvider>
  );
}

function MesMenuBar() {
  const windows = useMesWindows();
  const [openMenu, setOpenMenu] = useState<string | null>(null);
  const barRef = useRef<HTMLDivElement>(null);

  // 메뉴 밖을 누르거나 ESC 를 누르면 닫는다(메뉴가 열린 채 남아 화면을 가리지 않게).
  useEffect(() => {
    if (openMenu === null) return;
    const onDown = (e: MouseEvent) => {
      if (!barRef.current?.contains(e.target as Node)) setOpenMenu(null);
    };
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpenMenu(null); };
    document.addEventListener('mousedown', onDown);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDown);
      document.removeEventListener('keydown', onKey);
    };
  }, [openMenu]);

  function pick(key: MesWindowKey) {
    setOpenMenu(null);
    windows?.open(key, MES_WINDOW_TITLES[key]);
  }

  return (
    <div className="mes-menubar" ref={barRef}>
      {MES_MENUS.map(menu => (
        <div key={menu.label} className="mes-menu">
          <button type="button"
                  className={openMenu === menu.label ? 'on' : ''}
                  onClick={() => setOpenMenu(openMenu === menu.label ? null : menu.label)}>
            {menu.label}
          </button>
          {openMenu === menu.label && (
            <div className="mes-menu-drop">
              {menu.items.map(key => (
                <button key={key} type="button" onClick={() => pick(key)}>
                  {MES_WINDOW_TITLES[key]}
                </button>
              ))}
            </div>
          )}
        </div>
      ))}
      <span className="mes-menubar-hint">화면은 창으로 열립니다 — 여러 개를 띄워 두고 오갈 수 있습니다</span>
    </div>
  );
}

function WindowLayer() {
  const windows = useMesWindows();
  if (!windows) return null;

  return (
    <div className="mes-window-layer">
      {windows.windows.map(win => (
        <MesWindow key={win.id}
                   win={win}
                   onFocus={() => windows.focus(win.id)}
                   onClose={() => windows.close(win.id)}
                   onMinimize={() => windows.minimize(win.id)}
                   onToggleMaximize={() => windows.toggleMaximize(win.id)}
                   onMove={bounds => windows.move(win.id, bounds)}>
          <CurrentMesWindowProvider id={win.id} close={() => windows.close(win.id)}>
            {/* nonce 가 바뀌면 안을 새로 그린다 — 같은 창을 다른 LOT 으로 다시 열었을 때다. */}
            <div key={win.nonce} className="mes-win-page">{renderMesWindow(win.key, win.args)}</div>
          </CurrentMesWindowProvider>
        </MesWindow>
      ))}
    </div>
  );
}

function Taskbar() {
  const windows = useMesWindows();
  if (!windows || windows.windows.length === 0) return null;

  return (
    <div className="mes-taskbar">
      <span className="mes-taskbar-label">열린 창</span>
      {windows.windows.map(win => (
        <span key={win.id} className={`mes-task ${win.minimized ? 'off' : ''}`}>
          <button type="button" onClick={() => windows.focus(win.id)}>{win.title}</button>
          <button type="button" className="x" title="닫기" onClick={() => windows.close(win.id)}>✕</button>
        </span>
      ))}
    </div>
  );
}
