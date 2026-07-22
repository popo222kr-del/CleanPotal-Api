import { useEffect, useState } from 'react';
import { useAccess } from '../auth/useAccess';
import { api } from '../api/client';
import './ProdReqOptions.css';

// 생산팀 요청사항 — 분류 관리 전용 페이지 (구분/세부 위치/요청 분류를 한눈에 편집)
type ReqCategory = { name: string; subs: string[] };
type ReqOptions = { categories: ReqCategory[]; reqTypes: string[] };

function AddRow({ placeholder, onAdd }: { placeholder: string; onAdd: (v: string) => void }) {
  const [v, setV] = useState('');
  function submit() {
    const t = v.trim();
    if (!t) return;
    onAdd(t);
    setV('');
  }
  return (
    <div className="po-add">
      <input className="input" value={v} placeholder={placeholder}
        onChange={e => setV(e.target.value)}
        onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); submit(); } }} />
      <button type="button" className="po-add-btn" onClick={submit}>추가</button>
    </div>
  );
}

export default function ProdReqOptions() {
  const { canEditHandover: canEdit } = useAccess();
  const [opts, setOpts] = useState<ReqOptions>({ categories: [], reqTypes: [] });
  const [base, setBase] = useState('');
  const [saving, setSaving] = useState(false);
  const dirty = base !== '' && JSON.stringify(opts) !== base;

  useEffect(() => {
    api.get<ReqOptions>('/api/prodreq/options').then(o => {
      setOpts(o);
      setBase(JSON.stringify(o));
    }).catch(() => alert('분류 정보를 불러오지 못했습니다.'));
  }, []);

  // 미저장 이탈 경고 (새로고침/창 닫기)
  useEffect(() => {
    const h = (e: BeforeUnloadEvent) => { if (dirty) { e.preventDefault(); e.returnValue = ''; } };
    window.addEventListener('beforeunload', h);
    return () => window.removeEventListener('beforeunload', h);
  }, [dirty]);

  async function save() {
    if (!canEdit || saving) return;
    setSaving(true);
    try {
      const saved = await api.put<ReqOptions>('/api/prodreq/options', opts);
      setOpts(saved);
      setBase(JSON.stringify(saved));
    } catch (err) {
      alert(err instanceof Error ? err.message : '저장에 실패했습니다.');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div>
      <header className="pg-header">
        <div>
          <h2>요청 분류 관리</h2>
          <p>구분·세부 위치·요청 분류를 편집합니다. 저장하면 '새 요청 등록'의 선택지에 바로 반영됩니다.</p>
        </div>
        {canEdit && (
          <button className="btn btn-primary" onClick={save} disabled={saving || !dirty}>
            {saving ? '저장 중...' : dirty ? '변경사항 저장' : '저장됨'}
          </button>
        )}
      </header>

      <div className="pg-body">
        <p className="po-hint">이미 등록된 요청은 문자열로 저장되어 있어 여기서 삭제해도 영향받지 않습니다.</p>

        {/* ── 구분 & 세부 위치: 구분마다 카드 — 전체가 한눈에 ── */}
        <h3 className="po-sec-t">구분 & 세부 위치</h3>
        <div className="po-grid">
          {opts.categories.map((c, ci) => (
            <div key={ci} className="po-card">
              <div className="po-card-h">
                <b>{c.name}</b>
                <span className="po-cnt">{c.subs.length}</span>
                {canEdit && (
                  <button type="button" className="po-x" title="구분 삭제"
                    onClick={() => {
                      if (!confirm(`'${c.name}' 구분과 세부 위치 ${c.subs.length}개를 삭제할까요?`)) return;
                      setOpts(o => ({ ...o, categories: o.categories.filter((_, j) => j !== ci) }));
                    }}>✕</button>
                )}
              </div>
              <div className="po-subs">
                {c.subs.map((s, si) => (
                  <span key={si} className="po-chip">
                    {s}
                    {canEdit && (
                      <button type="button" className="po-chip-x" title="삭제"
                        onClick={() => setOpts(o => ({
                          ...o,
                          categories: o.categories.map((x, j) => j === ci ? { ...x, subs: x.subs.filter((_, k) => k !== si) } : x),
                        }))}>✕</button>
                    )}
                  </span>
                ))}
                {c.subs.length === 0 && <span className="po-empty">세부 위치 없음</span>}
              </div>
              {canEdit && (
                <AddRow placeholder="세부 위치 추가" onAdd={v =>
                  setOpts(o => ({
                    ...o,
                    categories: o.categories.map((x, j) =>
                      j === ci && !x.subs.includes(v) ? { ...x, subs: [...x.subs, v] } : x),
                  }))} />
              )}
            </div>
          ))}
          {canEdit && (
            <div className="po-card po-card-new">
              <div className="po-card-h"><b>새 구분 추가</b></div>
              <p className="po-empty">구분을 추가한 뒤 카드에서 세부 위치를 입력하세요.</p>
              <AddRow placeholder="새 구분 (예: 포장실)" onAdd={v =>
                setOpts(o => o.categories.some(c => c.name === v) ? o
                  : { ...o, categories: [...o.categories, { name: v, subs: [] }] })} />
            </div>
          )}
        </div>

        {/* ── 요청 분류 ── */}
        <h3 className="po-sec-t">요청 분류</h3>
        <div className="po-card po-card-wide">
          <div className="po-subs">
            {opts.reqTypes.map((t, i) => (
              <span key={i} className="po-chip">
                {t}
                {canEdit && (
                  <button type="button" className="po-chip-x" title="삭제"
                    onClick={() => setOpts(o => ({ ...o, reqTypes: o.reqTypes.filter((_, j) => j !== i) }))}>✕</button>
                )}
              </span>
            ))}
            {opts.reqTypes.length === 0 && <span className="po-empty">요청 분류 없음</span>}
          </div>
          {canEdit && (
            <AddRow placeholder="요청 분류 추가 (예: 안전)" onAdd={v =>
              setOpts(o => o.reqTypes.includes(v) ? o : { ...o, reqTypes: [...o.reqTypes, v] })} />
          )}
        </div>
      </div>
    </div>
  );
}
