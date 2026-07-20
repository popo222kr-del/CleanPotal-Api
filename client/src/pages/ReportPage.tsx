import { useEffect, useState, useCallback } from 'react';
import { useAccess } from '../auth/useAccess';
import { api } from '../api/client';
import type { Report, ReportBlock, ReportGroup } from '../api/types';
import './ReportPage.css';

type Kind = 'meeting' | 'weekly';
const META: Record<Kind, { icon: string; title: string; desc: string }> = {
  meeting: { icon: '📝', title: '생산미팅', desc: '월별 회의록 · 블록별 진행사항' },
  weekly: { icon: '📄', title: '주간보고', desc: '월별 주간보고 · 블록별 항목' },
};

const emptyBlock = (): ReportBlock => ({
  id: 0, number: 0, category: '', status: '', content: '', contentRich: '', followUp: '', followUpRich: '',
  kind: '', heading: '', isCollapsed: false, progressPercent: 0, importance: '', followUpAttachments: '',
});

type Form = Omit<Report, 'id' | 'reportType' | 'createdAt' | 'updatedAt'>;
const emptyForm = (): Form => ({
  monthTitle: '', title: '', shortTitle: '', dateRange: '', memo: '', memoRich: '',
  mainContent: '', mainContentRich: '', nightContent: '', nightContentRich: '',
  attendees: '', summary: '', memoAttachments: '', mainAttachments: '', blocks: [],
});

