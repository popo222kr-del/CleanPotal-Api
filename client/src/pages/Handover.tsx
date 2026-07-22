import { useEffect, useState, useCallback, useRef } from 'react';
import { useAccess } from '../auth/useAccess';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { useIsMobile } from '../hooks/useIsMobile';
import type { Handover as HO, TodayStatus, Notice, TeamEvent, UpcomingEdu } from '../api/types';
import './Handover.css';

const STATUSES = ['전체', '진행', '포장'];   // 완료는 상단 '완료 목록' 버튼으로 별도 관리
const CATEGORIES = ['전체', 'QTZ', 'SEMES', '삼성'];
const NEXT_STATUS: Record<string, string> = { 진행: '포장', 포장: '완료' };
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
// 긴 본문 판정 — 목록에서는 접어서(클램프) 표시
function isLong(s: string): boolean {
  return s.length > 160 || (s.match(/\n/g)?.length ?? 0) >= 4;
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
  const { canEditHandover: canEdit, isAdmin } = useAccess();
  const isMobile = useIsMobile();
  const { user } = useAuth();
  const nav = useNavigate();
  const [items, setItems] = useState<HO[]>([]);
  const [counts, setCounts] = useState<Record<string, number>>({});
  const [status, setStatus] = useState('전체');
  const [category, setCategory] = useState('전체');
  const [search, setSearch] = useState('');
  const [searchQ, setSearchQ] = useState('');   // 디바운스된 검색어 (서버 호출용)
  const [due, setDue] = useState<'none' | 'late' | 'today' | 'tomorrow'>('none');
  const [modal, setModal] = useState(false);
  const [editId, setEditId] = useState<number | null>(null);
  const [editItem, setEditItem] = useState<HO | null>(null);   // 수정 모달 제목/이력 표시용
  const [form, setForm] = useState(emptyForm);
  const [formBase, setFormBase] = useState('');   // 열림 시점 스냅샷 — dirty 판정
  const [saving, setSaving] = useState(false);    // 저장 중 이중 제출 방지
  const [expanded, setExpanded] = useState<Set<number>>(new Set());   // 긴 내용/메모 펼침
  // 대시보드
  const [dash, setDash] = useState<TodayStatus | null>(null);
  const [notices, setNotices] = useState<Notice[]>([]);
  const [dashOpen, setDashOpen] = useState(() => localStorage.getItem('ho_dash_open') !== '0');
  // 업체명 자동완성 (업체 관리 연동)
  const [vendorNames, setVendorNames] = useState<string[]>([]);
  // 완료 목록 팝업 (상단 버튼으로 별도 관리 — 수정은 관리자만)
  const [doneOpen, setDoneOpen] = useState(false);
  const [doneItems, setDoneItems] = useState<HO[]>([]);
  const [doneSearch, setDoneSearch] = useState('');
  // 이미지 첨부 (작업 내용 / 메모 각각)
  const contentFileRef = useRef<HTMLInputElement>(null);
  const memoFileRef = useRef<HTMLInputElement>(null);
  const [preview, setPreview] = useState<string | null>(null);   // 큰 이미지 미리보기(라이트박스)
  // 배차표 작성용 선택
  const [sel, setSel] = useState<Set<number>>(new Set());

  // 검색 디바운스 (300ms) — 타이핑마다 서버 호출 방지
  useEffect(() => {
    const t = window.setTimeout(() => setSearchQ(search), 300);
    return () => window.clearTimeout(t);
  }, [search]);

  const load = useCallback(async () => {
    const q = `?status=${encodeURIComponent(status)}&category=${encodeURIComponent(category)}&search=${encodeURIComponent(searchQ)}&weekly=${weekly}`;
    const list = await api.get<HO[]>(`/api/handover${q}`);
    setItems(list);
    // 목록에서 사라진 항목은 선택 해제 (배차표 작성 개수/내용 정확성)
    setSel(prev => new Set(list.filter(h => prev.has(h.id)).map(h => h.id)));
    setCounts(await api.get<Record<string, number>>(`/api/handover/counts?weekly=${weekly}`));
  }, [status, category, searchQ, weekly]);
  useEffect(() => { load(); }, [load]);

  useEffect(() => {
    api.get<{ vendorName: string }[]>('/api/vendor')
      .then(vs => setVendorNames(vs.map(v => v.vendorName)))
      .catch(() => { /* 자동완성은 부가 기능 */ });
  }, []);

  function toggleDash() {
    setDashOpen(o => { localStorage.setItem('ho_dash_open', o ? '0' : '1'); return !o; });
  }
  function toggleExpand(id: number) {
    setExpanded(prev => { const n = new Set(prev); if (n.has(id)) n.delete(id); else n.add(id); return n; });
  }

  // 머리글 sticky 상태 (붙는 순간만 라운드→직각)
  const [stuck, setStuck] = useState(false);
  const sentinelRef = useRef<HTMLDivElement | null>(null);
  useEffect(() => {
    const el = sentinelRef.current;
    if (!el) return;
    const ob = new IntersectionObserver(([e]) => setStuck(!e.isIntersecting), { threshold: 0 });
    ob.observe(el);
    return () => ob.disconnect();
  }, [isMobile]);

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
    if (!canEdit) return;
    setEditId(null);
    setEditItem(null);
    // 입고일은 오늘 기본 — 등록 시점이 대부분 입고 당일
    const base = { ...emptyForm, owner: user?.realName ?? '', inDate: todayStr() };
    setForm(base);
    setFormBase(JSON.stringify(base));
    setModal(true);
  }
  function openEdit(h: HO) {
    if (!canEdit) return;
    setEditId(h.id);
    setEditItem(h);
    const g = parseImageGroups(h.images);
    const base = {
      vendor: h.vendor, owner: h.owner, content: h.content,
      inDate: h.inDate ?? '', outDate: h.outDate ?? '',
      deliveryMethod: h.deliveryMethod, memo: h.memo, status: h.status,
      contentImages: g.content, memoImages: g.memo,
    };
    setForm(base);
    setFormBase(JSON.stringify(base));
    setModal(true);
  }
  // 변경사항 있으면 확인 후 닫기 (배경 클릭·취소·Esc 공통)
  function closeModal() {
    if (JSON.stringify(form) !== formBase &&
        !confirm('작성 중인 내용이 있습니다. 저장하지 않고 닫을까요?')) return;
    setModal(false);
  }
  const closeRef = useRef(closeModal);
  closeRef.current = closeModal;
  useEffect(() => {
    if (!modal) return;
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') closeRef.current(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [modal]);
  async function save(e: React.FormEvent) {
    if (!canEdit || saving) return;
    e.preventDefault();
    if (form.inDate && form.outDate && form.outDate < form.inDate) {
      alert('출고일이 입고일보다 빠를 수 없습니다.');
      return;
    }
    setSaving(true);
    try {
      const body = {
        ...form, inDate: form.inDate || null, outDate: form.outDate || null, isWeekly: weekly,
        images: JSON.stringify({ content: form.contentImages, memo: form.memoImages }),
      };
      if (editId) await api.put(`/api/handover/${editId}`, body);
      else await api.post('/api/handover', body);
      setModal(false);
      await load();
    } catch (err) {
      alert(err instanceof Error ? err.message : '저장에 실패했습니다.');
    } finally {
      setSaving(false);
    }
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
    // 완료는 목록(전체/진행/포장)에서 사라지므로 실수 방지용 확인
    if (newStatus === '완료' && !confirm(`'${h.vendor}' 항목을 완료 처리할까요?\n완료된 항목은 상단 '완료 목록'에서 볼 수 있습니다.`)) return;
    try {
      await api.patch(`/api/handover/${h.id}/status`, { status: newStatus });
      await load();
    } catch (err) {
      alert(err instanceof Error ? err.message : '상태 변경에 실패했습니다.');
    }
  }
  async function openDone() {
    try {
      const list = await api.get<HO[]>(`/api/handover?status=${encodeURIComponent('완료')}&category=전체&search=&weekly=${weekly}`);
      setDoneItems(list);
      setDoneSearch('');
      setDoneOpen(true);
    } catch (err) {
      alert(err instanceof Error ? err.message : '완료 목록을 불러오지 못했습니다.');
    }
  }
  async function remove(h: HO) {
    if (!canEdit) return;
    if (!confirm(`'${h.vendor}' 항목을 삭제하시겠습니까?`)) return;
    try {
      await api.del(`/api/handover/${h.id}`);
      await load();
    } catch (err) {
      alert(err instanceof Error ? err.message : '삭제에 실패했습니다.');
    }
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

  // 지연/오늘/내일 출고 필터 (클라이언트) — 지연은 별도 분리해 가시화
  const t0 = todayStr(), t1 = todayStr(1);
  const isOverdue = (h: HO) => h.outDate != null && h.outDate < t0 && h.status !== '완료';
  const lateCount = items.filter(isOverdue).length;
  const shown = items.filter(h => {
    if (due === 'late') return isOverdue(h);
    if (due === 'today') return h.outDate === t0;
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
        {canEdit && <button className="btn btn-primary" onClick={openAdd}>+ 새 항목 등록</button>}
      </header>

      <div className="pg-body">
        {/* ── 대시보드 ── */}
        {!weekly && dashOpen && (
          <div className="ho-dash">
            <div className="ho-card">
              <div className="ho-card-h"><h3>공지 & 일정</h3></div>
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
              <div className="ho-card-h">
                <h3>오늘의 세정팀 현황</h3>
                <div className="ho-card-hr">
                  <span className="ho-dim">{dash?.date}</span>
                  <button className="ho-fold" onClick={toggleDash} title="대시보드 접기">접기 ▴</button>
                </div>
              </div>
              <div className="ho-card-b">
                <div className="ho-teams">
                  {(dash?.teams ?? []).map(t => {
                    // 상단: 오늘 근무 인원(주간/야간 N명), 아래: 휴무·교육 명단
                    const work = t.badges.find(b => b.kind === 'day' || b.kind === 'night');
                    const offEdu = t.badges.filter(b => b.kind === 'dayoff' || b.kind === 'nightoff' || b.kind === 'off' || b.kind === 'edu');
                    // 휴무·교육이 없는 팀은 한 줄로 축약 — 정보 있는 팀에 시선 집중
                    if (offEdu.length === 0) {
                      return (
                        <div key={t.team} className="ho-team compact">
                          <span className="ho-team-n">{t.team}</span>
                          {work && <span className={`ho-work k-${work.kind}`} title={work.names.join(', ')}>{work.kind === 'night' ? '야간' : '주간'} {work.names.length}명</span>}
                          <span className="ho-team-none">휴무·교육 없음</span>
                        </div>
                      );
                    }
                    return (
                      <div key={t.team} className="ho-team">
                        <div className="ho-team-top">
                          <span className="ho-team-n">{t.team}</span>
                          {work && <span className={`ho-work k-${work.kind}`} title={work.names.join(', ')}>{work.kind === 'night' ? '야간' : '주간'} {work.names.length}명</span>}
                        </div>
                        <div className="ho-team-badges">
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
        {!weekly && !dashOpen && (
          <button className="ho-unfold" onClick={toggleDash}>대시보드 펴기 ▾</button>
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
          {lateCount > 0 && (
            <button className={`ho-due late ${due === 'late' ? 'on' : ''}`} onClick={() => setDue(d => d === 'late' ? 'none' : 'late')}>지연 {lateCount}</button>
          )}
          <button className={`ho-due today ${due === 'today' ? 'on' : ''}`} onClick={() => setDue(d => d === 'today' ? 'none' : 'today')}>오늘 출고</button>
          <button className={`ho-due tomo ${due === 'tomorrow' ? 'on' : ''}`} onClick={() => setDue(d => d === 'tomorrow' ? 'none' : 'tomorrow')}>내일 출고</button>
          <input className="ho-search" placeholder="업체/내용/담당자 검색"
            value={search} onChange={e => setSearch(e.target.value)} />
          <button className={`btn ho-tool-btn ${selShown.length > 0 ? 'btn-primary' : 'btn-ghost'}`} onClick={() => goDispatch(selShown)}
            title={selShown.length > 0 ? '선택한 항목으로 배차표를 작성합니다' : '배차표 열기 (날짜 이동으로 과거 이력 조회) · 항목 클릭=읽음, 더블클릭=수정'}>
            {selShown.length > 0 ? `배차표 작성 (${selShown.length})` : '배차표'}
          </button>
        </div>

        {isMobile ? (
          /* ── 모바일: 업체별 카드 리스트 ── */
          <div className="ho-mlist">
            {shown.length === 0 && <div className="ho-empty">항목이 없습니다</div>}
            {shown.map(h => {
              const imgs = allImages(h.images);
              const late = isOverdue(h);
              const canEditRow = canEdit && (h.status !== '완료' || isAdmin);   // 완료 항목은 관리자만
              return (
                <div key={h.id} className={`ho-mcard ${sel.has(h.id) ? 'sel' : ''} ${late ? 'late' : ''}`} onClick={() => markRead(h)}>
                  <div className="ho-mc-top">
                    <label className="ho-mc-chk" onClick={e => e.stopPropagation()}>
                      <input type="checkbox" checked={sel.has(h.id)} onChange={() => toggleSel(h.id)} />
                    </label>
                    <span className={`cat-badge ${h.category}`}>{h.category}</span>
                    <span className="ho-mc-vendor">{h.isNewUpdate && <span className="ho-new" title="미확인" />}{h.vendor}</span>
                    {late && <span className="ho-dplus">D+{-daysUntil(h.outDate)}</span>}
                    <span className={`status-badge s-${h.status}`}>{h.status}</span>
                  </div>
                  <div className="ho-mc-content">{h.content}</div>
                  {h.memo && <div className="ho-mc-memo">{h.memo}</div>}
                  {imgs.length > 0 && (
                    <div className="ho-memo-imgs">
                      {imgs.slice(0, 6).map((s, i) => (
                        <img key={i} src={s} alt="" onClick={e => { e.stopPropagation(); setPreview(s); }} />
                      ))}
                      {imgs.length > 6 && <span className="ho-more">+{imgs.length - 6}</span>}
                    </div>
                  )}
                  <div className="ho-mc-meta">
                    <span>입고 {h.inDate ?? '-'}</span>
                    <span>출고 {h.outDate ?? '-'}</span>
                    <span>{h.owner}{h.deliveryMethod !== '미정' ? ` · ${h.deliveryMethod}` : ''}</span>
                  </div>
                  <div className="ho-mc-foot">
                    <div className="ho-mc-actions" onClick={e => e.stopPropagation()}>
                      {canEditRow && NEXT_STATUS[h.status] && (
                        <button className={`ho-sm ${NEXT_STATUS[h.status] === '완료' ? 'ok' : ''}`}
                          onClick={() => changeStatus(h, NEXT_STATUS[h.status])}>{NEXT_STATUS[h.status]}</button>
                      )}
                      {canEditRow && <button className="ho-sm" onClick={() => openEdit(h)}>수정</button>}
                      {canEditRow && <button className="ho-sm danger" onClick={() => remove(h)}>삭제</button>}
                      {canEdit && !canEditRow && <span className="ho-lock">관리자 전용</span>}
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        ) : (
        <>
        <div ref={sentinelRef} aria-hidden style={{ height: 1 }} />
        <div className={`ho-wrap ${stuck ? 'stuck' : ''}`}>
          <table className="ho-table">
            <thead>
              <tr>
                <th className="c-sel">
                  <input type="checkbox" title="전체 선택"
                    checked={shown.length > 0 && shown.every(h => sel.has(h.id))}
                    onChange={e => setSel(e.target.checked ? new Set(shown.map(h => h.id)) : new Set())} />
                </th>
                <th>분류</th><th>업체</th><th>내용</th><th>입고일</th><th>출고일</th>
                <th>상태</th><th>메모</th><th>담당자</th><th>관리</th>
              </tr>
            </thead>
            <tbody>
              {shown.length === 0 && <tr><td colSpan={10} className="ho-empty">항목이 없습니다</td></tr>}
              {shown.map(h => {
                const late = isOverdue(h);
                const dPlus = late ? -daysUntil(h.outDate) : 0;
                const canEditRow = canEdit && (h.status !== '완료' || isAdmin);   // 완료 항목은 관리자만
                const exp = expanded.has(h.id);
                const contentLong = isLong(h.content), memoLong = isLong(h.memo);
                return (
                <tr key={h.id} className={late ? 'late' : ''} onClick={() => markRead(h)}
                  onDoubleClick={() => { if (canEditRow) openEdit(h); }}>
                  <td className="c-sel" onClick={e => e.stopPropagation()} onDoubleClick={e => e.stopPropagation()}>
                    <input type="checkbox" checked={sel.has(h.id)} onChange={() => toggleSel(h.id)} />
                  </td>
                  <td><span className={`cat-badge ${h.category}`}>{h.category}</span></td>
                  <td className="ho-vendor">
                    {h.isNewUpdate && <span className="ho-new" title="미확인" />}{h.vendor}
                  </td>
                  <td className="ho-content">
                    <div className={contentLong && !exp ? 'ho-clamp' : ''}>{h.content}</div>
                    {contentLong && (
                      <button className="ho-morebtn" onClick={e => { e.stopPropagation(); toggleExpand(h.id); }}>
                        {exp ? '접기' : '더보기'}
                      </button>
                    )}
                    {h.creatorName && <div className="ho-meta">등록: {h.creatorName} ({fmtDt(h.createDate)})</div>}
                  </td>
                  <td className="ho-dates">{h.inDate ?? '-'}</td>
                  <td className="ho-dates">
                    {h.outDate ?? '-'}
                    {late && <span className="ho-dplus" title="출고일이 지났습니다">D+{dPlus}</span>}
                  </td>
                  <td><span className={`status-badge s-${h.status}`}>{h.status}</span></td>
                  <td className="ho-memo">
                    {h.memo && <div className={memoLong && !exp ? 'ho-clamp' : ''}><span className="ho-memo-t">{h.memo}</span></div>}
                    {memoLong && !contentLong && (
                      <button className="ho-morebtn" onClick={e => { e.stopPropagation(); toggleExpand(h.id); }}>
                        {exp ? '접기' : '더보기'}
                      </button>
                    )}
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
                  <td className="ho-owner">
                    {h.owner}
                    {h.deliveryMethod !== '미정' && <div className="ho-deliv-t">{h.deliveryMethod}</div>}
                  </td>
                  <td onClick={e => e.stopPropagation()}>
                    <div className="ho-actions">
                      {canEditRow && NEXT_STATUS[h.status] && (
                        <button className={`ho-sm ${NEXT_STATUS[h.status] === '완료' ? 'ok' : ''}`}
                          onClick={() => changeStatus(h, NEXT_STATUS[h.status])}>{NEXT_STATUS[h.status]}</button>
                      )}
                      {canEditRow && <button className="ho-sm" onClick={() => openEdit(h)}>수정</button>}
                      {canEditRow && <button className="ho-sm danger" onClick={() => remove(h)}>삭제</button>}
                      {canEdit && !canEditRow && <span className="ho-lock" title="완료된 항목은 관리자만 수정할 수 있습니다">관리자 전용</span>}
                    </div>
                  </td>
                </tr>
                );
              })}
            </tbody>
          </table>
        </div>
        </>
        )}
      </div>

      {modal && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) closeModal(); }}>
          <form className="modal-box modal-wide" onSubmit={save}>
            <h3>{editId ? `업무 상세 수정 — ${editItem?.vendor ?? ''}` : '새 항목 등록'}</h3>
            {editItem && (
              <p className="ho-modal-meta">
                등록: {editItem.creatorName || '-'} ({fmtDt(editItem.createDate)})
                {editItem.modifyDate && ` · 수정: ${editItem.modifierName} (${fmtDt(editItem.modifyDate)})`}
              </p>
            )}

            <div className="row">
              <div><label>업체명</label>
                <input className="input" required list="ho-vendors" value={form.vendor} onChange={e => setForm({ ...form, vendor: e.target.value })} placeholder="예: 삼성전자, SEMES" />
                <datalist id="ho-vendors">{vendorNames.map(n => <option key={n} value={n} />)}</datalist></div>
              <div><label>담당자</label><input className="input" required value={form.owner} onChange={e => setForm({ ...form, owner: e.target.value })} /></div>
            </div>
            <div className="row">
              <div><label>입고일</label>
                <input className="input" type="date" value={form.inDate}
                  onChange={e => setForm(f => ({ ...f, inDate: e.target.value, outDate: f.outDate && f.outDate < e.target.value ? e.target.value : f.outDate }))} /></div>
              <div><label>출고일</label>
                <input className="input" type="date" value={form.outDate} min={form.inDate || undefined}
                  onChange={e => setForm({ ...form, outDate: e.target.value })} /></div>
            </div>
            <div className="row">
              <div><label>진행 상태</label>
                <select className="input" value={form.status} onChange={e => setForm({ ...form, status: e.target.value })}>
                  {STATUS_OPTIONS.map(s => <option key={s}>{s}</option>)}
                </select></div>
              <div><label>배송 방법</label>
                <select className="input" value={form.deliveryMethod} onChange={e => setForm({ ...form, deliveryMethod: e.target.value })}>
                  {DELIVERY_OPTIONS.map(d => <option key={d} value={d}>{d}</option>)}
                </select></div>
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
              {!editId && <button type="button" className="btn btn-ghost" onClick={() => setForm({ ...emptyForm, owner: user?.realName ?? '', inDate: todayStr() })}>초기화</button>}
              <button type="button" className="btn btn-ghost" onClick={closeModal}>취소</button>
              <button type="submit" className="btn btn-primary" disabled={saving}>
                {saving ? '저장 중...' : editId ? '수정 내용 저장' : '업무 등록하기'}
              </button>
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

      {/* ── 완료 목록 팝업 (수정은 관리자만) ── */}
      {doneOpen && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setDoneOpen(false); }}>
          <div className="modal-box ho-done-box">
            <h3>완료 목록 ({doneItems.length}건){!isAdmin && <span className="ho-done-hint">완료 항목 수정은 관리자만 가능합니다</span>}</h3>
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
                      <tr key={h.id} title={isAdmin ? '더블클릭하면 수정' : undefined}
                        onDoubleClick={() => { if (isAdmin) { setDoneOpen(false); openEdit(h); } }}>
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
