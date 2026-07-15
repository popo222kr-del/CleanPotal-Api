import { useEffect, useState, useCallback, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { useIsMobile } from '../hooks/useIsMobile';
import type { Handover as HO, TodayStatus, Notice, TeamEvent, UpcomingEdu } from '../api/types';
import './Handover.css';

const STATUSES = ['전체', '진행', '포장'];   // 전체 먼저, 완료는 '완료 목록' 팝업으로 조회
const CATEGORIES = ['전체', 'QTZ', 'SEMES', '삼성'];
const NEXT_STATUS: Record<string, string> = { 진행: '포장' };   // 포장→완료 인라인 버튼은 표시하지 않음
const STATUS_OPTIONS = ['진행', '포장', '완료'];
const DELIVERY_OPTIONS = ['미정', '배차', '택배', '업체 회수', '직접수령'];
const DOW = ['일', '월', '화', '수', '목', '금', '토'];

const emptyForm = { vendor: '', owner: '', content: '', inDate: '', outDate: '', deliveryMethod: '미정', memo: '', status: '진행', contentImages: [] as string[], memoImages: [] as string[] };

// 첨부 이미지 JSON 파싱 — {content:[],memo:[]}(신규) 또는 배열(구버전) 모두 지원
type ImgGroups = { content: string[]; memo: string[] };
type ImgGroup = 'content' | 'memo';
function parseImageGroups(s: string | null | undefined): ImgGroups {
  if (!s) return { content: [], memo: [] };
  try {
    const a = JSON.parse(s);
    if (Array.isArray(a)) return { content: a.filter((x: unknown) => typeof x === 'string'), memo: [] };
    const g = (k: string) => Array.isArray(a?.[k]) ? a[k].filter((x: unknown) => typeof x === 'string') : [];
    return { content: g('content'), memo: g('memo') };
  } catch { return { content: [], memo: [] }; }
}
function allImages(s: string | null | undefined): string[] {
  const g = parseImageGroups(s); return [...g.content, ...g.memo];
}
// 큰 이미지는 캔버스로 축소해 base64 용량을 줄인다 (최대 변 1400px, JPEG 0.72)
const MAX_DIM = 1400;
function resizeDataUrl(dataUrl: string): Promise<string> {
  return new Promise(res => {
    const img = new Image();
    img.onload = () => {
      const scale = Math.min(1, MAX_DIM / Math.max(img.width, img.height));
      const w = Math.round(img.width * scale), h = Math.round(img.height * scale);
      const cv = document.createElement('canvas');
      cv.width = w; cv.height = h;
      const ctx = cv.getContext('2d');
      if (!ctx) { res(dataUrl); return; }
      ctx.drawImage(img, 0, 0, w, h);
      try { res(cv.toDataURL('image/jpeg', 0.72)); } catch { res(dataUrl); }
    };
    img.onerror = () => res(dataUrl);
    img.src = dataUrl;
  });
}
function fileToDataUrl(file: File): Promise<string> {
  return new Promise((res, rej) => {
    const fr = new FileReader();
    fr.onload = () => res(fr.result as string);
    fr.onerror = rej;
    fr.readAsDataURL(file);
  });
}

// ── 날짜/아이콘 유틸 ──
function todayStr(offset = 0): string {
  const d = new Date(); d.setDate(d.getDate() + offset);
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}
function fmtDt(s: string | null): string {
  if (!s) return '';
  const d = new Date(s); if (isNaN(d.getTime())) return '';
  const p = (n: number) => String(n).padStart(2, '0');
  return `${String(d.getFullYear()).slice(2)}.${p(d.getMonth() + 1)}.${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`;
}
function fmtMd(s: string | null): string {
  if (!s) return '';
  const d = new Date(s + 'T00:00:00'); if (isNaN(d.getTime())) return s ?? '';
  const p = (n: number) => String(n).padStart(2, '0');
  return `${p(d.getMonth() + 1)}-${p(d.getDate())} (${DOW[d.getDay()]})`;
}
function daysUntil(s: string | null): number {
  if (!s) return 9999;
  const d = new Date(s + 'T00:00:00'); const t = new Date(); t.setHours(0, 0, 0, 0);
  return Math.round((d.getTime() - t.getTime()) / 86400000);
}
function deliveryIcon(m: string): string {
  if (m.includes('배차')) return '🚚';
  if (m.includes('택배')) return '📦';
  if (m.includes('회수')) return '🏢';
  return '➖';
}
function progClass(p: number): string {
  return p <= 29 ? 'p-red' : p <= 59 ? 'p-orange' : p <= 89 ? 'p-gold' : 'p-green';
}
// 팀 일정 D-day 뱃지 (WPF LoadUpcomingTeamEvents)
// 여러 날 일정은 시작=미래→D-n, 오늘 시작→오늘, 이미 시작·미종료→진행중, 종료→완료
function eventDday(startDate: string | null, endDate: string | null): { label: string; cls: string } {
  const s = daysUntil(startDate);
  if (s > 3) return { label: `D-${s}`, cls: 'd-far' };
  if (s > 0) return { label: `D-${s}`, cls: 'd-soon' };
  if (s === 0) return { label: '오늘', cls: 'd-today' };
  return daysUntil(endDate) < 0 ? { label: '완료', cls: 'd-done' } : { label: '진행중', cls: 'd-far' };
}
// 교육 D-day 뱃지 (WPF LoadUpcomingEdu)
function eduDday(startDate: string | null): { label: string; cls: string } {
  const n = daysUntil(startDate);
  if (n === 0) return { label: 'D-Day', cls: 'd-today' };
  if (n <= 2) return { label: `D-${n}`, cls: 'd-soon' };
  if (n <= 5) return { label: `D-${n}`, cls: 'd-far' };
  return { label: `D-${n}`, cls: 'd-green' };
}

export default function Handover({ weekly = false }: { weekly?: boolean }) {
  const isMobile = useIsMobile();
  const { user } = useAuth();
  const nav = useNavigate();
  const [items, setItems] = useState<HO[]>([]);
  const [counts, setCounts] = useState<Record<string, number>>({});
  const [status, setStatus] = useState('전체');
  const [category, setCategory] = useState('전체');
  const [search, setSearch] = useState('');
  const [due, setDue] = useState<'none' | 'today' | 'tomorrow'>('none');
  const [modal, setModal] = useState(false);
  const [editId, setEditId] = useState<number | null>(null);
  const [form, setForm] = useState(emptyForm);
  // 대시보드
  const [dash, setDash] = useState<TodayStatus | null>(null);
  const [notices, setNotices] = useState<Notice[]>([]);
  const [dashOpen, setDashOpen] = useState(true);
  // 완료 목록 팝업
  const [doneOpen, setDoneOpen] = useState(false);
  const [doneItems, setDoneItems] = useState<HO[]>([]);
  const [doneSearch, setDoneSearch] = useState('');
  // 이미지 첨부 (작업 내용 / 메모 각각)
  const contentFileRef = useRef<HTMLInputElement>(null);
  const memoFileRef = useRef<HTMLInputElement>(null);
  const [preview, setPreview] = useState<string | null>(null);   // 큰 이미지 미리보기(라이트박스)
  // 배차표 작성용 선택
  const [sel, setSel] = useState<Set<number>>(new Set());

  const load = useCallback(async () => {
    const q = `?status=${encodeURIComponent(status)}&category=${encodeURIComponent(category)}&search=${encodeURIComponent(search)}&weekly=${weekly}`;
    const list = await api.get<HO[]>(`/api/handover${q}`);
    setItems(list);
    // 목록에서 사라진 항목은 선택 해제 (배차표 작성 개수/내용 정확성)
    setSel(prev => new Set(list.filter(h => prev.has(h.id)).map(h => h.id)));
    setCounts(await api.get<Record<string, number>>(`/api/handover/counts?weekly=${weekly}`));
  }, [status, category, search, weekly]);
  useEffect(() => { load(); }, [load]);

  // 대시보드 데이터 (일반 인수인계에서만)
  const loadDash = useCallback(async () => {
    if (weekly) return;
    try {
      setDash(await api.get<TodayStatus>('/api/schedule/today-status'));
      setNotices(await api.get<Notice[]>('/api/notice'));
    } catch { /* 대시보드는 부가 정보이므로 실패해도 표 사용 가능 */ }
  }, [weekly]);
  useEffect(() => { loadDash(); }, [loadDash]);

  function openAdd() {
    setEditId(null);
    setForm({ ...emptyForm, owner: user?.realName ?? '' });
    setModal(true);
  }
  function openEdit(h: HO) {
    setEditId(h.id);
    const g = parseImageGroups(h.images);
    setForm({
      vendor: h.vendor, owner: h.owner, content: h.content,
      inDate: h.inDate ?? '', outDate: h.outDate ?? '',
      deliveryMethod: h.deliveryMethod, memo: h.memo, status: h.status,
      contentImages: g.content, memoImages: g.memo,
    });
    setModal(true);
  }
  async function save(e: React.FormEvent) {
    e.preventDefault();
    const body = {
      ...form, inDate: form.inDate || null, outDate: form.outDate || null, isWeekly: weekly,
      images: JSON.stringify({ content: form.contentImages, memo: form.memoImages }),
    };
    if (editId) await api.put(`/api/handover/${editId}`, body);
    else await api.post('/api/handover', body);
    setModal(false);
    load();
  }
  // ── 이미지 첨부 (작업 내용 / 메모 각각, 드래그·붙여넣기·선택) ──
  async function addImages(files: FileList | File[], group: ImgGroup) {
    const imgs = Array.from(files).filter(f => f.type.startsWith('image/'));
    for (const f of imgs) {
      const url = await resizeDataUrl(await fileToDataUrl(f));
      setForm(prev => group === 'content'
        ? { ...prev, contentImages: [...prev.contentImages, url] }
        : { ...prev, memoImages: [...prev.memoImages, url] });
    }
  }
  function onDropImages(e: React.DragEvent, group: ImgGroup) {
    e.preventDefault();
    if (e.dataTransfer.files.length) addImages(e.dataTransfer.files, group);
  }
  function onPasteImages(e: React.ClipboardEvent, group: ImgGroup) {
    const files: File[] = [];
    for (const it of e.clipboardData.items)
      if (it.type.startsWith('image/')) { const f = it.getAsFile(); if (f) files.push(f); }
    if (files.length) { e.preventDefault(); addImages(files, group); }
  }
  function removeImage(i: number, group: ImgGroup) {
    setForm(prev => group === 'content'
      ? { ...prev, contentImages: prev.contentImages.filter((_, idx) => idx !== i) }
      : { ...prev, memoImages: prev.memoImages.filter((_, idx) => idx !== i) });
  }
  // 첨부 썸네일 영역 렌더 (작업 내용/메모 공용)
  const renderThumbs = (imgs: string[], group: ImgGroup) => (
    imgs.length === 0
      ? <span className="img-drop-empty">이미지를 끌어다 놓거나 붙여넣기</span>
      : (
        <div className="img-thumbs">
          {imgs.map((src, i) => (
            <div className="img-thumb" key={i}>
              <img src={src} alt="" onClick={e => { e.stopPropagation(); setPreview(src); }} />
              <button type="button" className="img-x" title="삭제"
                onClick={e => { e.stopPropagation(); removeImage(i, group); }}>×</button>
            </div>
          ))}
        </div>
      )
  );
  async function changeStatus(h: HO, newStatus: string) {
    await api.patch(`/api/handover/${h.id}/status`, { status: newStatus });
    load();
  }
  async function remove(h: HO) {
    if (!confirm('삭제하시겠습니까?')) return;
    await api.del(`/api/handover/${h.id}`);
    load();
  }
  async function openDone() {
    const list = await api.get<HO[]>(`/api/handover?status=${encodeURIComponent('완료')}&category=전체&search=&weekly=${weekly}`);
    setDoneItems(list);
    setDoneSearch('');
    setDoneOpen(true);
  }

  // ── 배차표 작성 (체크한 항목 → 배차표에 미리 채움, 없으면 그냥 열기) ──
  function toggleSel(id: number) {
    setSel(prev => { const n = new Set(prev); if (n.has(id)) n.delete(id); else n.add(id); return n; });
  }
  function goDispatch(picked: HO[]) {
    const rows = picked.map(h => ({ vendorName: h.vendor, incomingDetails: h.content }));
    setSel(new Set());
    nav('/dispatch', rows.length ? { state: { rows } } : undefined);
  }

  async function markRead(h: HO) {
    if (!h.isNewUpdate) return;
    await api.post(`/api/handover/${h.id}/read`);
    setItems(prev => prev.map(x => (x.id === h.id ? { ...x, isNewUpdate: false } : x)));
  }

  // 오늘/내일 출고 필터 (클라이언트)
  const t0 = todayStr(), t1 = todayStr(1);
  const shown = items.filter(h => {
    if (due === 'today') return h.outDate != null && h.outDate <= t0;
    if (due === 'tomorrow') return h.outDate === t1;
    return true;
  });
  // 배차표로 넘길 항목 = 화면에 보이면서 체크된 것만
  const selShown = shown.filter(h => sel.has(h.id));

  return (
    <div>
      <header className="pg-header">
        <div>
          <h2>{weekly ? '주간세정 현황' : '현장 업무 인수인계'}</h2>
        </div>
        {!weekly && <button className="btn btn-ghost" onClick={() => nav('/notice')}>공지 관리</button>}
        {!weekly && <button className="btn btn-ghost" onClick={() => nav('/vendors')}>업체 정보</button>}
        <button className="btn btn-ghost" onClick={openDone}>완료 목록</button>
        <button className="btn btn-primary" onClick={openAdd}>+ 새 항목 등록</button>
      </header>

      <div className="pg-body">
        {/* ── 대시보드 ── */}
        {!weekly && dashOpen && (
          <div className="ho-dash">
            <div className="ho-card">
              <div className="ho-card-h"><h3>📢 Office 공지</h3></div>
              <div className="ho-card-b">
                {notices.slice(0, 4).map(n => (
                  <div key={n.id} className="ho-notice"><span className="ho-dot">•</span>{n.title || n.content}</div>
                ))}
                {dash && dash.upcomingEvents.length > 0 && (
                  <div className="ho-sub">
                    <h4>팀 일정</h4>
                    {dash.upcomingEvents.map((e: TeamEvent) => {
                      const dd = eventDday(e.startDate, e.endDate);
                      return (
                        <div key={e.id} className="ho-line">
                          <span className={`dday ${dd.cls}`}>{dd.label}</span>
                          <span className="ho-line-d">{e.startDate === e.endDate ? fmtMd(e.startDate) : `${fmtMd(e.startDate)} ~ ${fmtMd(e.endDate)}`}</span>
                          <b>{e.content}</b>{e.detail && <span className="ho-dim"> - {e.detail}</span>}
                        </div>
                      );
                    })}
                  </div>
                )}
                {dash && dash.upcomingEdu.length > 0 && (
                  <div className="ho-sub">
                    <h4>교육 일정</h4>
                    <div className="ho-edu-grid">
                      {dash.upcomingEdu.map((e: UpcomingEdu, i) => {
                        const dd = eduDday(e.startDate);
                        return (
                          <div key={i} className="ho-line ho-edu-item">
                            <span className={`dday ${dd.cls}`}>{dd.label}</span>
                            <span className="ho-line-d">{e.startDate === e.endDate ? fmtMd(e.startDate) : `${fmtMd(e.startDate)} ~ ${fmtMd(e.endDate)}`}</span>
                            <b>{e.memberName}</b> · {e.courseName}{e.eduMethod && <span className="ho-dim"> ({e.eduMethod})</span>}
                          </div>
                        );
                      })}
                    </div>
                  </div>
                )}
              </div>
            </div>

            <div className="ho-card">
              <div className="ho-card-h"><h3>👥 오늘의 세정팀 현황</h3><span className="ho-dim">{dash?.date}</span></div>
              <div className="ho-card-b">
                <div className="ho-teams">
                  {(dash?.teams ?? []).map(t => {
                    // 상단: 오늘 근무 인원(주간/야간 N명), 아래: 휴무·교육 명단
                    const work = t.badges.find(b => b.kind === 'day' || b.kind === 'night');
                    const offEdu = t.badges.filter(b => b.kind === 'dayoff' || b.kind === 'nightoff' || b.kind === 'off' || b.kind === 'edu');
                    return (
                      <div key={t.team} className="ho-team">
                        <div className="ho-team-top">
                          <span className="ho-team-n">{t.team}</span>
                          {work && <span className={`ho-work k-${work.kind}`} title={work.names.join(', ')}>{work.kind === 'night' ? '야간' : '주간'} {work.names.length}명 근무</span>}
                        </div>
                        <div className="ho-team-badges">
                          {offEdu.length === 0 && <span className="td-b k-none">휴무·교육 없음</span>}
                          {offEdu.map((b, i) => (
                            <div key={i} className="ho-team-line">
                              <span className={`td-b k-${b.kind}`}>{b.text.replace(/:\s*\d+$/, '')}</span>
                              <span className="ho-team-names">{b.names.join(', ')}</span>
                            </div>
                          ))}
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>
            </div>
          </div>
        )}
        {!weekly && (
          <div className="ho-dash-toggle">
            <button onClick={() => setDashOpen(o => !o)}>{dashOpen ? '대시보드 접기 ▲' : '대시보드 펴기 ▼'}</button>
          </div>
        )}

        {/* ── 툴바 ── */}
        <div className="ho-toolbar">
          {STATUSES.map(s => (
            <button key={s} className={`ho-tab ${status === s ? 'active' : ''}`} onClick={() => setStatus(s)}>
              {s}{s !== '전체' && counts[s] != null && <span className="ho-badge">{counts[s]}</span>}
            </button>
          ))}
          <span className="ho-sep" />
          {CATEGORIES.map(c => (
            <button key={c} className={`ho-cat ${category === c ? 'active' : ''}`} onClick={() => setCategory(c)}>{c}</button>
          ))}
          <span className="ho-sep" />
          <button className={`ho-due today ${due === 'today' ? 'on' : ''}`} onClick={() => setDue(d => d === 'today' ? 'none' : 'today')}>🔥 오늘 출고(마감)</button>
          <button className={`ho-due tomo ${due === 'tomorrow' ? 'on' : ''}`} onClick={() => setDue(d => d === 'tomorrow' ? 'none' : 'tomorrow')}>⚡ 내일 출고</button>
          <input className="ho-search" placeholder="업체/내용/담당자 검색"
            value={search} onChange={e => setSearch(e.target.value)} />
          <button className={`btn ho-tool-btn ${selShown.length > 0 ? 'btn-primary' : 'btn-ghost'}`} onClick={() => goDispatch(selShown)}
            title={selShown.length > 0 ? '선택한 항목으로 배차표를 작성합니다' : '배차표 열기 (날짜 이동으로 과거 이력 조회)'}>
            🚚 {selShown.length > 0 ? `배차표 작성 (${selShown.length})` : '배차표'}
          </button>
        </div>
        <div className="ho-hint">💡 항목을 클릭하면 확인(읽음) 처리되며, 행을 더블클릭하면 수정할 수 있습니다.</div>

        {isMobile ? (
          /* ── 모바일: 업체별 카드 리스트 ── */
          <div className="ho-mlist">
            {shown.length === 0 && <div className="ho-empty">항목이 없습니다</div>}
            {shown.map(h => {
              const imgs = allImages(h.images);
              return (
                <div key={h.id} className={`ho-mcard ${sel.has(h.id) ? 'sel' : ''}`} onClick={() => markRead(h)}>
                  <div className="ho-mc-top">
                    <label className="ho-mc-chk" onClick={e => e.stopPropagation()}>
                      <input type="checkbox" checked={sel.has(h.id)} onChange={() => toggleSel(h.id)} />
                    </label>
                    <span className={`cat-badge ${h.category}`}>{h.category}</span>
                    <span className="ho-mc-vendor">{h.isNewUpdate && <span className="ho-new" title="미확인" />}{h.vendor}</span>
                    <span className={`status-badge s-${h.status}`}>{h.status}</span>
                  </div>
                  <div className="ho-mc-content">{h.content}</div>
                  {h.memo && <div className="ho-mc-memo">📝 {h.memo}</div>}
                  {imgs.length > 0 && (
                    <div className="ho-memo-imgs">
                      {imgs.slice(0, 6).map((s, i) => (
                        <img key={i} src={s} alt="" onClick={e => { e.stopPropagation(); setPreview(s); }} />
                      ))}
                      {imgs.length > 6 && <span className="ho-more">+{imgs.length - 6}</span>}
                    </div>
                  )}
                  <div className="ho-mc-meta">
                    <span>📥 {h.inDate ?? '-'}</span>
                    <span>📤 {h.outDate ?? '-'}</span>
                    <span>{deliveryIcon(h.deliveryMethod)} {h.owner}</span>
                  </div>
                  <div className="ho-mc-foot">
                    <div className="ho-mc-prog">
                      <div className="prog-wrap"><div className={`prog-bar ${progClass(h.progressPercent)}`} style={{ width: `${h.progressPercent}%` }} /></div>
                      <span className="prog-txt">{h.progressPercent}%</span>
                    </div>
                    <div className="ho-mc-actions" onClick={e => e.stopPropagation()}>
                      {NEXT_STATUS[h.status] && (
                        <button className="ho-sm" onClick={() => changeStatus(h, NEXT_STATUS[h.status])}>{NEXT_STATUS[h.status]}</button>
                      )}
                      <button className="ho-sm" onClick={() => openEdit(h)}>수정</button>
                      <button className="ho-sm danger" onClick={() => remove(h)}>삭제</button>
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        ) : (
        <div className="ho-wrap">
          <table className="ho-table">
            <thead>
              <tr>
                <th className="c-sel">
                  <input type="checkbox" title="전체 선택"
                    checked={shown.length > 0 && shown.every(h => sel.has(h.id))}
                    onChange={e => setSel(e.target.checked ? new Set(shown.map(h => h.id)) : new Set())} />
                </th>
                <th>분류</th><th>업체</th><th>내용</th><th>입고일</th><th>출고일</th>
                <th>상태</th><th>메모</th><th>담당자</th><th>납품</th><th>진행률</th><th>관리</th>
              </tr>
            </thead>
            <tbody>
              {shown.length === 0 && <tr><td colSpan={12} className="ho-empty">항목이 없습니다</td></tr>}
              {shown.map(h => (
                <tr key={h.id} onClick={() => markRead(h)} onDoubleClick={() => openEdit(h)}>
                  <td className="c-sel" onClick={e => e.stopPropagation()} onDoubleClick={e => e.stopPropagation()}>
                    <input type="checkbox" checked={sel.has(h.id)} onChange={() => toggleSel(h.id)} />
                  </td>
                  <td><span className={`cat-badge ${h.category}`}>{h.category}</span></td>
                  <td className="ho-vendor">
                    {h.isNewUpdate && <span className="ho-new" title="미확인" />}{h.vendor}
                  </td>
                  <td className="ho-content">
                    {h.content}
                    {h.creatorName && <div className="ho-meta">등록: {h.creatorName} ({fmtDt(h.createDate)})</div>}
                  </td>
                  <td className="ho-dates">{h.inDate ?? '-'}</td>
                  <td className="ho-dates">{h.outDate ?? '-'}</td>
                  <td><span className={`status-badge s-${h.status}`}>{h.status}</span></td>
                  <td className="ho-memo">
                    {h.memo && <span className="ho-memo-t">{h.memo}</span>}
                    {(() => { const imgs = allImages(h.images); return imgs.length > 0 ? (
                      <div className="ho-memo-imgs">
                        {imgs.slice(0, 5).map((s, i) => (
                          <img key={i} src={s} alt="" onClick={e => { e.stopPropagation(); setPreview(s); }} />
                        ))}
                        {imgs.length > 5 && <span className="ho-more">+{imgs.length - 5}</span>}
                      </div>
                    ) : null; })()}
                    {h.modifyDate && <div className="ho-meta mod">수정: {h.modifierName} ({fmtDt(h.modifyDate)})</div>}
                  </td>
                  <td>{h.owner}</td>
                  <td className="ho-deliv" title={h.deliveryMethod}>{deliveryIcon(h.deliveryMethod)}</td>
                  <td>
                    <div className="prog-wrap"><div className={`prog-bar ${progClass(h.progressPercent)}`} style={{ width: `${h.progressPercent}%` }} /></div>
                    <span className="prog-txt">{h.progressPercent}%</span>
                  </td>
                  <td onClick={e => e.stopPropagation()}>
                    <div className="ho-actions">
                      {NEXT_STATUS[h.status] && (
                        <button className="ho-sm" onClick={() => changeStatus(h, NEXT_STATUS[h.status])}>{NEXT_STATUS[h.status]}</button>
                      )}
                      <button className="ho-sm" onClick={() => openEdit(h)}>수정</button>
                      <button className="ho-sm danger" onClick={() => remove(h)}>삭제</button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        )}
      </div>

      {modal && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setModal(false); }}>
          <form className="modal-box modal-wide" onSubmit={save}>
            <h3>{editId ? '업무 상세 수정' : '새 항목 등록'}</h3>

            <div className="row">
              <div><label>업체명</label>
                <input className="input" required value={form.vendor} onChange={e => setForm({ ...form, vendor: e.target.value })} placeholder="예: 삼성전자, SEMES" /></div>
              <div><label>배송 방법</label>
                <select className="input" value={form.deliveryMethod} onChange={e => setForm({ ...form, deliveryMethod: e.target.value })}>
                  {DELIVERY_OPTIONS.map(d => <option key={d} value={d}>{deliveryIcon(d)} {d}</option>)}
                </select></div>
            </div>
            <div className="row">
              <div><label>담당자</label><input className="input" required value={form.owner} onChange={e => setForm({ ...form, owner: e.target.value })} /></div>
              <div><label>진행 상태</label>
                <select className="input" value={form.status} onChange={e => setForm({ ...form, status: e.target.value })}>
                  {STATUS_OPTIONS.map(s => <option key={s}>{s}</option>)}
                </select></div>
            </div>
            <div className="row">
              <div><label>입고일</label><input className="input" type="date" value={form.inDate} onChange={e => setForm({ ...form, inDate: e.target.value })} /></div>
              <div><label>출고일</label><input className="input" type="date" value={form.outDate} onChange={e => setForm({ ...form, outDate: e.target.value })} /></div>
            </div>

            {/* 작업 내용 | 메모 (각각 이미지 첨부) */}
            <div className="ho-cols">
              <div className="ho-col" onPaste={e => onPasteImages(e, 'content')}>
                <label>작업 내용</label>
                <textarea className="input ta-lg" required value={form.content}
                  onChange={e => setForm({ ...form, content: e.target.value })} placeholder="입고 품목 · 수량 · 작업 요청 사항 등" />
                <label className="lbl-sm">작업 내용 이미지 <span className="lbl-hint">드래그 · Ctrl+V · 클릭</span></label>
                <div className="img-drop" onDrop={e => onDropImages(e, 'content')} onDragOver={e => e.preventDefault()}
                  onClick={() => contentFileRef.current?.click()}>
                  {renderThumbs(form.contentImages, 'content')}
                  <input ref={contentFileRef} type="file" accept="image/*" multiple hidden
                    onChange={e => { if (e.target.files) addImages(e.target.files, 'content'); e.target.value = ''; }} />
                </div>
              </div>
              <div className="ho-col" onPaste={e => onPasteImages(e, 'memo')}>
                <label>메모</label>
                <textarea className="input ta-lg" value={form.memo}
                  onChange={e => setForm({ ...form, memo: e.target.value })} placeholder="특이사항, 진행 메모 등" />
                <label className="lbl-sm">메모 이미지 <span className="lbl-hint">드래그 · Ctrl+V · 클릭</span></label>
                <div className="img-drop" onDrop={e => onDropImages(e, 'memo')} onDragOver={e => e.preventDefault()}
                  onClick={() => memoFileRef.current?.click()}>
                  {renderThumbs(form.memoImages, 'memo')}
                  <input ref={memoFileRef} type="file" accept="image/*" multiple hidden
                    onChange={e => { if (e.target.files) addImages(e.target.files, 'memo'); e.target.value = ''; }} />
                </div>
              </div>
            </div>

            <div className="modal-actions">
              {!editId && <button type="button" className="btn btn-ghost" onClick={() => setForm({ ...emptyForm, owner: user?.realName ?? '' })}>초기화</button>}
              <button type="button" className="btn btn-ghost" onClick={() => setModal(false)}>취소</button>
              <button type="submit" className="btn btn-primary">{editId ? '수정 내용 저장' : '업무 등록하기'}</button>
            </div>
          </form>
        </div>
      )}

      {/* 이미지 라이트박스 */}
      {preview && (
        <div className="img-light" onClick={() => setPreview(null)}>
          <img src={preview} alt="" />
        </div>
      )}

      {/* ── 완료 목록 팝업 ── */}
      {doneOpen && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setDoneOpen(false); }}>
          <div className="modal-box ho-done-box">
            <h3>✅ 완료 목록 ({doneItems.length}건)</h3>
            <input className="ho-search" style={{ width: '100%', marginBottom: 10 }}
              placeholder="업체 / 내용 / 담당자 검색"
              value={doneSearch} onChange={e => setDoneSearch(e.target.value)} />
            <div className="ho-done-list">
              <table className="ho-table">
                <thead>
                  <tr><th>분류</th><th>업체</th><th>내용</th><th>입고일</th><th>출고일</th><th>담당자</th></tr>
                </thead>
                <tbody>
                  {(() => {
                    const q = doneSearch.trim();
                    const rows = q
                      ? doneItems.filter(h => h.vendor.includes(q) || h.content.includes(q) || h.owner.includes(q))
                      : doneItems;
                    if (rows.length === 0) return <tr><td colSpan={6} className="ho-empty">완료 항목이 없습니다</td></tr>;
                    return rows.map(h => (
                      <tr key={h.id} onDoubleClick={() => { setDoneOpen(false); openEdit(h); }} title="더블클릭하면 수정">
                        <td><span className={`cat-badge ${h.category}`}>{h.category}</span></td>
                        <td className="ho-vendor">{h.vendor}</td>
                        <td className="ho-content">{h.content}</td>
                        <td className="ho-dates">{h.inDate ?? '-'}</td>
                        <td className="ho-dates">{h.outDate ?? '-'}</td>
                        <td>{h.owner}</td>
                      </tr>
                    ));
                  })()}
                </tbody>
              </table>
            </div>
            <div className="modal-actions">
              <button type="button" className="btn btn-ghost" onClick={() => setDoneOpen(false)}>닫기</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
