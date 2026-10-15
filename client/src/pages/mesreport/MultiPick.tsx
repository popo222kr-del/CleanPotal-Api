import { useEffect, useMemo, useRef, useState } from 'react';
import './MultiPick.css';

// 여러 개 고르기 — 매출 트렌드·출하실적의 업체·라인 필터. 검색 + 체크, [적용]을 눌러야 바뀐다.

export interface OptionItem { key: string; name: string; amount: number }

/** 옆 금액 — 억·만 */
function krw(n: number) {
  const a = Math.abs(n);
  if (a >= 1e8) return `${(n / 1e8).toFixed(a >= 1e10 ? 0 : 2)}억`;
  if (a >= 1e4) return `${Math.round(n / 1e4).toLocaleString()}만`;
  return Math.round(n).toLocaleString();
}

/** 업체·라인 고르기 — 단추를 누르면 검색창과 체크 목록. [적용]을 눌러야 다시 조회한다. */
export default function MultiPick({ label, options, value, names, onApply }: {
  label: string; options: OptionItem[]; value: string[]; names?: Record<string, string>; onApply: (v: string[]) => void;
}) {
  const [open, setOpen] = useState(false);
  const [q, setQ] = useState('');
  const [draft, setDraft] = useState<string[]>(value);
  const box = useRef<HTMLDivElement>(null);
  useEffect(() => { if (open) { setDraft(value); setQ(''); } }, [open]);   // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => {
    if (!open) return;
    const close = (e: MouseEvent) => { if (box.current && !box.current.contains(e.target as Node)) setOpen(false); };
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, [open]);
  const nameOf = (k: string) => options.find(o => o.key === k)?.name ?? names?.[k] ?? k;
  // 이미 고른 것은 목록에 없어도(다른 기간 등) 맨 위에
  const list = useMemo(() => {
    const extra = value.filter(v => !options.some(o => o.key === v)).map(v => ({ key: v, name: nameOf(v), amount: 0 }));
    const t = q.trim().toLowerCase();
    return [...extra, ...options].filter(o => !t || o.name.toLowerCase().includes(t) || o.key.toLowerCase().includes(t));
  }, [options, value, q]);   // eslint-disable-line react-hooks/exhaustive-deps
  const toggle = (k: string) => setDraft(d => (d.includes(k) ? d.filter(x => x !== k) : [...d, k]));
  const text = value.length === 0 ? '전체' : value.length === 1 ? nameOf(value[0]) : `${nameOf(value[0])} 외 ${value.length - 1}`;
  return (
    <div className="tr-pick" ref={box}>
      <button type="button" className={`tr-pick-btn${value.length ? ' on' : ''}`} onClick={() => setOpen(o => !o)} aria-expanded={open}>
        <span className="k">{label}</span><span className="v">{text}</span><span className="c">▾</span>
      </button>
      {open && (
        <div className="tr-pick-pop" role="dialog" aria-label={`${label} 고르기`}>
          <input className="input" autoFocus placeholder={`${label} 검색`} value={q} onChange={e => setQ(e.target.value)} />
          <div className="tr-pick-tools">
            <button type="button" className="btn btn-ghost" onClick={() => setDraft(d => [...new Set([...d, ...list.map(o => o.key)])])}>보이는 것 모두</button>
            <button type="button" className="btn btn-ghost" onClick={() => setDraft([])}>모두 해제</button>
            <span className="mr-dim">{draft.length ? `${draft.length}개 선택` : '전체'}</span>
          </div>
          <div className="tr-pick-list">
            {list.length === 0 && <div className="mr-dim tr-pick-empty">없음</div>}
            {list.map(o => (
              <label key={o.key} className={draft.includes(o.key) ? 'on' : ''}>
                <input type="checkbox" checked={draft.includes(o.key)} onChange={() => toggle(o.key)} />
                <span className="n">{o.name}</span>
                {o.amount > 0 && <span className="a">{krw(o.amount)}</span>}
              </label>
            ))}
          </div>
          <div className="tr-pick-foot">
            <button type="button" className="btn btn-ghost" onClick={() => setOpen(false)}>취소</button>
            <button type="button" className="btn btn-primary" onClick={() => { onApply(draft); setOpen(false); }}>적용</button>
          </div>
        </div>
      )}
    </div>
  );
}
