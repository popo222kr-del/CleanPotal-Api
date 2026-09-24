import { useEffect, useLayoutEffect, useState, useCallback, useRef } from 'react';
import { useAccess } from '../auth/useAccess';
import { api, ApiError } from '../api/client';
import { useIsMobile } from '../hooks/useIsMobile';
import type { Report, ReportGroup, ShiftTeams } from '../api/types';
import './Meeting.css';

const DOW = ['일', '월', '화', '수', '목', '금', '토'];

type MeetingHit = {
  reportId: number; reportShortTitle: string; reportTitle: string; dateRange: string;
  fieldLabel: string; text: string;
};

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
  const { canEditHandover: canEdit } = useAccess();
  const isMobile = useIsMobile();
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
  // 전역 검색 (주간/야간/Office 메모 텍스트)
  const [q, setQ] = useState('');
  const [hits, setHits] = useState<MeetingHit[]>([]);

  // 에디터 자동 높이(auto-grow) — 내용만큼만 커지도록
  const dayRef = useRef<HTMLTextAreaElement>(null);
  const nightRef = useRef<HTMLTextAreaElement>(null);
  const memoRef = useRef<HTMLTextAreaElement>(null);
  useLayoutEffect(() => {
    for (const r of [dayRef, nightRef, memoRef]) {
      const el = r.current;
      if (el) { el.style.height = 'auto'; el.style.height = `${el.scrollHeight}px`; }
    }
  }, [dayText, nightText, memoText, selId]);

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
  // 첫 진입 시 최신 보고서 자동 열기 (모바일은 목록 우선이라 제외)
  const booted = useRef(false);
  useEffect(() => {
    loadGroups().then(gs => {
      if (booted.current) return;
      booted.current = true;
      const latest = gs[0]?.reports[0];
      if (latest && !isMobile) openReport(latest.id, true);
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loadGroups]);

  // 미저장 이탈 경고
  useEffect(() => {
    const h = (e: BeforeUnloadEvent) => { if (dirty) { e.preventDefault(); e.returnValue = ''; } };
    window.addEventListener('beforeunload', h);
    return () => window.removeEventListener('beforeunload', h);
  }, [dirty]);

  // ── 전역 검색 (디바운스) ──
  useEffect(() => {
    const key = q.trim();
    if (!key) { setHits([]); return; }
    const t = window.setTimeout(async () => {
      try { setHits(await api.get<MeetingHit[]>(`/api/reports/search?type=meeting&q=${encodeURIComponent(key)}`)); }
      catch { setHits([]); }
    }, 300);
    return () => window.clearTimeout(t);
  }, [q]);

  const openSeq = useRef(0);
  async function openReport(id: number, force = false) {
    if (id === selId) return;
    // 자동 저장 체계: 이동 전에 미저장 변경을 조용히 저장하고 넘어간다
    if (!force && dirty) await saveRef.current();
    const seq = ++openSeq.current;
    const r = await api.get<Report>(`/api/reports/${id}`);
    if (seq !== openSeq.current) return;   // 더 최근 클릭이 있으면 무시
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

  const [saveErr, setSaveErr] = useState(false);

  // 주간/야간/메모만 갱신하고 나머지(블록·리치·첨부 등)는 받아 온 그대로 보존
  type Texts = { mainContent: string; nightContent: string; memo: string };
  const TEXT_FIELDS = ['mainContent', 'nightContent', 'memo'] as const;
  const TEXT_LABEL: Record<keyof Texts, string> = { mainContent: '주간', nightContent: '야간', memo: '메모' };
  function buildBody(base: Report, t: Texts) {
    return {
      reportType: base.reportType, monthTitle: base.monthTitle,
      title: base.title, shortTitle: base.shortTitle, dateRange: base.dateRange,
      memo: t.memo, memoRich: base.memoRich,
      mainContent: t.mainContent, mainContentRich: base.mainContentRich,
      nightContent: t.nightContent, nightContentRich: base.nightContentRich,
      attendees: base.attendees, summary: base.summary,
      memoAttachments: base.memoAttachments, mainAttachments: base.mainAttachments,
      blocks: base.blocks.map(b => ({
        number: b.number, category: b.category, status: b.status,
        content: b.content, contentRich: b.contentRich, followUp: b.followUp, followUpRich: b.followUpRich,
        kind: b.kind, heading: b.heading, isCollapsed: b.isCollapsed,
        progressPercent: b.progressPercent, importance: b.importance,
        followUpAttachments: b.followUpAttachments,
      })),
      // 받아 온 버전을 함께 보낸다 — 그 사이 다른 조가 저장했으면 서버가 409 로 알려 준다.
      rowVersion: base.rowVersion,
    };
  }

  async function save() {
    if (!canEdit) return;
    if (!report) return;
    setSaving(true);
    try {
      const mine: Texts = { mainContent: dayText, nightContent: nightText, memo: memoText };
      let saved: Report;
      try {
        saved = await api.put<Report>(`/api/reports/${report.id}`, buildBody(report, mine));
      } catch (err) {
        if (!(err instanceof ApiError && err.status === 409)) throw err;
        // 주간조와 야간조가 같은 보고서를 동시에 쓴다. 예전에는 버전을 안 보내서 한쪽 저장이
        // 다른 쪽이 쓴 칸을 옛 값으로 덮었다. 이제는 칸 단위로 합친다 — 내가 고친 칸은 내 것,
        // 안 고친 칸은 상대 것. 같은 칸을 둘 다 고쳤을 때만 물어본다.
        const latest = await api.get<Report>(`/api/reports/${report.id}`);
        const merged: Texts = { ...mine };
        for (const f of TEXT_FIELDS) {
          const iChanged = mine[f] !== report[f];
          const theyChanged = latest[f] !== report[f];
          if (!iChanged) merged[f] = latest[f];
          else if (theyChanged && latest[f] !== mine[f]) {
            const keepMine = confirm(
              `다른 사람이 방금 '${TEXT_LABEL[f]}' 칸을 먼저 고쳤습니다.\n\n`
              + `[확인] 내가 쓴 내용으로 덮어씁니다.\n[취소] 상대가 쓴 내용을 불러옵니다(내가 쓴 내용은 사라집니다).`);
            if (!keepMine) merged[f] = latest[f];
          }
        }
        saved = await api.put<Report>(`/api/reports/${latest.id}`, buildBody(latest, merged));
        setDayText(merged.mainContent);
        setNightText(merged.nightContent);
        setMemoText(merged.memo);
      }
      setReport(saved);
      setDirty(false);
      setSaveErr(false);
    } catch {
      setSaveErr(true);   // dirty 유지 → 다음 입력/재시도 타이머에서 다시 저장
    } finally {
      setSaving(false);
    }
  }
  const saveRef = useRef(save);
  saveRef.current = save;

  // 자동 저장 — 입력 멈추고 1초 후 (실패 시 3초 후 재시도)
  useEffect(() => {
    if (!dirty || !report || !canEdit || saving) return;
    const t = window.setTimeout(() => { saveRef.current(); }, saveErr ? 3000 : 1000);
    return () => window.clearTimeout(t);
  }, [dayText, nightText, memoText, dirty, report, canEdit, saving, saveErr]);

  // Ctrl+S / Cmd+S — 즉시 저장
  useEffect(() => {
    const h = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') { e.preventDefault(); saveRef.current(); }
    };
    window.addEventListener('keydown', h);
    return () => window.removeEventListener('keydown', h);
  }, []);

  async function removeReport() {
    if (!canEdit) return;
    if (!report) return;
    if (!confirm(`'${report.title}' 보고서를 삭제하시겠습니까?\n삭제된 보고서는 복구할 수 없습니다.`)) return;
    await api.del(`/api/reports/${report.id}`);
    setSelId(null); setReport(null); setDirty(false);
    setDayText(''); setNightText(''); setMemoText(''); setTeams(null);
    loadGroups();
  }

  async function openCreate() {
    if (!canEdit) return;
    if (dirty) await saveRef.current();
    const now = new Date();
    const pad = (n: number) => String(n).padStart(2, '0');
    setCreateDate(`${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`);   // 기본: 오늘(로컬)
    setCreateOpen(true);
  }

  async function confirmCreate() {
    if (!canEdit) return;
    if (!createDate) return;
    const d = new Date(createDate + 'T00:00:00');
    const dr = fmtDR(d);
    // 같은 날짜가 이미 있으면 해당 보고서로 이동 (WPF 동일)
    const exist = groups.flatMap(g => g.reports).find(r => r.dateRange === dr);
    if (exist) {
      alert(`이미 '${dr}' 날짜의 미팅 보고서가 존재합니다.\n해당 보고서로 이동합니다.`);
      setCreateOpen(false);
      setDirty(false);
      openReport(exist.id, true);
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
    openReport(created.id, true);
  }

  const mark = () => setDirty(true);
  const dayLabel = teams?.dayTeams.length ? `주간 (${teams.dayTeams.join(', ')})` : '주간';
  const nightLabel = teams?.nightTeams.length ? `야간 (${teams.nightTeams.join(', ')})` : '야간';
  const createPreview = createDate
    ? (() => { const d = new Date(createDate + 'T00:00:00'); return `선택 날짜: ${fmtDR(d)} (${DOW[d.getDay()]})`; })()
    : '';

  // 모바일: 상세 → 목록으로 돌아가기 (미저장 변경은 조용히 저장)
  async function backToList() {
    if (dirty) await saveRef.current();
    setSelId(null); setReport(null); setDirty(false);
  }

  const searching = q.trim() !== '';
  async function openHit(id: number) {
    setQ('');
    await openReport(id);
  }

  return (
    <div className="mt-page">
      <header className="pg-header">
        <div>
          <h2>생산팀 인수인계</h2>
        </div>
        {canEdit && report && (
          <span className={`mt-savestat ${saving ? 's-saving' : saveErr ? 's-err' : dirty ? 's-typing' : 's-ok'}`}>
            {saving ? '저장 중…' : saveErr ? '저장 실패 · 자동 재시도' : dirty ? '입력 중…' : '모든 변경사항 저장됨'}
          </span>
        )}
      </header>

      <div className={`mt-body ${isMobile ? ((report || searching) ? 'mob-detail' : 'mob-list') : ''}`}>
        {/* ── 좌: 검색 + 월별 날짜 목록 (모바일은 보고서 미선택·검색 전에만) ── */}
        <aside className="mt-left" style={isMobile && (report || searching) ? { display: 'none' } : undefined}>
          <input className="mt-search" placeholder="내용 검색 (주간/야간/Office 메모)" value={q} onChange={e => setQ(e.target.value)} />
          {canEdit && <button className="btn btn-primary mt-new" onClick={openCreate}>+ 인수인계서</button>}
          {groups.map(g => {
            const open = openMonth === g.monthTitle;
            return (
              <div key={g.monthTitle} className="mt-month">
                <button className={`mt-month-h ${open ? 'open' : ''}`}
                  onClick={() => setOpenMonth(open ? null : g.monthTitle)}>
                  <span className={`mt-arrow ${open ? 'open' : ''}`}>›</span> {g.monthTitle}
                </button>
                {open && g.reports.map(r => (
                  <button key={r.id}
                    className={`mt-date ${r.id === selId ? 'active' : ''} ${!r.hasContent && !r.hasMemo ? 'empty' : ''}`}
                    onClick={() => openReport(r.id)}>
                    <span>{r.shortTitle}</span>
                    {r.hasMemo && <span className="mt-dot" title="Office 메모 있음" />}
                  </button>
                ))}
              </div>
            );
          })}
          {groups.length === 0 && <p className="mt-empty-side">보고서가 없습니다</p>}
        </aside>

        {/* ── 중앙: 검색 결과 or 주간/야간/메모 카드 ── */}
        <section className="mt-center" style={isMobile && !report && !searching ? { display: 'none' } : undefined}>
          {isMobile && (report || searching) && (
            <button className="mt-back-btn" onClick={() => (searching ? setQ('') : backToList())}>← 목록</button>
          )}
          {searching ? (
            <>
              <div className="mt-title-row">
                <h3 className="mt-title">'{q.trim()}' 검색 결과 <span className="mt-title-sub">{hits.length}건</span></h3>
                <button className="mt-del-btn" onClick={() => setQ('')}>검색 닫기</button>
              </div>
              {hits.length === 0 && <div className="mt-placeholder"><p>검색 결과가 없습니다</p></div>}
              {hits.map((h, i) => (
                <button key={i} className="mt-hit" onClick={() => openHit(h.reportId)}>
                  <div className="mt-hit-top">
                    <span className="mt-hit-date">[{h.reportShortTitle || h.reportTitle}]</span>
                    <b>{h.fieldLabel}</b>
                  </div>
                  <p>{h.text}</p>
                </button>
              ))}
            </>
          ) : !report ? (
            <div className="mt-placeholder">
              <p>{groups.length === 0 ? '아직 작성된 보고서가 없습니다' : '왼쪽에서 보고서를 선택하세요'}</p>
              {canEdit && <button className="btn btn-primary" onClick={openCreate}>+ 인수인계서 만들기</button>}
            </div>
          ) : (
            <>
              <div className="mt-title-row">
                <h2 className="mt-title">{report.title}</h2>
                <button className="mt-del-btn" onClick={removeReport} title="보고서 삭제">삭제</button>
              </div>

              <div className="mt-card">
                <div className="mt-card-h day">{dayLabel}</div>
                <textarea ref={dayRef} className="mt-editor" value={dayText}
                  onChange={e => { setDayText(e.target.value); mark(); }}
                  placeholder="주간 근무 내용을 입력하세요" />
              </div>

              <div className="mt-card">
                <div className="mt-card-h night">{nightLabel}</div>
                <textarea ref={nightRef} className="mt-editor" value={nightText}
                  onChange={e => { setNightText(e.target.value); mark(); }}
                  placeholder="야간 근무 내용을 입력하세요" />
              </div>

              <div className="mt-card">
                <div className="mt-card-h memo">Office 메모</div>
                <textarea ref={memoRef} className="mt-editor" value={memoText}
                  onChange={e => { setMemoText(e.target.value); mark(); }}
                  placeholder="공유할 메모를 입력하세요" />
              </div>
            </>
          )}
        </section>
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
