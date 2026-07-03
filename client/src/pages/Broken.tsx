import { useEffect, useState, useCallback } from 'react';
import { api } from '../api/client';
import type { BrokenRecord, BrokenFilterOptions, BrokenTraining, BrokenGoal } from '../api/types';
import './Broken.css';

const STATUSES = ['접수', '조치중', '완료'];
const emptyForm = {
  occurDate: new Date().toISOString().slice(0, 10), line: '', productName: '', productType: '',
  sn: '', team: '', causer: '', jobTitle: '', career: '', occurStage: '', description: '',
  status: '접수', isOfficial: true, positionFrozen: false,
};
type Tab = 'records' | 'trainings' | 'goals';

export default function Broken() {
  const [tab, setTab] = useState<Tab>('records');
  return (
    <div>
      <header className="pg-header">
        <div><h2>🔧 BROKEN 관리</h2><p>파손·불량 기록 · 교육 기록 · 교육 목표</p></div>
      </header>
      <div className="pg-body">
        <div className="bk-tabs">
          <button className={tab === 'records' ? 'active' : ''} onClick={() => setTab('records')}>파손 기록</button>
          <button className={tab === 'trainings' ? 'active' : ''} onClick={() => setTab('trainings')}>교육 기록</button>
          <button className={tab === 'goals' ? 'active' : ''} onClick={() => setTab('goals')}>교육 목표 · 메모</button>
        </div>
        {tab === 'records' && <Records />}
        {tab === 'trainings' && <Trainings />}
        {tab === 'goals' && <Goals />}
      </div>
    </div>
  );
}

