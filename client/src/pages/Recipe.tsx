import { useEffect, useState, useCallback } from 'react';
import { api } from '../api/client';
import type { Recipe as R } from '../api/types';
import './ProductMaster.css';

type Form = Omit<R, 'id'>;
const blank = (): Form => ({
  text: '', displayText: '', s2Minutes: 0, s2Temperature: 0, hfMinutes: 0, diMinutes: 0,
  totalMinutes: 0, isFavorite: false, orderIndex: 0,
});
const num = (n: number) => (Number.isInteger(n) ? String(n) : n.toFixed(1));

export default function Recipe() {
  const [list, setList] = useState<R[]>([]);
  const [search, setSearch] = useState('');
  const [edit, setEdit] = useState<R | 'new' | null>(null);
  const [form, setForm] = useState<Form>(blank());

  const load = useCallback(async () => {
    setList(await api.get<R[]>(`/api/recipe?search=${encodeURIComponent(search)}`));
  }, [search]);
  useEffect(() => { load(); }, [load]);

  function openNew() { setEdit('new'); setForm(blank()); }
  function openEdit(r: R) { setEdit(r); setForm({ ...r }); }
  async function save() {
    if (edit === 'new') await api.post('/api/recipe', form);
    else if (edit) await api.put(`/api/recipe/${edit.id}`, form);
    setEdit(null); load();
  }
  async function del(id: number) {
    if (!confirm('이 레시피를 삭제할까요?')) return;
    await api.del(`/api/recipe/${id}`); setEdit(null); load();
  }
  async function toggleFav(e: React.MouseEvent, r: R) {
    e.stopPropagation();
    await api.put(`/api/recipe/${r.id}`, { ...r, isFavorite: !r.isFavorite });
    load();
  }

  return (
    <div>
      <header className="pg-header">
        <div><h2>🧪 레시피 관리</h2><p>세정 공정 레시피 (S2 · HF · DI 시간/온도)</p></div>
      </header>
      <div className="pg-body">
        <div className="pm-toolbar">
          <input className="input pm-search" placeholder="레시피 검색" value={search} onChange={e => setSearch(e.target.value)} />
          <span className="pm-count">{list.length}건</span>
          <button className="btn btn-primary" onClick={openNew}>+ 레시피 추가</button>
        </div>
        <div className="pm-tablewrap">
          <table className="pm-table">
            <thead><tr><th style={{ width: 40 }}>★</th><th>레시피</th><th className="r">S2 (분)</th><th className="r">S2 온도</th><th className="r">HF (분)</th><th className="r">DI (분)</th><th className="r">총 (분)</th></tr></thead>
            <tbody>
              {list.length === 0 && <tr><td colSpan={7} className="pm-empty">레시피가 없습니다</td></tr>}
              {list.map(r => (
                <tr key={r.id} className="pm-row" onClick={() => openEdit(r)}>
                  <td style={{ textAlign: 'center' }}>
                    <button className="rc-star" onClick={e => toggleFav(e, r)} title="즐겨찾기">{r.isFavorite ? '★' : '☆'}</button>
                  </td>
                  <td>{r.displayText || r.text || '(이름 없음)'}</td>
                  <td className="r">{num(r.s2Minutes)}</td>
                  <td className="r">{num(r.s2Temperature)}</td>
                  <td className="r">{num(r.hfMinutes)}</td>
                  <td className="r">{num(r.diMinutes)}</td>
                  <td className="r"><b>{num(r.totalMinutes)}</b></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {edit && (
          <div className="pm-modal-bg" onClick={() => setEdit(null)}>
            <div className="pm-modal" onClick={e => e.stopPropagation()}>
              <div className="pm-modal-head"><h3>{edit === 'new' ? '레시피 추가' : '레시피 수정'}</h3><button className="pm-x" onClick={() => setEdit(null)}>✕</button></div>
              <div className="pm-modal-body">
                <FF l="표시명"><input className="input" value={form.displayText} onChange={e => setForm({ ...form, displayText: e.target.value })} /></FF>
                <FF l="원본 텍스트"><input className="input" value={form.text} onChange={e => setForm({ ...form, text: e.target.value })} /></FF>
                <div className="rc-grid">
                  <FF l="S2 시간(분)"><input className="input" type="number" value={form.s2Minutes} onChange={e => setForm({ ...form, s2Minutes: Number(e.target.value) })} /></FF>
                  <FF l="S2 온도"><input className="input" type="number" value={form.s2Temperature} onChange={e => setForm({ ...form, s2Temperature: Number(e.target.value) })} /></FF>
                  <FF l="HF 시간(분)"><input className="input" type="number" value={form.hfMinutes} onChange={e => setForm({ ...form, hfMinutes: Number(e.target.value) })} /></FF>
                  <FF l="DI 시간(분)"><input className="input" type="number" value={form.diMinutes} onChange={e => setForm({ ...form, diMinutes: Number(e.target.value) })} /></FF>
                  <FF l="총 시간(분)"><input className="input" type="number" value={form.totalMinutes} onChange={e => setForm({ ...form, totalMinutes: Number(e.target.value) })} /></FF>
                  <FF l="순서"><input className="input" type="number" value={form.orderIndex} onChange={e => setForm({ ...form, orderIndex: Number(e.target.value) })} /></FF>
                </div>
                <label className="rc-fav"><input type="checkbox" checked={form.isFavorite} onChange={e => setForm({ ...form, isFavorite: e.target.checked })} /> 즐겨찾기</label>
              </div>
              <div className="pm-modal-foot">
                {edit !== 'new' && <button className="btn pm-del" onClick={() => del(edit.id)}>삭제</button>}
                <div style={{ flex: 1 }} />
                <button className="btn btn-ghost" onClick={() => setEdit(null)}>취소</button>
                <button className="btn btn-primary" onClick={save}>저장</button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

function FF({ l, children }: { l: string; children: React.ReactNode }) {
  return <div className="pm-field"><label>{l}</label>{children}</div>;
}
