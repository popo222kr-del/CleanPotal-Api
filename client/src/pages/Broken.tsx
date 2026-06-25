import { useEffect, useState, useCallback } from 'react';
import { api } from '../api/client';
import type { BrokenRecord, BrokenFilterOptions } from '../api/types';
import './Broken.css';

const STATUSES = ['접수', '조치중', '완료'];
const emptyForm = {
  occurDate: new Date().toISOString().slice(0, 10), line: '', productName: '', productType: '',
  sn: '', team: '', causer: '', jobTitle: '', career: '', occurStage: '', description: '',
  status: '접수', isOfficial: true,
};

export default function Broken() {
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
    <div>
      <header className="pg-header">
        <div><h2>🔧 BROKEN 관리</h2><p>파손·불량 발생 기록 및 조치 관리</p></div>
        <button className="btn btn-primary" onClick={openAdd}>+ 등록</button>
      </header>
      <div className="pg-body">
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
            <L l="내용"><textarea className="input ta" value={form.description} onChange={e => setForm({ ...form, description: e.target.value })} /></L>
            <div className="modal-actions">
              <button type="button" className="btn btn-ghost" onClick={() => setModal(false)}>취소</button>
              <button type="submit" className="btn btn-primary">{editId ? '저장' : '등록'}</button>
            </div>
          </form>
        </div>
      )}
    </div>
  );
}

function L({ l, children }: { l: string; children: React.ReactNode }) {
  return <div className="bk-field"><label>{l}</label>{children}</div>;
}
