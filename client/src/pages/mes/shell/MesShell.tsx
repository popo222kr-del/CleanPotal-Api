import { useEffect, useRef, useState } from 'react';
import { Outlet } from 'react-router-dom';
import { useMesWindows } from './windowTypes';
import type { MesWindowKey } from './windowTypes';
import { MES_MENUS, MES_WINDOW_TITLES } from './windowRegistry';
import { useAccess } from '../../../auth/useAccess';
import './MesShell.css';
import '../Mes.css';

/**
 * MES 셸 — 상단 메뉴바 + 지금 보고 있는 화면(Dash Board 또는 OPER).
 *
 * 데스크톱 MES Client 의 배치를 그대로 옮긴 것이다. 사이드바에는 Dash Board 와 OPER 만 두고,
 * 나머지 화면은 메뉴에서 <b>새 창</b>으로 연다 — 창이라 본 화면 밖으로 끌어낼 수 있고,
 * OPER 에서 LOT 을 고르고 검사값을 넣던 상태를 그대로 둔 채 다른 화면을 볼 수 있다.
 *
 * 창을 쥐고 있는 쪽은 포털 레이아웃(Layout)이다. 여기서 쥐고 있으면 사이드바로 MES 를 벗어나는
 * 순간 창이 닫혀 버린다.
 */
export default function MesShell() {
  return (
    <div className="mes-shell">
      <MesMenuBar />
      <div className="mes-shell-body"><Outlet /></div>
      <OpenWindowBar />
    </div>
  );
}

function MesMenuBar() {
  const windows = useMesWindows();
  const acc = useAccess();
  // 사이드바와 같은 숨김 설정을 따른다 — 예전에는 MES 상단 메뉴가 숨김을 보지 않았다.
  const menus = MES_MENUS
    .map(m => ({ ...m, items: m.items.filter(key => !acc.isHidden(`/mes/${key}`)) }))
    .filter(m => m.items.length > 0);
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

  // 창은 이 클릭 처리 안에서 바로 열린다 — 한 박자 늦으면 브라우저가 팝업으로 보고 막는다.
  function pick(key: MesWindowKey) {
    setOpenMenu(null);
    windows?.open(key, MES_WINDOW_TITLES[key]);
  }

  return (
    <>
      <div className="mes-menubar" ref={barRef}>
        {menus.map(menu => (
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
        <span className="mes-menubar-hint">화면은 새 창으로 열립니다 — 옮기거나 다른 모니터에 둘 수 있습니다</span>
      </div>

      {windows?.blocked && (
        <div className="mes-popup-blocked">
          브라우저가 새 창을 막았습니다. 주소창 오른쪽의 팝업 차단 아이콘에서 이 사이트를 허용해 주세요.
          <button type="button" onClick={windows.dismissBlocked}>확인</button>
        </div>
      )}
    </>
  );
}

/** 열어 둔 창 목록 — 다른 창에 가려 안 보일 때 눌러서 앞으로 가져온다. */
function OpenWindowBar() {
  const windows = useMesWindows();
  if (!windows || windows.windows.length === 0) return null;

  return (
    <div className="mes-taskbar">
      <span className="mes-taskbar-label">열린 창</span>
      {windows.windows.map(win => (
        <span key={win.id} className="mes-task">
          <button type="button" title="이 창을 앞으로" onClick={() => windows.focus(win.id)}>{win.title}</button>
          <button type="button" className="x" title="닫기" onClick={() => windows.close(win.id)}>✕</button>
        </span>
      ))}
    </div>
  );
}