// ── 파손 기록 ──
function Records() {
  const [items, setItems] = useState<BrokenRecord[]>([]);
  const [opts, setOpts] = useState<BrokenFilterOptions>({ years: [], teams: [], productTypes: [] });
  const [year, setYear] = useState<number | ''>(new Date().getFullYear());
  const [team, setTeam] = useState('전체');
  const [ptype, setPtype] = useState('전체');
  const [official, setOfficial] = useState('전체');
  const [search, setSearch] = useState('');
  const [modal, setModal] = useState(false);
  const [editId, setEditId] = useState<number | null>(null);
  const [form, setForm] = useState(emptyForm);

  const loadOpts = useCallback(async () => setOpts(await api.get<BrokenFilterOptions>('/api/broken/filters')), []);
  const load = useCallback(async () => {
    const p = new URLSearchParams();
    if (year !== '') p.set('year', String(year));
    if (team !== '전체') p.set('team', team);
    if (ptype !== '전체') p.set('productType', ptype);
    if (official !== '전체') p.set('official', official);
    if (search) p.set('search', search);
    setItems(await api.get<BrokenRecord[]>(`/api/broken?${p.toString()}`));
  }, [year, team, ptype, official, search]);

  useEffect(() => { loadOpts(); }, [loadOpts]);
  useEffect(() => { load(); }, [load]);

  function openAdd() { setEditId(null); setForm(emptyForm); setModal(true); }
  function openEdit(b: BrokenRecord) {
    setEditId(b.id);
    setForm({
      occurDate: b.occurDate ?? '', line: b.line, productName: b.productName, productType: b.productType,
      sn: b.sn, team: b.team, causer: b.causer, jobTitle: b.jobTitle, career: b.career,
      occurStage: b.occurStage, description: b.description, status: b.status, isOfficial: b.isOfficial,
      positionFrozen: b.positionFrozen,
    });
    setModal(true);
  }
  async function save(e: React.FormEvent) {
    e.preventDefault();
    const body = { ...form, occurDate: form.occurDate || null };
    if (editId) await api.put(`/api/broken/${editId}`, body);
    else await api.post('/api/broken', body);
    setModal(false); load(); loadOpts();
  }
  async function remove(b: BrokenRecord) {
    if (!confirm('삭제하시겠습니까?')) return;
    await api.del(`/api/broken/${b.id}`); load(); loadOpts();
  }

  return (
    <>
      <div className="bk-filters">
        <select value={year} onChange={e => setYear(e.target.value === '' ? '' : Number(e.target.value))}>
          <option value="">전체 연도</option>
          {opts.years.map(y => <option key={y} value={y}>{y}년</option>)}
        </select>
        <select value={team} onChange={e => setTeam(e.target.value)}>
          <option>전체</option>{opts.teams.map(t => <option key={t}>{t}</option>)}
        </select>
        <select value={ptype} onChange={e => setPtype(e.target.value)}>
          <option>전체</option>{opts.productTypes.map(t => <option key={t}>{t}</option>)}
        </select>
        <select value={official} onChange={e => setOfficial(e.target.value)}>
          <option>전체</option><option>공식</option><option>비공식</option>
        </select>
        <input className="bk-search" placeholder="제품/유발자/SN 검색" value={search} onChange={e => setSearch(e.target.value)} />
        <span className="bk-count">{items.length}건</span>
        <button className="btn btn-primary bk-add" onClick={openAdd}>+ 등록</button>
      </div>

      <div className="bk-table-wrap">
        <table className="bk-table">
          <thead>
            <tr><th>No</th><th>발생일</th><th>라인</th><th>제품</th><th>제품군</th><th>유발자</th><th>팀</th><th>발생단계</th><th>구분</th><th>상태</th><th>관리</th></tr>
          </thead>
          <tbody>
            {items.length === 0 && <tr><td colSpan={11} className="bk-empty">기록이 없습니다</td></tr>}
            {items.map(b => (
              <tr key={b.id}>
                <td>{b.no}</td>
                <td>{b.occurDate ?? '-'}</td>
                <td>{b.line || '-'}</td>
                <td className="bk-prod">{b.productName}{b.sn && <small> ({b.sn})</small>}</td>
                <td>{b.productType || '-'}</td>
                <td>{b.causer}{b.jobTitle && <small className="bk-jt"> {b.jobTitle}</small>}</td>
                <td>{b.team || '-'}</td>
                <td>{b.occurStage || '-'}</td>
                <td><span className={`bk-official ${b.isOfficial ? 'on' : 'off'}`}>{b.isOfficial ? '공식' : '비공식'}</span></td>
                <td><span className={`bk-status s-${b.status}`}>{b.status}</span></td>
                <td><div className="bk-actions"><button className="bk-sm" onClick={() => openEdit(b)}>수정</button><button className="bk-sm danger" onClick={() => remove(b)}>삭제</button></div></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {modal && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setModal(false); }}>
          <form className="modal-box bk-modal" onSubmit={save}>
            <h3>{editId ? 'BROKEN 수정' : 'BROKEN 등록'}</h3>
            <div className="bk-grid">
              <L l="발생일"><input className="input" type="date" value={form.occurDate} onChange={e => setForm({ ...form, occurDate: e.target.value })} /></L>
              <L l="라인"><input className="input" value={form.line} onChange={e => setForm({ ...form, line: e.target.value })} /></L>
              <L l="제품명"><input className="input" required value={form.productName} onChange={e => setForm({ ...form, productName: e.target.value })} /></L>
              <L l="제품군"><input className="input" value={form.productType} onChange={e => setForm({ ...form, productType: e.target.value })} /></L>
              <L l="S/N"><input className="input" value={form.sn} onChange={e => setForm({ ...form, sn: e.target.value })} /></L>
              <L l="팀"><input className="input" value={form.team} onChange={e => setForm({ ...form, team: e.target.value })} placeholder="생산 / 물류" /></L>
              <L l="유발자"><input className="input" value={form.causer} onChange={e => setForm({ ...form, causer: e.target.value })} /></L>
              <L l="직위"><input className="input" value={form.jobTitle} onChange={e => setForm({ ...form, jobTitle: e.target.value })} /></L>
              <L l="경력"><input className="input" value={form.career} onChange={e => setForm({ ...form, career: e.target.value })} /></L>
              <L l="발생단계"><input className="input" value={form.occurStage} onChange={e => setForm({ ...form, occurStage: e.target.value })} /></L>
              <L l="상태"><select className="input" value={form.status} onChange={e => setForm({ ...form, status: e.target.value })}>{STATUSES.map(s => <option key={s}>{s}</option>)}</select></L>
              <L l="구분">
                <div className="bk-radio">
                  <label className={form.isOfficial ? 'on' : ''}><input type="radio" checked={form.isOfficial} onChange={() => setForm({ ...form, isOfficial: true })} /> 공식</label>
                  <label className={!form.isOfficial ? 'on' : ''}><input type="radio" checked={!form.isOfficial} onChange={() => setForm({ ...form, isOfficial: false })} /> 비공식</label>
                </div>
              </L>
            </div>
            <label className="bk-frozen"><input type="checkbox" checked={form.positionFrozen} onChange={e => setForm({ ...form, positionFrozen: e.target.checked })} /> 직위/경력 입력시점 고정</label>
            <L l="내용"><textarea className="input ta" value={form.description} onChange={e => setForm({ ...form, description: e.target.value })} /></L>
            <div className="modal-actions">
              <button type="button" className="btn btn-ghost" onClick={() => setModal(false)}>취소</button>
              <button type="submit" className="btn btn-primary">{editId ? '저장' : '등록'}</button>
            </div>
          </form>
        </div>
      )}
    </>
  );
}

