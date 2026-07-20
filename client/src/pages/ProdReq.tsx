import { useEffect, useState, useCallback, useRef } from 'react';
import { useAccess } from '../auth/useAccess';
import { api } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { useIsMobile } from '../hooks/useIsMobile';
import type { ProdReq as PR } from '../api/types';
import './ProdReq.css';

// ── 구분/세부위치/분류 (WPF와 동일) ──
const LOCATIONS = ['METAL', 'N-METAL', '레이저실', '기타'];
const SUB_LOCATIONS: Record<string, string[]> = {
  METAL: ['입고실', '출고실', '세정실', '반입구'],
  'N-METAL': ['입고실', '출고실', '세정실', '반입구'],
  레이저실: ['LASER', 'CO2', '각인기', '기타'],
  기타: ['기타'],
};
const REQ_TYPES = ['소모품', '수리', '내용', '기타'];

// "[소모품] 장갑 요청" → { tag, body }
function parseDetail(s: string): { tag: string; body: string } {
  const m = /^\[(.+?)\]\s*([\s\S]*)$/.exec(s || '');
  return m ? { tag: m[1], body: m[2] } : { tag: '', body: s || '' };
}
function parseImages(s: string | null | undefined): string[] {
  if (!s) return [];
  try { const a = JSON.parse(s); return Array.isArray(a) ? a.filter(x => typeof x === 'string') : []; }
  catch { return []; }
}
// D-day 뱃지 (WPF DurationText + 색상 규칙)
function ddayChip(p: PR): { text: string; cls: string } {
  if (p.status === '완료') return { text: '완료', cls: 'c-done' };
  if (p.status === '보류') return { text: '보류', cls: 'c-hold' };
  if (!p.dueDate) return { text: '-', cls: 'c-none' };
  const days = Math.round((new Date(p.dueDate + 'T00:00:00').getTime() - new Date(new Date().toDateString()).getTime()) / 86400000);
  if (days < 0) return { text: `지연 +${-days}일`, cls: 'c-late' };
  if (days === 0) return { text: 'D-Day', cls: 'c-today' };
  return { text: `D-${days}`, cls: 'c-ok' };
}
// 마감까지 남은 일수 (없으면 아주 큰 값 → 정렬 시 맨 뒤)
function dueDays(p: PR): number {
  if (!p.dueDate) return 99999;
  return Math.round((new Date(p.dueDate + 'T00:00:00').getTime() - new Date(new Date().toDateString()).getTime()) / 86400000);
}
// 지연: 진행 상태이면서 마감일이 지난 건
function isLate(p: PR): boolean {
  return p.status === '진행' && p.dueDate != null && dueDays(p) < 0;
}
// 이미지 축소 (인수인계와 동일)
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

