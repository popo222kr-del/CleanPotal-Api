import { useEffect, useState, useCallback } from 'react';
import { api } from '../api/client';
import type { EducationPlan as E } from '../api/types';
import './EduDashboard.css';

const STATUSES = ['대기', '신청완료', '취소', '진행', '완료'];
const nowY = new Date().getFullYear();
const YEARS = [nowY + 1, nowY, nowY - 1, nowY - 2];
const blank = () => ({ memberName: '', courseName: '', startDate: '', endDate: '', status: '대기', progress: 0, eduMethod: '', attachmentPath: '' });

export default function EduDashboard() {
  const [year, setYear] = useState<number | ''>('');
  const [status, setStatus] = useState('전체');
  const [search, setSearch] = useState('');
  const [all, setAll] = useState<E[]>([]);
  const [edit, setEdit] = useState<E | 'new' | null>(null);
  const [form, setForm] = useState(blank());

  const load = useCallback(async () => {
    const p = new URLSearchParams();
    if (year !== '') p.set('year', String(year));
    if (search) p.set('search', search);
    setAll(await api.get<E[]>(`/api/education?${p.toString()}`));
  }, [year, search]);
  useEffect(() => { load(); }, [load]);

  const counts: Record<string, number> = { 전체: all.length };
  STATUSES.forEach(s => counts[s] = all.filter(e => e.status === s).length);
  const list = status === '전체' ? all : all.filter(e => e.status === status);

  function openNew() { setEdit('new'); setForm(blank()); }
  function openEdit(e: E) { setEdit(e); setForm({ ...e, startDate: e.startDate ?? '', endDate: e.endDate ?? '' }); }
  async function save() {
    const body = { ...form, startDate: form.startDate || null, endDate: form.endDate || null };
    if (edit === 'new') await api.post('/api/education', body);
    else if (edit) await api.put(`/api/education/${edit.id}`, body);
    setEdit(null); load();
  }
  async function del(id: number) {
    if (!confirm('이 교육 일정을 삭제할까요? (근무표의 교육 표시도 함께 제거됩니다)')) return;
    await api.del(`/api/education/${id}`); setEdit(null); load();
  }

  return (
    <div>
      <header className="pg-header">
        <div><h2>🎓 교육 현황 대시보드</h2></div>
        <button className="btn btn-primary" onClick={openNew}>+ 교육 등록</button>
      </header>
      <div className="pg-body">
        <div className="ed-cards">
          {['전체', ...STATUSES].map(s => (
            <button key={s} className={`ed-card ${status === s ? 'active' : ''} s-${s}`} onClick={() => setStatus(s)}>
              <div className="ed-card-n">{counts[s] ?? 0}</div>
              <div className="ed-card-l">{s}</div>
            </button>
          ))}
        </div>

        <div className="ed-filters">
          <select value={year} onChange={e => setYear(e.target.value === '' ? '' : Number(e.target.value))}>
            <option value="">전체 연도</option>
            {YEARS.map(y => <option key={y} value={y}>{y}년</option>)}
          </select>
          <input className="input ed-search" placeholder="이름/교육명 검색" value={search} onChange={e => setSearch(e.target.value)} />
          <span className="ed-count">{list.length}건</span>
        </div>

        <div className="pm-tablewrap">
          <table className="pm-table">
            <thead><tr><th>이름</th><th>교육명</th><th>시작</th><th>종료</th><th>상태</th><th>진행률</th><th>방법</th><th>첨부</th></tr></thead>
            <tbody>
              {list.length === 0 && <tr><td colSpan={8} className="pm-empty">교육 일정이 없습니다</td></tr>}
              {list.map(e => (
                <tr key={e.id} className="pm-row" onClick={() => openEdit(e)}>
                  <td><b>{e.memberName}</b></td>
                  <td>{e.courseName}</td>
                  <td>{e.startDate ?? '-'}</td>
                  <td>{e.endDate ?? '-'}</td>
                  <td><span className={`ed-chip s-${e.status}`}>{e.status}</span></td>
                  <td><div className="ed-bar"><span style={{ width: `${Math.min(100, e.progress)}%` }} /></div>{e.progress}%</td>
                  <td>{e.eduMethod || '-'}</td>
                  <td>{e.attachmentPath ? '📎' : '-'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {edit && (
          <div className="pm-modal-bg" onClick={() => setEdit(null)}>
            <div className="pm-modal" onClick={ev => ev.stopPropagation()}>
              <div className="pm-modal-head"><h3>{edit === 'new' ? '교육 등록' : '교육 수정'}</h3><button className="pm-x" onClick={() => setEdit(null)}>✕</button></div>
              <div className="pm-modal-body">
                <FF l="이름"><input className="input" value={form.memberName} onChange={e => setForm({ ...form, memberName: e.target.value })} /></FF>
                <FF l="교육명"><input className="input" value={form.courseName} onChange={e => setForm({ ...form, courseName: e.target.value })} /></FF>
                <div className="ed-grid">
                  <FF l="시작일"><input className="input" type="date" value={form.startDate} onChange={e => setForm({ ...form, startDate: e.target.value })} /></FF>
                  <FF l="종료일"><input className="input" type="date" value={form.endDate} onChange={e => setForm({ ...form, endDate: e.target.value })} /></FF>
                  <FF l="상태"><select className="input" value={form.status} onChange={e => setForm({ ...form, status: e.target.value })}>{STATUSES.map(s => <option key={s}>{s}</option>)}</select></FF>
                  <FF l="진행률(%)"><input className="input" type="number" min={0} max={100} value={form.progress} onChange={e => setForm({ ...form, progress: Number(e.target.value) })} /></FF>
                  <FF l="교육 방법"><input className="input" value={form.eduMethod} onChange={e => setForm({ ...form, eduMethod: e.target.value })} /></FF>
                  <FF l="첨부 경로"><input className="input" value={form.attachmentPath} onChange={e => setForm({ ...form, attachmentPath: e.target.value })} /></FF>
                </div>
                <p className="ed-hint">💡 저장 시 해당 인원의 근무표에 교육 기간이 "교육"으로 자동 반영됩니다.</p>
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
