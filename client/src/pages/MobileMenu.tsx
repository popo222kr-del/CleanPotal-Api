import { useEffect, useState } from 'react';
import { MOBILE_TAB_CHOICES } from '../components/Layout';
import { DEFAULT_MOBILE_TABS, MAX_MOBILE_TABS, loadMobileTabs, saveMobileTabs, type MobileTab } from '../components/mobileTabs';
import './MobileMenu.css';

// 모바일 하단 메뉴(관리자) — 휴대폰 화면 아래 칸을 모든 사람에게 같게 정한다.
// 맨 앞 '홈' 과 맨 끝 '더보기' 는 고정이고, 그 사이 최대 4칸을 고른다. 칸 이름은 짧게(8자까지) 바꿀 수 있다.
// 각 사람에게는 볼 권한이 있는 칸만 보인다 — 권한이 없는 칸은 그 사람 화면에서 빠진다.

const groups = [...new Set(MOBILE_TAB_CHOICES.map(c => c.group))];
const nameOf = (to: string) => (to === 'qr' ? 'QR 스캔' : MOBILE_TAB_CHOICES.find(c => c.to === to)?.label ?? to);

export default function MobileMenu() {
  const [tabs, setTabs] = useState<MobileTab[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState('');

  useEffect(() => { void loadMobileTabs().then(setTabs); }, []);

  if (!tabs) return <div className="page-loading">불러오는 중…</div>;

  const set = (i: number, p: Partial<MobileTab>) => setTabs(ts => ts!.map((t, j) => (j === i ? { ...t, ...p } : t)));
  const move = (i: number, d: number) => setTabs(ts => {
    const n = [...ts!]; const j = i + d;
    if (j < 0 || j >= n.length) return n;
    [n[i], n[j]] = [n[j], n[i]]; return n;
  });
  const used = new Set(tabs.map(t => t.to));

  async function save(next: MobileTab[] | null) {
    setBusy(true); setMsg('');
    try {
      await saveMobileTabs(next ? next.map(t => ({ to: t.to, ...(t.label?.trim() ? { label: t.label.trim() } : {}) })) : null);
      if (!next) setTabs(DEFAULT_MOBILE_TABS);
      setMsg('저장했습니다. 휴대폰 화면 아래 메뉴에 바로 반영됩니다.');
    } catch (e) {
      setMsg(e instanceof Error ? e.message : '저장하지 못했습니다.');
    } finally { setBusy(false); }
  }

  return (
    <div className="mm-page">
      <header className="pg-header">
        <div>
          <h2>모바일 하단 메뉴</h2>
          <p>휴대폰 화면 아래 메뉴 칸을 정합니다 · 모든 사람에게 같게 보이고, 권한이 없는 칸은 그 사람에게서 빠집니다</p>
        </div>
      </header>
      <div className="pg-body">
        <div className="mm-wrap">
          <section className="mm-card">
            <h3>칸 구성 <em>{tabs.length} / {MAX_MOBILE_TABS}</em></h3>
            <div className="mm-fixed">홈 <span>맨 앞 고정</span></div>
            {tabs.map((t, i) => (
              <div key={i} className="mm-row">
                <span className="mm-no">{i + 1}</span>
                <select className="input" value={t.to} onChange={e => set(i, { to: e.target.value, label: undefined })}>
                  <option value="qr" disabled={used.has('qr') && t.to !== 'qr'}>QR 스캔</option>
                  {groups.map(g => (
                    <optgroup key={g} label={g}>
                      {MOBILE_TAB_CHOICES.filter(c => c.group === g).map(c => (
                        <option key={c.to} value={c.to} disabled={used.has(c.to) && t.to !== c.to}>{c.label}</option>
                      ))}
                    </optgroup>
                  ))}
                </select>
                <input className="input mm-label" maxLength={8} placeholder="칸 이름(짧게)" value={t.label ?? ''}
                       onChange={e => set(i, { label: e.target.value })} title={`비우면 기본 이름: ${nameOf(t.to)}`} />
                <span className="mm-move">
                  <button type="button" onClick={() => move(i, -1)} disabled={i === 0} aria-label="앞으로">▲</button>
                  <button type="button" onClick={() => move(i, 1)} disabled={i === tabs.length - 1} aria-label="뒤로">▼</button>
                  <button type="button" className="del" onClick={() => setTabs(ts => ts!.filter((_, j) => j !== i))} aria-label="빼기">✕</button>
                </span>
              </div>
            ))}
            {tabs.length < MAX_MOBILE_TABS && (
              <button type="button" className="btn btn-ghost mm-add" onClick={() => {
                const first = MOBILE_TAB_CHOICES.find(c => !used.has(c.to));
                if (first) setTabs([...tabs, { to: first.to }]);
              }}>+ 칸 추가</button>
            )}
            <div className="mm-fixed">더보기 <span>맨 끝 고정 · 전체 메뉴</span></div>

            <div className="mm-actions">
              <button type="button" className="btn btn-ghost" disabled={busy} onClick={() => { if (confirm('예전 기본 구성(QR 스캔·일정·요청사항·근무표)으로 되돌릴까요?')) void save(null); }}>기본값으로</button>
              {msg && <span className="mm-msg">{msg}</span>}
              <button type="button" className="btn btn-primary" disabled={busy} onClick={() => void save(tabs)}>{busy ? '저장 중…' : '저장'}</button>
            </div>
          </section>

          {/* 미리 보기 — 휴대폰 아래 메뉴 모양 */}
          <section className="mm-preview">
            <h3>미리 보기</h3>
            <div className="mm-phone">
              <div className="mm-bar">
                <span className="on">홈</span>
                {tabs.map((t, i) => <span key={i}>{t.label?.trim() || nameOf(t.to)}</span>)}
                <span>더보기</span>
              </div>
            </div>
            <p className="mm-note">칸이 많을수록 글자가 작아집니다. 이름은 4~5글자 이내가 보기 좋습니다.</p>
          </section>
        </div>
      </div>
    </div>
  );
}