// ── 교육 기록 ──
function Trainings() {
  const [type, setType] = useState('전체');
  const [list, setList] = useState<BrokenTraining[]>([]);
  const [edit, setEdit] = useState<BrokenTraining | 'new' | null>(null);
  const [form, setForm] = useState({ trainingType: 'production', trainingDate: '', content: '' });

  const load = useCallback(async () => {
    const q = type === '전체' ? '' : `?type=${type}`;
    setList(await api.get<BrokenTraining[]>(`/api/broken/trainings${q}`));
  }, [type]);
  useEffect(() => { load(); }, [load]);

  function openNew() { setEdit('new'); setForm({ trainingType: type === '전체' ? 'production' : type, trainingDate: '', content: '' }); }
  function openEdit(t: BrokenTraining) { setEdit(t); setForm({ trainingType: t.trainingType, trainingDate: t.trainingDate ?? '', content: t.content }); }
  async function save() {
    const body = { ...form, trainingDate: form.trainingDate || null };
    if (edit === 'new') await api.post('/api/broken/trainings', body);
    else if (edit) await api.put(`/api/broken/trainings/${edit.id}`, body);
    setEdit(null); load();
  }
  async function del(id: number) {
    if (!confirm('삭제할까요?')) return;
    await api.del(`/api/broken/trainings/${id}`); setEdit(null); load();
  }

  return (
    <>
      <div className="bk-filters">
        <select value={type} onChange={e => setType(e.target.value)}>
          <option value="전체">전체</option><option value="production">생산</option><option value="logistics">물류</option>
        </select>
        <span className="bk-count">{list.length}건</span>
        <button className="btn btn-primary bk-add" onClick={openNew}>+ 교육 추가</button>
      </div>
      <div className="bk-table-wrap">
        <table className="bk-table">
          <thead><tr><th>구분</th><th>일자</th><th>내용</th><th>첨부</th><th>관리</th></tr></thead>
          <tbody>
            {list.length === 0 && <tr><td colSpan={5} className="bk-empty">교육 기록이 없습니다</td></tr>}
            {list.map(t => (
              <tr key={t.id}>
                <td>{t.trainingType === 'logistics' ? '물류' : '생산'}</td>
                <td>{t.trainingDate ?? '-'}</td>
                <td className="bk-prod">{t.content}</td>
                <td>{[t.documents, t.images].filter(x => x && x !== '[]').length ? '있음' : '-'}</td>
                <td><div className="bk-actions"><button className="bk-sm" onClick={() => openEdit(t)}>수정</button><button className="bk-sm danger" onClick={() => del(t.id)}>삭제</button></div></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {edit && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setEdit(null); }}>
          <div className="modal-box bk-modal">
            <h3>{edit === 'new' ? '교육 기록 추가' : '교육 기록 수정'}</h3>
            <div className="bk-grid">
              <L l="구분"><select className="input" value={form.trainingType} onChange={e => setForm({ ...form, trainingType: e.target.value })}><option value="production">생산</option><option value="logistics">물류</option></select></L>
              <L l="일자"><input className="input" type="date" value={form.trainingDate} onChange={e => setForm({ ...form, trainingDate: e.target.value })} /></L>
            </div>
            <L l="내용"><textarea className="input ta" value={form.content} onChange={e => setForm({ ...form, content: e.target.value })} /></L>
            <div className="modal-actions">
              {edit !== 'new' && <button type="button" className="btn danger-btn" onClick={() => del(edit.id)}>삭제</button>}
              <button type="button" className="btn btn-ghost" onClick={() => setEdit(null)}>취소</button>
              <button type="button" className="btn btn-primary" onClick={save}>저장</button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}

// ── 교육 목표 · 메모 ──
function Goals() {
  const [goals, setGoals] = useState<BrokenGoal[]>([]);
  const [memo, setMemo] = useState('');
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    api.get<BrokenGoal[]>('/api/broken/goals').then(setGoals);
    api.get<{ memo: string }>('/api/broken/memo').then(m => setMemo(m.memo));
  }, []);

  function setTarget(i: number, v: string) { setGoals(gs => gs.map((g, idx) => idx === i ? { ...g, target: v } : g)); }
  function addRow(cat: string) {
    const year = new Date().getFullYear();
    setGoals(gs => [...gs, { id: 0, category: cat, year, target: '' }]);
  }
  function delRow(i: number) { setGoals(gs => gs.filter((_, idx) => idx !== i)); }
  async function save() {
    await api.put('/api/broken/goals', goals.map(g => ({ category: g.category, year: g.year, target: g.target })));
    await api.put('/api/broken/memo', { memo });
    setSaved(true); setTimeout(() => setSaved(false), 1500);
  }

  const cats = [{ key: 'production', label: '생산' }, { key: 'logistics', label: '물류' }];
  return (
    <div className="bk-goals">
      {cats.map(c => (
        <div key={c.key} className="bk-goal-sec">
          <div className="bk-goal-h">{c.label} 교육 목표 <button className="bk-sm" onClick={() => addRow(c.key)}>+ 연도</button></div>
          {goals.map((g, i) => g.category === c.key && (
            <div key={i} className="bk-goal-row">
              <input className="input" type="number" style={{ width: 90 }} value={g.year} onChange={e => setGoals(gs => gs.map((x, idx) => idx === i ? { ...x, year: Number(e.target.value) } : x))} />
              <input className="input" placeholder="목표" value={g.target} onChange={e => setTarget(i, e.target.value)} />
              <button className="bk-sm danger" onClick={() => delRow(i)}>✕</button>
            </div>
          ))}
        </div>
      ))}
      <div className="bk-goal-sec">
        <div className="bk-goal-h">메모</div>
        <textarea className="input ta" value={memo} onChange={e => setMemo(e.target.value)} />
      </div>
      <button className="btn btn-primary" onClick={save}>{saved ? '저장됨 ✓' : '저장'}</button>
    </div>
  );
}

function L({ l, children }: { l: string; children: React.ReactNode }) {
  return <div className="bk-field"><label>{l}</label>{children}</div>;
}