// 로컬(KST) 기준 yyyy-MM-dd — toISOString은 UTC라 자정~09시에 어제 날짜가 됨
function localYmd(offsetDays = 0): string {
  const d = new Date(); d.setDate(d.getDate() + offsetDays);
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

const emptyReg = { category: 'METAL', location: '입고실', reqType: '소모품', body: '', images: [] as string[] };

export default function ProdReq() {
  const { canEditHandover: canEdit } = useAccess();
  const { user } = useAuth();
  const isMobile = useIsMobile();
  const [items, setItems] = useState<PR[]>([]);
  const [tab, setTab] = useState<'전체' | '진행' | '보류'>('전체');
  const [showActive, setShowActive] = useState(true);
  const [showDone, setShowDone] = useState(false);   // 조치 완료 내역은 기본 접힘
  const [doneSearch, setDoneSearch] = useState('');
  const [preview, setPreview] = useState<string | null>(null);
  // 등록 모달
  const [regOpen, setRegOpen] = useState(false);
  const [reg, setReg] = useState(emptyReg);
  const regFileRef = useRef<HTMLInputElement>(null);
  // 조치 모달
  const [act, setAct] = useState<PR | null>(null);
  const [actForm, setActForm] = useState({ status: '진행', dueDate: '', actionDetail: '', actionImages: [] as string[], reqBody: '', reqTag: '', reqImages: [] as string[] });
  const actFileRef = useRef<HTMLInputElement>(null);
  const reqEditFileRef = useRef<HTMLInputElement>(null);

  const load = useCallback(async () => {
    setItems(await api.get<PR[]>('/api/prodreq?status=전체'));
  }, []);
  useEffect(() => {
    load();
    api.post('/api/prodreq/mark-read').catch(() => {});   // 진입 시 읽음 처리 → 뱃지 초기화
  }, [load]);

  const active = items.filter(p => p.status !== '완료' && (tab === '전체' || p.status === tab))
    // 지연 건을 항상 맨 위로, 그다음 마감 임박 순
    .sort((a, b) => {
      const la = isLate(a), lb = isLate(b);
      if (la !== lb) return la ? -1 : 1;
      return dueDays(a) - dueDays(b);
    });
  const cntInProg = items.filter(p => p.status === '진행').length;
  const cntHold = items.filter(p => p.status === '보류').length;
  const doneAll = items.filter(p => p.status === '완료')
    .sort((a, b) => (b.actionDate ?? '').localeCompare(a.actionDate ?? ''));
  // 완료 내역 검색 (요청내용·구분·요청자·담당자·조치내용)
  const dq = doneSearch.trim().toLowerCase();
  const done = dq
    ? doneAll.filter(p => [p.requestDetail, p.category, p.location, p.requester, p.assignee, p.actionDetail]
        .some(v => (v ?? '').toLowerCase().includes(dq)))
    : doneAll;

  // ── 등록 ──
  function openReg() {
    if (!canEdit) return;
    setReg(emptyReg);
    setRegOpen(true);
  }
  async function addRegImages(files: FileList | File[]) {
    for (const f of Array.from(files).filter(f => f.type.startsWith('image/'))) {
      const url = await resizeDataUrl(await fileToDataUrl(f));
      setReg(prev => ({ ...prev, images: [...prev.images, url] }));
    }
  }
  async function saveReg(e: React.FormEvent) {
    if (!canEdit) return;
    e.preventDefault();
    if (!reg.body.trim()) { alert('상세 요청사항을 입력해 주세요.'); return; }
    await api.post('/api/prodreq', {
      requestDate: localYmd(),
      dueDate: null,
      category: reg.category,
      location: reg.location,
      requestDetail: `[${reg.reqType}] ${reg.body.trim()}`,
      actionDate: null, actionDetail: '', assignee: '',
      requestImages: JSON.stringify(reg.images),
    });
    setRegOpen(false);
    load();
    api.post('/api/prodreq/mark-read').catch(() => {});   // 내가 등록한 건이 내 미확인으로 잡히지 않게
  }

  // ── 조치/수정 ──
  const canEditReq = (p: PR) => p.requester === (user?.realName ?? '');
  function openAct(p: PR) {
    if (!canEdit) return;
    const d = parseDetail(p.requestDetail);
    const tomorrow = localYmd(1);
    setActForm({
      status: p.status, dueDate: p.dueDate ?? tomorrow,
      actionDetail: p.actionDetail, actionImages: parseImages(p.actionImages),
      reqBody: d.body, reqTag: d.tag, reqImages: parseImages(p.requestImages),
    });
    setAct(p);
  }
  async function addActImages(files: FileList | File[], target: 'action' | 'request') {
    for (const f of Array.from(files).filter(f => f.type.startsWith('image/'))) {
      const url = await resizeDataUrl(await fileToDataUrl(f));
      setActForm(prev => target === 'action'
        ? { ...prev, actionImages: [...prev.actionImages, url] }
        : { ...prev, reqImages: [...prev.reqImages, url] });
    }
  }
  async function saveAct(e: React.FormEvent) {
    e.preventDefault();
    if (!act) return;
    await api.put(`/api/prodreq/${act.id}`, {
      requestDate: act.requestDate,
      dueDate: actForm.dueDate || null,
      category: act.category,
      location: act.location,
      requestDetail: actForm.reqTag ? `[${actForm.reqTag}] ${actForm.reqBody}` : actForm.reqBody,
      actionDate: act.actionDate,
      actionDetail: actForm.actionDetail,
      assignee: '',                                   // 서버가 현재 사용자로 자동 기록
      status: actForm.status,
      requestImages: canEditReq(act) ? JSON.stringify(actForm.reqImages) : undefined,
      actionImages: JSON.stringify(actForm.actionImages),
    });
    setAct(null);
    load();
  }
  async function remove(p: PR) {
    if (!canEdit) return;
    if (!confirm(p.status === '완료' ? '완료된 항목을 삭제하시겠습니까?' : '이 요청을 삭제하시겠습니까?')) return;
    await api.del(`/api/prodreq/${p.id}`);
    load();
  }

  // 공용 행 렌더 (진행/완료 표)
  const renderRow = (p: PR, isDone: boolean) => {
    const d = parseDetail(p.requestDetail);
    const chip = ddayChip(p);
    const reqImgs = parseImages(p.requestImages);
    const actImgs = parseImages(p.actionImages);
    return (
      <tr key={p.id} className={!isDone && isLate(p) ? 'late' : ''} onDoubleClick={() => openAct(p)} title="더블클릭하면 조치/수정">
        <td className="pr-date">{isDone ? (p.actionDate ?? '-') : (p.requestDate ?? '-')}</td>
        {!isDone && <td><span className={`pr-chip ${chip.cls}`}>● {chip.text}</span></td>}
        <td className="pr-cat"><b>{p.category}</b>{p.location && <div className="pr-loc">({p.location})</div>}</td>
        <td className="pr-detail">
          {d.tag && <span className="pr-tag">[{d.tag}]</span>} {d.body}
          {reqImgs.length > 0 && (
            <div className="pr-inline-imgs">
              {reqImgs.slice(0, 4).map((s, i) => <img key={i} src={s} alt="" onClick={e => { e.stopPropagation(); setPreview(s); }} />)}
            </div>
          )}
        </td>
        <td className="pr-people">
          <div><span className="dot blue" />요청 <b>{p.requester || '-'}</b></div>
          <div><span className="dot green" />담당 <b>{p.assignee || '-'}</b></div>
        </td>
        <td className="pr-action">
          {p.actionDetail}
          {actImgs.length > 0 && (
            <div className="pr-inline-imgs">
              {actImgs.slice(0, 4).map((s, i) => <img key={i} src={s} alt="" onClick={e => { e.stopPropagation(); setPreview(s); }} />)}
            </div>
          )}
        </td>
        <td className="pr-manage" onClick={e => e.stopPropagation()}>
          <button className="pr-sm" onClick={() => openAct(p)}>조치/수정</button>
          <button className="pr-sm danger" onClick={() => remove(p)}>삭제</button>
        </td>
      </tr>
    );
  };

  // 모바일 카드 렌더
  const renderCard = (p: PR, isDone: boolean) => {
    const d = parseDetail(p.requestDetail);
    const chip = ddayChip(p);
    const reqImgs = parseImages(p.requestImages);
    const actImgs = parseImages(p.actionImages);
    return (
      <div key={p.id} className={`pr-mcard ${!isDone && isLate(p) ? 'late' : ''}`} onClick={() => openAct(p)}>
        <div className="pr-mc-top">
          {!isDone && <span className={`pr-chip ${chip.cls}`}>● {chip.text}</span>}
          <span className="pr-mc-cat"><b>{p.category}</b>{p.location && <span className="pr-loc"> ({p.location})</span>}</span>
          <span className="pr-mc-date">{isDone ? (p.actionDate ?? '-') : (p.requestDate ?? '-')}</span>
        </div>
        <div className="pr-mc-detail">{d.tag && <span className="pr-tag">[{d.tag}]</span>} {d.body}</div>
        {reqImgs.length > 0 && (
          <div className="pr-mc-imgs">
            {reqImgs.slice(0, 6).map((s, i) => <img key={i} src={s} alt="" onClick={e => { e.stopPropagation(); setPreview(s); }} />)}
          </div>
        )}
        <div className="pr-mc-people">
          <span><span className="dot blue" />요청 <b>{p.requester || '-'}</b></span>
          <span><span className="dot green" />담당 <b>{p.assignee || '-'}</b></span>
        </div>
        {p.actionDetail && <div className="pr-mc-action">🛠 {p.actionDetail}</div>}
        {actImgs.length > 0 && (
          <div className="pr-mc-imgs">
            {actImgs.slice(0, 6).map((s, i) => <img key={i} src={s} alt="" onClick={e => { e.stopPropagation(); setPreview(s); }} />)}
          </div>
        )}
        <div className="pr-mc-foot" onClick={e => e.stopPropagation()}>
          <button className="pr-sm" onClick={() => openAct(p)}>조치/수정</button>
          <button className="pr-sm danger" onClick={() => remove(p)}>삭제</button>
        </div>
      </div>
    );
  };

  return (
    <div>
      <header className="pg-header">
        <div>
          <h2>생산팀 요청사항</h2>
        </div>
        <button className="btn btn-primary" onClick={openReg}>+ 새 요청 등록</button>
      </header>

      <div className="pg-body">
        {/* ── 진행 및 보류 중인 요청 ── */}
        <div className="pr-card">
          <div className="pr-card-h">
            <h3>진행 및 보류 중인 요청</h3>
            <div className="pr-tabs">
              {(['전체', '진행', '보류'] as const).map(t => (
                <button key={t} className={`pr-tab ${tab === t ? 'on' : ''}`} onClick={() => setTab(t)} type="button">
                  {t}
                  <span className={`pr-cnt ${t === '진행' ? 'g' : t === '보류' ? 'a' : ''}`}>
                    {t === '전체' ? cntInProg + cntHold : t === '진행' ? cntInProg : cntHold}
                  </span>
                </button>
              ))}
            </div>
            <button className="pr-fold" onClick={() => setShowActive(v => !v)} type="button">{showActive ? '진행 목록 접기 ▲' : '진행 목록 펴기 ▼'}</button>
          </div>
          {showActive && (isMobile ? (
            <div className="pr-mlist">
              {active.length === 0 && <div className="pr-empty">진행 중인 요청이 없습니다</div>}
              {active.map(p => renderCard(p, false))}
            </div>
          ) : (
            <div className="pr-wrap">
              <table className="pr-table">
                <thead>
                  <tr>
                    <th>요청일</th><th>상태</th><th>구분</th><th>요청사항</th>
                    <th>요청자 / 담당자</th><th>조치내용</th><th>관리</th>
                  </tr>
                </thead>
                <tbody>
                  {active.length === 0 && <tr><td colSpan={7} className="pr-empty">진행 중인 요청이 없습니다</td></tr>}
                  {active.map(p => renderRow(p, false))}
                </tbody>
              </table>
            </div>
          ))}
        </div>

        {/* ── 조치 완료 내역 ── */}
        <div className="pr-card">
          <div className="pr-card-h">
            <h3>조치 완료 내역 (최근)</h3>
            <span className="pr-dim">{done.length}{dq ? `/${doneAll.length}` : ''}건</span>
            {showDone && (
              <input className="pr-done-search" placeholder="완료 내역 검색 (내용·구분·요청/담당자)"
                value={doneSearch} onChange={e => setDoneSearch(e.target.value)} />
            )}
            <button className="pr-fold" onClick={() => setShowDone(v => !v)} type="button">{showDone ? '조치 완료 접기 ▲' : '조치 완료 펴기 ▼'}</button>
          </div>
          {showDone && (isMobile ? (
            <div className="pr-mlist">
              {done.length === 0 && <div className="pr-empty">완료된 항목이 없습니다</div>}
              {done.map(p => renderCard(p, true))}
            </div>
          ) : (
            <div className="pr-wrap">
              <table className="pr-table">
                <thead>
                  <tr>
                    <th>완료일</th><th>구분</th><th>요청내용</th>
                    <th>요청자 / 담당자</th><th>조치내용</th><th>관리</th>
                  </tr>
                </thead>
                <tbody>
                  {done.length === 0 && <tr><td colSpan={6} className="pr-empty">{dq ? '검색 결과가 없습니다' : '완료된 항목이 없습니다'}</td></tr>}
                  {done.map(p => renderRow(p, true))}
                </tbody>
              </table>
            </div>
          ))}
        </div>
      </div>

      {/* ── 새 요청 등록 모달 ── */}
      {regOpen && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setRegOpen(false); }}>
          <form className="modal-box pr-modal" onSubmit={saveReg}
            onPaste={e => {
              const fs: File[] = [];
              for (const it of e.clipboardData.items) if (it.type.startsWith('image/')) { const f = it.getAsFile(); if (f) fs.push(f); }
              if (fs.length) { e.preventDefault(); addRegImages(fs); }
            }}>
            <h3>새 요청 등록</h3>
            <div className="row">
              <div><label>구분</label>
                <select className="input" value={reg.category}
                  onChange={e => setReg({ ...reg, category: e.target.value, location: SUB_LOCATIONS[e.target.value][0] })}>
                  {LOCATIONS.map(l => <option key={l}>{l}</option>)}
                </select>
              </div>
              <div><label>세부 위치</label>
                <select className="input" value={reg.location} onChange={e => setReg({ ...reg, location: e.target.value })}>
                  {(SUB_LOCATIONS[reg.category] ?? ['기타']).map(l => <option key={l}>{l}</option>)}
                </select>
              </div>
              <div><label>요청 분류</label>
                <select className="input" value={reg.reqType} onChange={e => setReg({ ...reg, reqType: e.target.value })}>
                  {REQ_TYPES.map(t => <option key={t}>{t}</option>)}
                </select>
              </div>
            </div>
            <label>상세 요청사항</label>
            <textarea className="input pr-ta" required value={reg.body} onChange={e => setReg({ ...reg, body: e.target.value })}
              placeholder="요청 내용을 자세히 입력해 주세요" />
            <label>이미지 첨부 <span className="lbl-hint">드래그 · Ctrl+V · 클릭</span></label>
            <div className="img-drop" onClick={() => regFileRef.current?.click()}
              onDrop={e => { e.preventDefault(); if (e.dataTransfer.files.length) addRegImages(e.dataTransfer.files); }}
              onDragOver={e => e.preventDefault()}>
              {reg.images.length === 0
                ? <span className="img-drop-empty">이미지를 끌어다 놓거나 붙여넣기</span>
                : <div className="img-thumbs">{reg.images.map((s, i) => (
                    <div className="img-thumb" key={i}>
                      <img src={s} alt="" onClick={e => { e.stopPropagation(); setPreview(s); }} />
                      <button type="button" className="img-x" onClick={e => { e.stopPropagation(); setReg(prev => ({ ...prev, images: prev.images.filter((_, j) => j !== i) })); }}>×</button>
                    </div>))}
                  </div>}
              <input ref={regFileRef} type="file" accept="image/*" multiple hidden
                onChange={e => { if (e.target.files) addRegImages(e.target.files); e.target.value = ''; }} />
            </div>
            <p className="pr-note">요청자: <b>{user?.realName}</b> · 요청일: 오늘 (조치 예정일은 담당자가 조치 시 지정)</p>
            <div className="modal-actions">
              <button type="button" className="btn btn-ghost" onClick={() => setRegOpen(false)}>취소</button>
              <button type="submit" className="btn btn-primary">등록하기</button>
            </div>
          </form>
        </div>
      )}

      {/* ── 조치/수정 모달 ── */}
      {act && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setAct(null); }}>
          <form className="modal-box pr-modal wide" onSubmit={saveAct}>
            <h3>조치 / 수정 — {act.category}{act.location ? ` (${act.location})` : ''}</h3>

            <label>원본 요청사항 {!canEditReq(act) && <span className="lbl-hint">요청자({act.requester})만 수정 가능</span>}</label>
            <textarea className="input pr-ta" value={actForm.reqBody} readOnly={!canEditReq(act)}
              onChange={e => setActForm({ ...actForm, reqBody: e.target.value })} />
            {canEditReq(act) ? (
              <div className="img-drop" onClick={() => reqEditFileRef.current?.click()}
                onDrop={e => { e.preventDefault(); if (e.dataTransfer.files.length) addActImages(e.dataTransfer.files, 'request'); }}
                onDragOver={e => e.preventDefault()}
                onPaste={e => {
                  const fs: File[] = [];
                  for (const it of e.clipboardData.items) if (it.type.startsWith('image/')) { const f = it.getAsFile(); if (f) fs.push(f); }
                  if (fs.length) { e.preventDefault(); addActImages(fs, 'request'); }
                }}>
                {actForm.reqImages.length === 0
                  ? <span className="img-drop-empty">요청 이미지 (끌어다 놓기 · 붙여넣기)</span>
                  : <div className="img-thumbs">{actForm.reqImages.map((s, i) => (
                      <div className="img-thumb" key={i}>
                        <img src={s} alt="" onClick={e => { e.stopPropagation(); setPreview(s); }} />
                        <button type="button" className="img-x" onClick={e => { e.stopPropagation(); setActForm(prev => ({ ...prev, reqImages: prev.reqImages.filter((_, j) => j !== i) })); }}>×</button>
                      </div>))}
                    </div>}
                <input ref={reqEditFileRef} type="file" accept="image/*" multiple hidden
                  onChange={e => { if (e.target.files) addActImages(e.target.files, 'request'); e.target.value = ''; }} />
              </div>
            ) : (
              actForm.reqImages.length > 0 && (
                <div className="img-thumbs static">
                  {actForm.reqImages.map((s, i) => <img key={i} src={s} alt="" onClick={() => setPreview(s)} />)}
                </div>
              )
            )}

            <div className="row">
              <div><label>상태</label>
                <select className="input" value={actForm.status} onChange={e => setActForm({ ...actForm, status: e.target.value })}>
                  {['진행', '보류', '완료'].map(s => <option key={s}>{s}</option>)}
                </select>
              </div>
              <div><label>담당자 (자동 기록) 🔒</label>
                <input className="input" value={user?.realName ?? ''} readOnly title="저장 시 현재 로그인 사용자가 자동으로 기록됩니다" />
              </div>
              <div><label>조치 예정일</label>
                <input className="input" type="date" value={actForm.dueDate} onChange={e => setActForm({ ...actForm, dueDate: e.target.value })} />
              </div>
            </div>

            <label>조치 내용</label>
            <textarea className="input pr-ta" value={actForm.actionDetail}
              onChange={e => setActForm({ ...actForm, actionDetail: e.target.value })}
              placeholder="조치한 내용 / 진행 상황을 입력하세요" />
            <label>조치 결과 사진 <span className="lbl-hint">드래그 · Ctrl+V · 클릭</span></label>
            <div className="img-drop" onClick={() => actFileRef.current?.click()}
              onDrop={e => { e.preventDefault(); if (e.dataTransfer.files.length) addActImages(e.dataTransfer.files, 'action'); }}
              onDragOver={e => e.preventDefault()}
              onPaste={e => {
                const fs: File[] = [];
                for (const it of e.clipboardData.items) if (it.type.startsWith('image/')) { const f = it.getAsFile(); if (f) fs.push(f); }
                if (fs.length) { e.preventDefault(); addActImages(fs, 'action'); }
              }}>
              {actForm.actionImages.length === 0
                ? <span className="img-drop-empty">조치 결과 이미지 (끌어다 놓기 · 붙여넣기)</span>
                : <div className="img-thumbs">{actForm.actionImages.map((s, i) => (
                    <div className="img-thumb" key={i}>
                      <img src={s} alt="" onClick={e => { e.stopPropagation(); setPreview(s); }} />
                      <button type="button" className="img-x" onClick={e => { e.stopPropagation(); setActForm(prev => ({ ...prev, actionImages: prev.actionImages.filter((_, j) => j !== i) })); }}>×</button>
                    </div>))}
                  </div>}
              <input ref={actFileRef} type="file" accept="image/*" multiple hidden
                onChange={e => { if (e.target.files) addActImages(e.target.files, 'action'); e.target.value = ''; }} />
            </div>

            <div className="modal-actions">
              <button type="button" className="btn btn-ghost" onClick={() => setAct(null)}>취소</button>
              <button type="submit" className="btn btn-primary">저장</button>
            </div>
          </form>
        </div>
      )}

      {/* 라이트박스 */}
      {preview && <div className="img-light" onClick={() => setPreview(null)}><img src={preview} alt="" /></div>}
    </div>
  );
}
