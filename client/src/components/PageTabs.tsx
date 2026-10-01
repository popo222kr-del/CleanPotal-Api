import { useCallback, useEffect, useRef, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import { useAuth } from '../auth/AuthContext';

/**
 * 화면 위 페이지 탭 — 연 화면을 탭으로 남겨 두어 메뉴를 다시 찾지 않고 오간다(PC 전용, 폰은 하단 탭바를 쓴다).
 *
 * - 화면을 열면 탭이 생긴다. 이미 있으면 그 탭으로 간다. 탭은 마지막으로 본 주소(검색 조건 등 ?뒤 포함)를 기억한다.
 * - 📌 고정한 탭은 맨 앞에 붙고 닫기 버튼이 없다. 고정 목록은 계정(/api/me/prefs/tabs)에 저장해 어느 PC 에서든 같다.
 * - 고정하지 않은 탭은 이 브라우저에만 기억한다(다른 PC 에서 연 화면까지 따라오면 오히려 어지럽다). 많아지면 오래된 것부터 닫는다.
 * - 탭을 옮겨 다녀도 화면은 새로 불러온다(입력 중이던 내용은 각 화면의 자동 저장·나가기 확인을 따른다).
 */

type Tab = { path: string; url: string; title: string };
type Pin = { path: string; title: string };

const MAX_OPEN = 12;          // 고정하지 않은 탭 상한
const PREF_KEY = 'tabs';

const readLocal = (key: string): Tab[] => {
  try {
    const v = JSON.parse(localStorage.getItem(key) ?? '[]');
    return Array.isArray(v) ? v.filter(t => t && typeof t.path === 'string' && typeof t.url === 'string') : [];
  } catch { return []; }
};
const writeLocal = (key: string, tabs: Tab[]) => {
  try { localStorage.setItem(key, JSON.stringify(tabs)); } catch { /* 저장 못 해도 탭은 이 화면에서 동작한다 */ }
};
const parsePins = (raw: unknown): Pin[] => {
  const list = (raw as { pinned?: unknown } | null)?.pinned;
  return Array.isArray(list)
    ? list.filter((p): p is Pin => !!p && typeof p.path === 'string' && p.path.startsWith('/') && typeof p.title === 'string').slice(0, 30)
    : [];
};

/** 탭을 만들지 않는 화면 — QR 구역 점검(현장 한 장짜리)·로그인. */
const skip = (path: string) => path.startsWith('/c/') || path.startsWith('/e/') || path === '/login';

export default function PageTabs({ titleOf }: { titleOf: (path: string) => string | undefined }) {
  const { user } = useAuth();
  const loc = useLocation();
  const nav = useNavigate();
  const storeKey = `cp_tabs:${user?.username ?? ''}`;
  const [tabs, setTabs] = useState<Tab[]>(() => readLocal(storeKey));
  const [pins, setPins] = useState<Pin[]>([]);
  const pinsLoaded = useRef(false);

  // 계정이 바뀌면(같은 탭에서 다시 로그인) 그 계정의 탭으로
  useEffect(() => { setTabs(readLocal(storeKey)); }, [storeKey]);
  useEffect(() => { writeLocal(storeKey, tabs); }, [storeKey, tabs]);

  useEffect(() => {
    let alive = true;
    pinsLoaded.current = false;
    api.get<Record<string, unknown>>('/api/me/prefs')
      .then(p => { if (alive) { setPins(parsePins(p[PREF_KEY])); pinsLoaded.current = true; } })
      .catch(() => {});
    return () => { alive = false; };
  }, [user?.username]);

  // 화면을 열 때마다 탭을 만들거나 그 탭의 주소를 갱신한다.
  useEffect(() => {
    const path = loc.pathname;
    if (skip(path)) return;
    const url = path + loc.search;
    const known = titleOf(path);
    setTabs(ts => {
      const i = ts.findIndex(t => t.path === path);
      if (i >= 0) {
        const next = [...ts];
        next[i] = { ...ts[i], url, title: known ?? ts[i].title };
        return next;
      }
      const next = [...ts, { path, url, title: known ?? path }];
      // 너무 많으면 지금 화면·고정 탭을 뺀 가장 오래된 탭부터 닫는다
      const pinned = new Set(pins.map(p => p.path));
      while (next.filter(t => !pinned.has(t.path)).length > MAX_OPEN) {
        const old = next.findIndex(t => t.path !== path && !pinned.has(t.path));
        if (old < 0) break;
        next.splice(old, 1);
      }
      return next;
    });
    if (known) return;
    // 메뉴에 없는 화면(공지·배차표·MES 세부 화면 등)은 화면 제목을 탭 이름으로 쓴다.
    const t = window.setTimeout(() => {
      const h = document.querySelector('.main-content .pg-header h2, .main-content h2')?.textContent?.trim();
      if (h) setTabs(ts => ts.map(x => (x.path === path ? { ...x, title: h } : x)));
    }, 700);
    return () => window.clearTimeout(t);
    // pins 는 상한 계산에만 쓴다 — 고정을 바꿨다고 탭을 다시 만들 일은 없다.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loc.pathname, loc.search, titleOf]);

  const savePins = useCallback((next: Pin[]) => {
    setPins(next);
    api.put(`/api/me/prefs/${PREF_KEY}`, next.length ? { pinned: next } : null)
      .catch(err => alert(err instanceof Error ? err.message : '고정 탭을 저장하지 못했습니다.'));
  }, []);

  // 보이는 순서: 고정 탭(고정한 순서) → 나머지(연 순서)
  const pinnedSet = new Set(pins.map(p => p.path));
  const shown: (Tab & { pinned: boolean })[] = [
    ...pins.map(p => {
      const t = tabs.find(x => x.path === p.path);
      return { path: p.path, url: t?.url ?? p.path, title: titleOf(p.path) ?? t?.title ?? p.title, pinned: true };
    }),
    ...tabs.filter(t => !pinnedSet.has(t.path)).map(t => ({ ...t, pinned: false })),
  ];
  if (shown.length === 0) return null;

  function close(path: string) {
    const idx = shown.findIndex(t => t.path === path);
    setTabs(ts => ts.filter(t => t.path !== path));
    if (path !== loc.pathname) return;
    // 보고 있던 탭을 닫으면 오른쪽(없으면 왼쪽) 탭으로
    const rest = shown.filter(t => t.path !== path);
    const go = rest[Math.min(idx, rest.length - 1)];
    nav(go ? go.url : '/dashboard');
  }
  function togglePin(t: Tab & { pinned: boolean }) {
    if (!pinsLoaded.current && !t.pinned) {
      // 서버 고정 목록을 아직 못 읽었는데 저장하면 다른 PC 에서 고정한 탭을 덮어쓴다.
      alert('고정 탭 목록을 불러오는 중입니다. 잠시 뒤 다시 눌러 주세요.');
      return;
    }
    savePins(t.pinned ? pins.filter(p => p.path !== t.path) : [...pins, { path: t.path, title: t.title }]);
  }
  function closeOthers() {
    // 고정 탭과 지금 보는 탭만 남긴다
    setTabs(ts => ts.filter(t => pinnedSet.has(t.path) || t.path === loc.pathname));
  }

  return (
    <div className="pt-bar" role="tablist" aria-label="연 화면">
      <div className="pt-list" onWheel={e => {
        // 스크롤바를 숨겼으니 마우스 휠(세로)로 탭 목록을 옆으로 넘긴다
        const el = e.currentTarget;
        if (el.scrollWidth > el.clientWidth && Math.abs(e.deltaY) > Math.abs(e.deltaX)) el.scrollLeft += e.deltaY;
      }}>
        {shown.map(t => {
          const active = t.path === loc.pathname;
          return (
            <div key={t.path} role="tab" aria-selected={active}
              className={`pt-tab ${active ? 'on' : ''} ${t.pinned ? 'pinned' : ''}`}
              title={t.title}
              onClick={() => { if (!active) nav(t.url); }}
              // 가운데 버튼 클릭 = 닫기(브라우저 탭과 같게). 고정 탭은 닫지 않는다.
              onAuxClick={e => { if (e.button === 1 && !t.pinned) { e.preventDefault(); close(t.path); } }}>
              <button type="button" className="pt-pin" aria-label={t.pinned ? '고정 풀기' : '고정'}
                title={t.pinned ? '고정 풀기' : '탭 고정(모든 PC 에서 항상 보임)'}
                onClick={e => { e.stopPropagation(); togglePin(t); }}>
                <PinIcon filled={t.pinned} />
              </button>
              <span className="pt-title">{t.title}</span>
              {!t.pinned && (
                <button type="button" className="pt-x" aria-label="탭 닫기" title="닫기"
                  onClick={e => { e.stopPropagation(); close(t.path); }}>×</button>
              )}
            </div>
          );
        })}
      </div>
      {shown.some(t => !t.pinned && t.path !== loc.pathname) && (
        <button type="button" className="pt-closeall" onClick={closeOthers} title="고정 탭과 지금 화면만 남기고 닫기">다른 탭 닫기</button>
      )}
    </div>
  );
}

function PinIcon({ filled }: { filled: boolean }) {
  return (
    <svg viewBox="0 0 24 24" width="13" height="13" fill={filled ? 'currentColor' : 'none'} stroke="currentColor" strokeWidth="1.8" strokeLinejoin="round" strokeLinecap="round">
      <path d="M9 3.5h6l-1 5.5 3.5 3.5v1.5h-11v-1.5L10 9z" />
      <path d="M12 14v6.5" />
    </svg>
  );
}
