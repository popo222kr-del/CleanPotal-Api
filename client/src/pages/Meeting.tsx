import { useEffect, useState, useCallback } from 'react';
import { api } from '../api/client';
import type { ProductionMeetingGroup, ProductionMeeting } from '../api/types';
import './Meeting.css';

const empty = { title: '', meetingDate: new Date().toISOString().slice(0, 10), dayContent: '', nightContent: '', officeMemo: '' };

export default function Meeting() {
  const [groups, setGroups] = useState<ProductionMeetingGroup[]>([]);
  const [sel, setSel] = useState<ProductionMeeting | null>(null);
  const [adding, setAdding] = useState(false);
  const [form, setForm] = useState(empty);

  const load = useCallback(async () => {
    const g = await api.get<ProductionMeetingGroup[]>('/api/productionmeeting');
    setGroups(g);
    return g;
  }, []);
  useEffect(() => { load(); }, [load]);

  function pick(m: ProductionMeeting) {
    setAdding(false); setSel(m);
    setForm({ title: m.title, meetingDate: m.meetingDate, dayContent: m.dayContent, nightContent: m.nightContent, officeMemo: m.officeMemo });
  }
  function startAdd() {
    setAdding(true); setSel(null); setForm(empty);
  }
  async function save(e: React.FormEvent) {
    e.preventDefault();
    if (adding) {
      const created = await api.post<ProductionMeeting>('/api/productionmeeting', form);
      await load(); pick(created);
    } else if (sel) {
      const updated = await api.put<ProductionMeeting>(`/api/productionmeeting/${sel.id}`, form);
      await load(); pick(updated);
    }
  }
  async function remove() {
    if (!sel || !confirm('이 미팅 기록을 삭제할까요?')) return;
    await api.del(`/api/productionmeeting/${sel.id}`);
    setSel(null); setAdding(false); load();
  }

  // 폼의 날짜로 주/야 팀 미리보기 (편집 중 즉시 반영용; 선택된 항목 기준)
  const dayTeam = sel?.dayTeam ?? '';
  const nightTeam = sel?.nightTeam ?? '';
  const showEditor = adding || sel;

  return (
    <div>
      <header className="pg-header">
        <div><h2>🏭 생산미팅</h2><p>주간·야간 팀별 미팅 내용 기록</p></div>
        <button className="btn btn-primary" onClick={startAdd}>+ 새 미팅</button>
      </header>
      <div className="pg-body">
        <div className="mt-layout">
          <div className="mt-list">
            {groups.length === 0 && <div className="mt-no">기록이 없습니다</div>}
            {groups.map(g => (
              <div key={g.monthTitle} className="mt-group">
                <div className="mt-month">{g.monthTitle}</div>
                {g.reports.map(r => (
                  <div key={r.id} className={`mt-item ${sel?.id === r.id ? 'active' : ''}`} onClick={() => pick(r)}>
                    <div className="mt-item-title">{r.title}</div>
                    <div className="mt-item-date">{r.meetingDate}</div>
                  </div>
                ))}
              </div>
            ))}
          </div>

          <div className="mt-editor">
            {!showEditor && <div className="mt-empty"><div style={{ fontSize: 36 }}>🏭</div><p>미팅을 선택하거나 새로 등록하세요</p></div>}
            {showEditor && (
              <form onSubmit={save}>
                <div className="mt-row">
                  <div style={{ flex: 2 }}><label>제목</label><input className="input" value={form.title} placeholder="(비우면 날짜로 자동 생성)" onChange={e => setForm({ ...form, title: e.target.value })} /></div>
                  <div><label>날짜</label><input className="input" type="date" required value={form.meetingDate} onChange={e => setForm({ ...form, meetingDate: e.target.value })} /></div>
                </div>

                <div className="mt-section day">
                  <div className="mt-sec-head"><span className="mt-tag day">주간</span>{!adding && dayTeam && <span className="mt-team">{dayTeam}</span>}</div>
                  <textarea className="input mt-ta" value={form.dayContent} placeholder="주간 팀 미팅 내용…" onChange={e => setForm({ ...form, dayContent: e.target.value })} />
                </div>
                <div className="mt-section night">
                  <div className="mt-sec-head"><span className="mt-tag night">야간</span>{!adding && nightTeam && <span className="mt-team">{nightTeam}</span>}</div>
                  <textarea className="input mt-ta" value={form.nightContent} placeholder="야간 팀 미팅 내용…" onChange={e => setForm({ ...form, nightContent: e.target.value })} />
                </div>
                <div className="mt-section office">
                  <div className="mt-sec-head"><span className="mt-tag office">Office 메모</span></div>
                  <textarea className="input mt-ta" value={form.officeMemo} placeholder="Office 메모…" onChange={e => setForm({ ...form, officeMemo: e.target.value })} />
                </div>

                <div className="mt-actions">
                  <button type="button" className="btn btn-ghost" onClick={() => { setAdding(false); setSel(null); }}>취소</button>
                  {!adding && <button type="button" className="btn mt-del" onClick={remove}>삭제</button>}
                  <button type="submit" className="btn btn-primary">{adding ? '등록' : '저장'}</button>
                </div>
              </form>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
