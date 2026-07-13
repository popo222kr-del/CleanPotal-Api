import { useEffect, useState, useCallback } from 'react';
import { api } from '../api/client';
import type { Report, ReportGroup, ShiftTeams } from '../api/types';
import './Meeting.css';

const DOW = ['일', '월', '화', '수', '목', '금', '토'];

// "yyyy.MM.dd" → Date (실패 시 null)
function parseDR(s: string): Date | null {
  const m = /^(\d{4})\.(\d{2})\.(\d{2})$/.exec(s.trim());
  if (!m) return null;
  return new Date(+m[1], +m[2] - 1, +m[3]);
}
function fmtDR(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}.${p(d.getMonth() + 1)}.${p(d.getDate())}`;
}
// "2026년 7월" 정렬 키
function monthKey(t: string): number {
  const m = /(\d{4})년\s*(\d{1,2})월/.exec(t);
  return m ? +m[1] * 100 + +m[2] : 0;
}

export default function Meeting() {
  const [groups, setGroups] = useState<ReportGroup[]>([]);
  const [openMonth, setOpenMonth] = useState<string | null>(null);   // 아코디언: 한 번에 한 달만
  const [selId, setSelId] = useState<number | null>(null);
  const [report, setReport] = useState<Report | null>(null);
  const [teams, setTeams] = useState<ShiftTeams | null>(null);
  const [dirty, setDirty] = useState(false);
  const [saving, setSaving] = useState(false);
  // 초안 (WPF _draftReport)
  const [dayText, setDayText] = useState('');
  const [nightText, setNightText] = useState('');
  const [memoText, setMemoText] = useState('');
  // 새 보고서 모달
  const [createOpen, setCreateOpen] = useState(false);
  const [createDate, setCreateDate] = useState('');

  // 월 내림차순 · 날짜 내림차순 정렬 (WPF와 동일: 최신이 위)
  const sortGroups = (gs: ReportGroup[]): ReportGroup[] =>
    [...gs]
      .map(g => ({
        ...g,
        reports: [...g.reports].sort((a, b) =>
          (parseDR(b.dateRange)?.getTime() ?? 0) - (parseDR(a.dateRange)?.getTime() ?? 0)),
      }))
      .sort((a, b) => monthKey(b.monthTitle) - monthKey(a.monthTitle));

  const loadGroups = useCallback(async (): Promise<ReportGroup[]> => {
    const gs = sortGroups(await api.get<ReportGroup[]>('/api/reports?type=meeting'));
    setGroups(gs);
    setOpenMonth(prev => prev ?? gs[0]?.monthTitle ?? null);   // 첫 로딩: 최신 달 펼침
    return gs;
  }, []);
  useEffect(() => { loadGroups(); }, [loadGroups]);

  // 미저장 이탈 경고
  useEffect(() => {
    const h = (e: BeforeUnloadEvent) => { if (dirty) { e.preventDefault(); e.returnValue = ''; } };
    window.addEventListener('beforeunload', h);
    return () => window.removeEventListener('beforeunload', h);
  }, [dirty]);

  async function openReport(id: number) {
    if (id === selId) return;
    if (dirty && !confirm('저장하지 않은 변경사항이 있습니다.\n저장하지 않고 이동하시겠습니까?')) return;
    const r = await api.get<Report>(`/api/reports/${id}`);
    setSelId(id);
    setReport(r);
    setDayText(r.mainContent);
    setNightText(r.nightContent);
    setMemoText(r.memo);
    setDirty(false);
    // 주간(김팀)/야간(장팀) 라벨 — 근무표에서 해당 날짜의 팀 조회
    setTeams(null);
    const d = parseDR(r.dateRange);
    if (d) {
      const p = (n: number) => String(n).padStart(2, '0');
      api.get<ShiftTeams>(`/api/schedule/shift-teams?date=${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`)
        .then(setTeams).catch(() => {});
    }
  }

  async function save() {
    if (!report) return;
    setSaving(true);
    try {
      // 주간/야간/메모만 갱신하고 나머지(블록·리치·첨부 등)는 그대로 보존
      const body = {
        reportType: report.reportType, monthTitle: report.monthTitle,
        title: report.title, shortTitle: report.shortTitle, dateRange: report.dateRange,
        memo: memoText, memoRich: report.memoRich,
        mainContent: dayText, mainContentRich: report.mainContentRich,
        nightContent: nightText, nightContentRich: report.nightContentRich,
        attendees: report.attendees, summary: report.summary,
        memoAttachments: report.memoAttachments, mainAttachments: report.mainAttachments,
        blocks: report.blocks.map(b => ({
          number: b.number, category: b.category, status: b.status,
          content: b.content, contentRich: b.contentRich, followUp: b.followUp, followUpRich: b.followUpRich,
          kind: b.kind, heading: b.heading, isCollapsed: b.isCollapsed,
          progressPercent: b.progressPercent, importance: b.importance,
          followUpAttachments: b.followUpAttachments,
        })),
      };
      await api.put(`/api/reports/${report.id}`, body);
      setReport({ ...report, mainContent: dayText, nightContent: nightText, memo: memoText });
      setDirty(false);
    } finally {
      setSaving(false);
    }
  }

  async function removeReport() {
    if (!report) return;
    if (!confirm(`'${report.title}' 보고서를 삭제하시겠습니까?\n삭제된 보고서는 복구할 수 없습니다.`)) return;
    await api.del(`/api/reports/${report.id}`);
    setSelId(null); setReport(null); setDirty(false);
    loadGroups();
  }

  function openCreate() {
    if (dirty && !confirm('저장하지 않은 변경사항이 있습니다.\n저장하지 않고 이동하시겠습니까?')) return;
    setCreateDate(new Date().toISOString().slice(0, 10));   // 기본: 오늘
    setCreateOpen(true);
  }

  async function confirmCreate() {
    if (!createDate) return;
    const d = new Date(createDate + 'T00:00:00');
    const dr = fmtDR(d);
    // 같은 날짜가 이미 있으면 해당 보고서로 이동 (WPF 동일)
    const exist = groups.flatMap(g => g.reports).find(r => r.dateRange === dr);
    if (exist) {
      alert(`이미 '${dr}' 날짜의 미팅 보고서가 존재합니다.\n해당 보고서로 이동합니다.`);
      setCreateOpen(false);
      setDirty(false);
      openReport(exist.id);
      return;
    }
    const p = (n: number) => String(n).padStart(2, '0');
    // WPF: 최신 보고서의 '종결' 아닌 블록을 이월
    let blocks: Report['blocks'] = [];
    const latest = groups[0]?.reports[0];
    if (latest) {
      try {
        const lr = await api.get<Report>(`/api/reports/${latest.id}`);
        blocks = lr.blocks.filter(b => b.status !== '종결');
      } catch { /* 이월 실패해도 생성은 진행 */ }
    }
    const body = {
      reportType: 'meeting',
      monthTitle: `${d.getFullYear()}년 ${d.getMonth() + 1}월`,
      title: `${d.getFullYear()}년 ${d.getMonth() + 1}월 ${d.getDate()}일`,
      shortTitle: `${p(d.getMonth() + 1)}.${p(d.getDate())} (${DOW[d.getDay()]})`,
      dateRange: dr,
      memo: '', memoRich: '', mainContent: '', mainContentRich: '',
      nightContent: '', nightContentRich: '', attendees: '', summary: '',
      memoAttachments: '', mainAttachments: '',
      blocks: blocks.map((b, i) => ({
        number: i + 1, category: b.category, status: b.status,
        content: b.content, contentRich: b.contentRich, followUp: b.followUp, followUpRich: b.followUpRich,
        kind: b.kind, heading: b.heading, isCollapsed: b.isCollapsed,
        progressPercent: b.progressPercent, importance: b.importance,
        followUpAttachments: b.followUpAttachments,
      })),
    };
    const created = await api.post<Report>('/api/reports', body);
    setCreateOpen(false);
    setDirty(false);
    await loadGroups();
    setOpenMonth(body.monthTitle);
    openReport(created.id);
  }

  const mark = () => setDirty(true);
  const dayLabel = teams?.dayTeams.length ? `주간 (${teams.dayTeams.join(', ')})` : '주간';
  const nightLabel = teams?.nightTeams.length ? `야간 (${teams.nightTeams.join(', ')})` : '야간';
  const createPreview = createDate
    ? (() => { const d = new Date(createDate + 'T00:00:00'); return `선택 날짜: ${fmtDR(d)} (${DOW[d.getDay()]})`; })()
    : '';

  return (
    <div className="mt-page">
      <header className="pg-header">
        <div>
          <h2>📝 생산 미팅</h2>
          <p>생산 관련 미팅 및 협의 내용을 관리합니다</p>
        </div>
        <button className="btn btn-primary" onClick={save} disabled={!report || saving || !dirty}>
          {saving ? '저장 중…' : dirty ? '변경사항 저장 *' : '저장됨'}
        </button>
      </header>

      <div className="mt-body">
        {/* ── 좌: 월별 날짜 목록 ── */}
        <aside className="mt-left">
          <button className="btn btn-primary mt-new" onClick={openCreate}>+ 새 보고서 생성</button>
          {groups.map(g => {
            const open = openMonth === g.monthTitle;
            return (
              <div key={g.monthTitle} className="mt-month">
                <button className={`mt-month-h ${open ? 'open' : ''}`}
                  onClick={() => setOpenMonth(open ? null : g.monthTitle)}>
                  <span className="mt-arrow">{open ? '▼' : '▶'}</span> {g.monthTitle}
                </button>
                {open && g.reports.map(r => (
                  <button key={r.id} className={`mt-date ${r.id === selId ? 'active' : ''}`}
                    onClick={() => openReport(r.id)}>
                    {r.shortTitle}
                  </button>
                ))}
              </div>
            );
          })}
          {groups.length === 0 && <p className="mt-empty-side">보고서가 없습니다</p>}
        </aside>

        {/* ── 중앙: 주간/야간 ── */}
        <section className="mt-center">
          {!report ? (
            <div className="mt-placeholder">왼쪽에서 보고서를 선택하거나 새 보고서를 생성하세요</div>
          ) : (
            <>
              <div className="mt-title-row">
                <h2 className="mt-title">{report.title}</h2>
                <button className="btn mt-del" onClick={removeReport}>보고서 삭제</button>
              </div>
              <span className="mt-shift day">{dayLabel}</span>
              <textarea className="mt-editor" value={dayText}
                onChange={e => { setDayText(e.target.value); mark(); }}
                placeholder="주간 근무 내용을 입력하세요" />
              <span className="mt-shift night">{nightLabel}</span>
              <textarea className="mt-editor" value={nightText}
                onChange={e => { setNightText(e.target.value); mark(); }}
                placeholder="야간 근무 내용을 입력하세요" />
            </>
          )}
        </section>

        {/* ── 우: Office 메모 ── */}
        <aside className="mt-right">
          <h3>Office 메모</h3>
          <textarea className="mt-memo" value={memoText} disabled={!report}
            onChange={e => { setMemoText(e.target.value); mark(); }}
            placeholder={report ? '메모를 입력하세요' : ''} />
        </aside>
      </div>

      {/* ── 새 보고서 모달 ── */}
      {createOpen && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setCreateOpen(false); }}>
          <div className="modal-box mt-create">
            <h3>새 보고서 생성</h3>
            <label>보고서 날짜</label>
            <input className="input" type="date" value={createDate} onChange={e => setCreateDate(e.target.value)} />
            <p className="mt-preview">{createPreview}</p>
            <div className="modal-actions">
              <button className="btn btn-ghost" onClick={() => setCreateOpen(false)}>취소</button>
              <button className="btn btn-primary" onClick={confirmCreate}>생성하기</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