export default function ReportPage({ kind }: { kind: Kind }) {
  const { canEditHandover, canEditOffice } = useAccess();
  const canEdit = canEditHandover || canEditOffice;
  const meta = META[kind];
  const [groups, setGroups] = useState<ReportGroup[]>([]);
  const [selId, setSelId] = useState<number | null>(null);
  const [mode, setMode] = useState<'view' | 'new' | null>(null);
  const [form, setForm] = useState<Form>(emptyForm());

  const load = useCallback(async () => {
    setGroups(await api.get<ReportGroup[]>(`/api/reports?type=${kind}`));
  }, [kind]);
  useEffect(() => { load(); setSelId(null); setMode(null); }, [load]);

  async function open(id: number) {
    const r = await api.get<Report>(`/api/reports/${id}`);
    setSelId(id); setMode('view');
    setForm({ ...r });
  }
  function startNew() {
    if (!canEdit) return;
    setSelId(null); setMode('new'); setForm(emptyForm());
  }
  function setF(patch: Partial<Form>) { setForm(f => ({ ...f, ...patch })); }

  function setBlock(i: number, patch: Partial<ReportBlock>) {
    setForm(f => ({ ...f, blocks: f.blocks.map((b, idx) => idx === i ? { ...b, ...patch } : b) }));
  }
  function addBlock() { setForm(f => ({ ...f, blocks: [...f.blocks, emptyBlock()] })); }
  function delBlock(i: number) { setForm(f => ({ ...f, blocks: f.blocks.filter((_, idx) => idx !== i) })); }

  async function save() {
    if (!canEdit) return;
    const body = { reportType: kind, ...form, blocks: form.blocks.map((b, i) => ({ ...b, number: b.number || i + 1 })) };
    if (mode === 'new') { const r = await api.post<Report>('/api/reports', body); await load(); open(r.id); }
    else if (selId) { await api.put(`/api/reports/${selId}`, body); await load(); }
  }
  async function del() {
    if (!canEdit) return;
    if (!selId || !confirm('이 보고서를 삭제할까요?')) return;
    await api.del(`/api/reports/${selId}`); setSelId(null); setMode(null); load();
  }

  return (
    <div>
      <header className="pg-header">
        <div><h2>{meta.title}</h2></div>
        {canEdit && <button className="btn btn-primary" onClick={startNew}>+ 새 보고서</button>}
      </header>
      <div className="pg-body">
        <div className="rp-layout">
          <div className="rp-left">
            {groups.length === 0 && <div className="rp-empty">보고서가 없습니다</div>}
            {groups.map(g => (
              <div key={g.monthTitle} className="rp-group">
                <div className="rp-month">{g.monthTitle}</div>
                {g.reports.map(r => (
                  <button key={r.id} className={`rp-item ${selId === r.id ? 'active' : ''}`} onClick={() => open(r.id)}>
                    <div className="rp-item-t">{r.shortTitle || r.title || '(제목 없음)'}</div>
                    <div className="rp-item-m">{r.dateRange} · {r.blockCount}블록</div>
                  </button>
                ))}
              </div>
            ))}
          </div>

          <div className="rp-right">
            {!mode && <div className="rp-none"><div style={{ fontSize: 34 }}>{meta.icon}</div><p>보고서를 선택하세요</p></div>}
            {mode && (
              <div className="rp-detail">
                <div className="rp-grid">
                  <FF l="월(그룹)"><input className="input" value={form.monthTitle} onChange={e => setF({ monthTitle: e.target.value })} placeholder="예: 2026년 7월" /></FF>
                  <FF l="기간"><input className="input" value={form.dateRange} onChange={e => setF({ dateRange: e.target.value })} /></FF>
                  <FF l="제목"><input className="input" value={form.title} onChange={e => setF({ title: e.target.value })} /></FF>
                  <FF l="짧은 제목"><input className="input" value={form.shortTitle} onChange={e => setF({ shortTitle: e.target.value })} /></FF>
                </div>

                {kind === 'meeting' && (
                  <div className="rp-grid">
                    <FF l="참석자"><input className="input" value={form.attendees} onChange={e => setF({ attendees: e.target.value })} /></FF>
                    <FF l="요약"><input className="input" value={form.summary} onChange={e => setF({ summary: e.target.value })} /></FF>
                    <FF l="주간 내용"><textarea className="input rp-ta" value={form.mainContent} onChange={e => setF({ mainContent: e.target.value })} /></FF>
                    <FF l="야간 내용"><textarea className="input rp-ta" value={form.nightContent} onChange={e => setF({ nightContent: e.target.value })} /></FF>
                  </div>
                )}
                <FF l="메모"><textarea className="input rp-ta" value={form.memo} onChange={e => setF({ memo: e.target.value })} /></FF>

                <div className="rp-blocks-head">
                  <span>블록 ({form.blocks.length})</span>
                  <button className="btn btn-ghost" onClick={addBlock}>+ 블록 추가</button>
                </div>
                <div className="rp-blocks">
                  {form.blocks.map((b, i) => (
                    <div key={i} className="rp-block">
                      <div className="rp-block-top">
                        <span className="rp-bno">#{i + 1}</span>
                        <input className="input rp-cat" placeholder="카테고리" value={b.category} onChange={e => setBlock(i, { category: e.target.value })} />
                        <input className="input rp-status" placeholder="상태" value={b.status} onChange={e => setBlock(i, { status: e.target.value })} />
                        <input className="input rp-imp" placeholder="중요도" value={b.importance} onChange={e => setBlock(i, { importance: e.target.value })} />
                        <label className="rp-prog">진행<input className="input" type="number" min={0} max={100} value={b.progressPercent} onChange={e => setBlock(i, { progressPercent: Number(e.target.value) })} />%</label>
                        <button className="rp-bdel" onClick={() => delBlock(i)}>✕</button>
                      </div>
                      {b.heading && <input className="input rp-heading" placeholder="제목(heading)" value={b.heading} onChange={e => setBlock(i, { heading: e.target.value })} />}
                      <textarea className="input rp-ta" placeholder="내용" value={b.content} onChange={e => setBlock(i, { content: e.target.value })} />
                      <textarea className="input rp-ta" placeholder="후속조치" value={b.followUp} onChange={e => setBlock(i, { followUp: e.target.value })} />
                    </div>
                  ))}
                  {form.blocks.length === 0 && <div className="rp-empty">블록이 없습니다. "+ 블록 추가"로 만드세요.</div>}
                </div>

                <div className="rp-actions">
                  {mode === 'view' && <button className="btn rp-delbtn" onClick={del}>삭제</button>}
                  <div style={{ flex: 1 }} />
                  <button className="btn btn-primary" onClick={save}>저장</button>
                </div>
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

function FF({ l, children }: { l: string; children: React.ReactNode }) {
  return <div className="rp-field"><label>{l}</label>{children}</div>;
}
