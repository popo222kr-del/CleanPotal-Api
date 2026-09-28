import { useEffect, useState } from 'react';
import { CARDS, DEFAULT_LAYOUT, orderOf, type CardGroup, type DashLayout } from './layout';

/**
 * 대시보드 카드 구성 창 — 카드마다 켜고 끄고(체크), 위아래로 순서를 바꾼다.
 * 이 계정 권한으로 볼 수 있는 카드만 나온다. 저장하면 PC·폰 모두 같은 구성이 된다.
 */
export default function DashboardEditor({ layout, available, onSave, onClose }: {
  layout: DashLayout; available: Set<string>;
  onSave: (l: DashLayout) => Promise<void>; onClose: () => void;
}) {
  const [draft, setDraft] = useState<DashLayout>(layout);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  function toggle(key: string) {
    setDraft(d => ({ ...d, hidden: d.hidden.includes(key) ? d.hidden.filter(k => k !== key) : [...d.hidden, key] }));
  }
  function move(group: CardGroup, key: string, delta: number) {
    setDraft(d => {
      const list = orderOf(group, d);
      const i = list.indexOf(key);
      const j = i + delta;
      if (i < 0 || j < 0 || j >= list.length) return d;
      [list[i], list[j]] = [list[j], list[i]];
      const other: CardGroup = group === 'top' ? 'site' : 'top';
      return { ...d, order: group === 'top' ? [...list, ...orderOf(other, d)] : [...orderOf(other, d), ...list] };
    });
  }
  async function save() {
    setSaving(true);
    try { await onSave(draft); onClose(); }
    catch (err) { alert(err instanceof Error ? err.message : '저장하지 못했습니다.'); }
    finally { setSaving(false); }
  }

  const section = (group: CardGroup, title: string) => {
    // 권한이 없는 카드는 켜도 나오지 않으므로 목록에서도 뺀다. 순서는 보이는 카드끼리만 바꾼다.
    const keys = orderOf(group, draft).filter(k => available.has(k));
    if (keys.length === 0) return null;
    return (
      <div className="db-ed-sec">
        <h4>{title}</h4>
        {keys.map((k, i) => {
          const c = CARDS.find(x => x.key === k)!;
          const on = !draft.hidden.includes(k);
          return (
            <div key={k} className={`db-ed-row ${on ? '' : 'off'}`}>
              <label>
                <input type="checkbox" checked={on} onChange={() => toggle(k)} />
                <span><b>{c.label}</b><em>{c.hint}</em></span>
              </label>
              <span className="db-ed-move">
                <button type="button" aria-label="위로" disabled={i === 0} onClick={() => move(group, k, -1)}>▲</button>
                <button type="button" aria-label="아래로" disabled={i === keys.length - 1} onClick={() => move(group, k, 1)}>▼</button>
              </span>
            </div>
          );
        })}
      </div>
    );
  };

  return (
    <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="modal-box db-ed" role="dialog" aria-label="대시보드 구성">
        <h3>대시보드 구성</h3>
        <p className="db-ed-hint">보고 싶은 카드만 켜고 순서를 정하세요. 내 계정에 저장되어 PC·휴대폰 모두 같게 보입니다.</p>
        {section('top', '위쪽 카드')}
        {section('site', '현장 현황 칸')}
        <p className="db-ed-note">맨 위 이상 알림(출고일 지남·미조치 NG 등)은 놓치면 안 되므로 항상 보입니다.</p>
        <div className="modal-actions">
          <button type="button" className="btn btn-ghost db-ed-reset" onClick={() => setDraft(DEFAULT_LAYOUT)}>기본값으로</button>
          <button type="button" className="btn btn-ghost" onClick={onClose}>취소</button>
          <button type="button" className="btn btn-primary" onClick={save} disabled={saving}>{saving ? '저장 중...' : '저장'}</button>
        </div>
      </div>
    </div>
  );
}
