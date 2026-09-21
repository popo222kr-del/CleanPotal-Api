import { useEffect, useMemo, useRef, useState } from 'react';
import './Combo.css';

// 목록에서 고르되 없는 값은 그냥 적어 넣을 수 있는 입력칸.
//
// <input list> + <datalist> 로도 같은 일이 되지만 그 팝업은 브라우저가 그리는 것이라
// CSS 가 닿지 않는다. 폭이 입력칸과 맞지 않아 옆이 비고, 글꼴도 화면과 따로 논다.
// 그래서 팝업을 직접 그린다 — 폭은 입력칸에 맞추고 모양은 다른 팝업들과 같이 간다.
type Props = {
  value: string;
  onChange: (v: string) => void;
  options: string[];
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  /** 목록이 비었을 때 보여 줄 말 */
  emptyText?: string;
  /** 주면 목록 맨 위에 값을 비우는 줄을 둔다 (예: '(미지정)') */
  clearLabel?: string;
};

export default function Combo({
  value, onChange, options, placeholder, disabled, className, emptyText = '목록이 비어 있습니다', clearLabel,
}: Props) {
  const [open, setOpen] = useState(false);
  // 연 뒤에 글자를 쳤을 때만 목록을 거른다. 화살표로 열면 전부 보여 주는 편이 고르기 쉽다.
  const [filtering, setFiltering] = useState(false);
  const [hi, setHi] = useState(0);
  const wrap = useRef<HTMLDivElement>(null);
  const pop = useRef<HTMLDivElement>(null);

  const hits = useMemo(() => {
    const uniq = [...new Set(options.map(o => (o ?? '').trim()).filter(Boolean))];
    const q = value.trim().toLowerCase();
    return filtering && q ? uniq.filter(o => o.toLowerCase().includes(q)) : uniq;
  }, [options, value, filtering]);

  useEffect(() => {
    if (!open) return;
    function onDown(e: MouseEvent) {
      if (!wrap.current?.contains(e.target as Node)) { setOpen(false); setFiltering(false); }
    }
    document.addEventListener('mousedown', onDown);
    return () => document.removeEventListener('mousedown', onDown);
  }, [open]);

  // 키보드로 옮긴 칸이 팝업 밖에 있으면 따라 내려간다
  useEffect(() => {
    if (!open) return;
    pop.current?.querySelector<HTMLElement>('.cb-item.on')?.scrollIntoView({ block: 'nearest' });
  }, [hi, open]);

  function show(next: boolean) {
    setOpen(next);
    if (next) { setFiltering(false); setHi(0); }
  }
  function pick(v: string) {
    onChange(v);
    setOpen(false);
    setFiltering(false);
  }
  function onKey(e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      if (!open) { show(true); return; }
      setHi(i => Math.min(i + 1, hits.length - 1));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setHi(i => Math.max(i - 1, 0));
    } else if (e.key === 'Enter') {
      if (open && hits[hi]) { e.preventDefault(); pick(hits[hi]); }
    } else if (e.key === 'Escape') {
      if (open) { e.preventDefault(); setOpen(false); setFiltering(false); }
    } else if (e.key === 'Tab') {
      setOpen(false); setFiltering(false);
    }
  }

  return (
    <div className={`cb${className ? ` ${className}` : ''}`} ref={wrap}>
      <input
        className="input cb-input"
        value={value}
        placeholder={placeholder}
        disabled={disabled}
        autoComplete="off"
        onChange={e => { onChange(e.target.value); setFiltering(true); setOpen(true); setHi(0); }}
        onFocus={() => show(true)}
        onKeyDown={onKey}
      />
      <button type="button" className="cb-arrow" tabIndex={-1} disabled={disabled}
        aria-label={open ? '목록 닫기' : '목록 열기'}
        onMouseDown={e => e.preventDefault()}
        onClick={() => show(!open)}>▾</button>
      {open && (
        <div className="cb-pop" ref={pop}>
          {clearLabel && !filtering && (
            <button type="button" className={`cb-item cb-clear${value === '' ? ' sel' : ''}`}
              onMouseDown={e => { e.preventDefault(); pick(''); }}>{clearLabel}</button>
          )}
          {hits.length === 0 && <div className="cb-none">{emptyText}</div>}
          {hits.map((o, i) => (
            <button type="button" key={o}
              className={`cb-item${i === hi ? ' on' : ''}${o === value ? ' sel' : ''}`}
              onMouseEnter={() => setHi(i)}
              onMouseDown={e => { e.preventDefault(); pick(o); }}>
              {o}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
