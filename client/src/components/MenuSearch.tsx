import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';

/**
 * 사이드바 메뉴 검색 — 메뉴를 펼쳐 찾지 않고 이름으로 바로 간다.
 *
 * - 이 계정이 볼 수 있는 메뉴만 찾는다(권한·숨긴 메뉴는 Layout 이 걸러서 넘긴다).
 * - 띄어쓰기·대소문자를 무시하고, 초성으로도 찾는다(예: "ㅇㅊ" → 업체 관리·업체 견적서).
 * - ↑↓ 로 고르고 Enter 로 이동, Esc 로 지운다. 어디서든 Ctrl+K 로 검색칸에 들어온다.
 */
export type MenuEntry = { to: string; label: string; group: string };

const CHO = ['ㄱ', 'ㄲ', 'ㄴ', 'ㄷ', 'ㄸ', 'ㄹ', 'ㅁ', 'ㅂ', 'ㅃ', 'ㅅ', 'ㅆ', 'ㅇ', 'ㅈ', 'ㅉ', 'ㅊ', 'ㅋ', 'ㅌ', 'ㅍ', 'ㅎ'];
/** 한글 음절을 초성으로 바꾼다(한글이 아니면 그대로). */
const chosung = (s: string) => [...s].map(c => {
  const code = c.charCodeAt(0) - 0xAC00;
  return code >= 0 && code < 11172 ? CHO[Math.floor(code / 588)] : c;
}).join('');
const norm = (s: string) => s.replace(/\s+/g, '').toLowerCase();
const onlyCho = (s: string) => /^[ㄱ-ㅎ]+$/.test(s);

function score(e: MenuEntry, q: string): number {
  const label = norm(e.label);
  const all = norm(e.group + e.label);
  if (label.startsWith(q)) return 0;
  if (label.includes(q)) return 1;
  if (all.includes(q)) return 2;                       // 묶음 이름으로도(예: "mes")
  if (onlyCho(q)) {
    if (chosung(label).startsWith(q)) return 3;
    if (chosung(label).includes(q)) return 4;
  }
  return -1;
}

export default function MenuSearch({ entries, onGo }: { entries: MenuEntry[]; onGo?: () => void }) {
  const nav = useNavigate();
  const [q, setQ] = useState('');
  const [sel, setSel] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);

  const key = norm(q);
  const hits = useMemo(() => {
    if (!key) return [];
    return entries
      .map(e => ({ e, s: score(e, key) }))
      .filter(x => x.s >= 0)
      .sort((a, b) => a.s - b.s)
      .map(x => x.e);
  }, [entries, key]);

  useEffect(() => { setSel(0); }, [key]);

  // Ctrl+K(맥은 ⌘K) — 어느 화면에서든 메뉴 검색칸으로(화면에 표시는 하지 않는다)
  useEffect(() => {
    const h = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        inputRef.current?.focus();
        inputRef.current?.select();
      }
    };
    window.addEventListener('keydown', h);
    return () => window.removeEventListener('keydown', h);
  }, []);

  function go(e: MenuEntry) {
    nav(e.to);
    setQ('');
    inputRef.current?.blur();
    onGo?.();
  }

  return (
    <div className="ms-wrap">
      <div className="ms-box">
        <svg className="ms-ico" viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
          <circle cx="11" cy="11" r="6.5" /><path d="m16 16 4.5 4.5" />
        </svg>
        <input ref={inputRef} className="ms-input" value={q} placeholder="메뉴 검색" aria-label="메뉴 검색"
          onChange={e => setQ(e.target.value)}
          onKeyDown={e => {
            if (e.key === 'ArrowDown') { e.preventDefault(); setSel(s => Math.min(s + 1, hits.length - 1)); }
            else if (e.key === 'ArrowUp') { e.preventDefault(); setSel(s => Math.max(s - 1, 0)); }
            else if (e.key === 'Enter' && hits[sel]) { e.preventDefault(); go(hits[sel]); }
            else if (e.key === 'Escape') { setQ(''); inputRef.current?.blur(); }
          }} />
        {q && <button type="button" className="ms-clear" aria-label="지우기" onClick={() => { setQ(''); inputRef.current?.focus(); }}>×</button>}
      </div>
      {key && (
        <div className="ms-results" role="listbox">
          {hits.length === 0 && <div className="ms-empty">찾는 메뉴가 없습니다</div>}
          {hits.map((e, i) => (
            <button key={e.to} type="button" role="option" aria-selected={i === sel}
              className={`ms-hit ${i === sel ? 'on' : ''}`}
              onMouseEnter={() => setSel(i)}
              // 검색칸에서 포커스가 빠지기 전에 이동(클릭이 blur 에 묻히지 않게)
              onMouseDown={ev => { ev.preventDefault(); go(e); }}>
              <span className="ms-label">{e.label}</span>
              <span className="ms-group">{e.group}</span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
