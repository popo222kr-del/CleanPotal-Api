import { useEffect, useState, useCallback, useRef } from 'react';
import { useLocation } from 'react-router-dom';
import html2canvas from 'html2canvas';
import { api } from '../api/client';
import type { Dispatch as D, Vendor } from '../api/types';
import './Dispatch.css';

// ── 행 모델 (WPF DispatchItemModel) ──
type Row = {
  key: string;            // 로컬 렌더링 키
  id: number;             // 0 = 신규
  vendorName: string; outgoingDetails: string; incomingDetails: string;
  managerName: string; contactNumber: string; fullAddress: string; note: string;
};
type VendorRef = {
  addresses: { label: string; full: string; main: boolean }[];
  managers: { name: string; contact: string }[];
};
// 인수인계에서 넘어오는 항목
type ImportRow = { vendorName: string; incomingDetails: string };

let keySeq = 1;
const newKey = () => `r${keySeq++}`;
const blankRow = (): Row => ({
  key: newKey(), id: 0, vendorName: '', outgoingDetails: '-', incomingDetails: '',
  managerName: '', contactNumber: '', fullAddress: '', note: '',
});

const DOW = ['일', '월', '화', '수', '목', '금', '토'];
function ymd(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}
function addDays(s: string, n: number): string {
  const d = new Date(s + 'T00:00:00'); d.setDate(d.getDate() + n);
  return ymd(d);
}
function titleOf(s: string): string {
  const d = new Date(s + 'T00:00:00');
  return `${d.getFullYear()}년 ${d.getMonth() + 1}월 ${d.getDate()}일 (${DOW[d.getDay()]}요일) 배차 LIST`;
}

// 업체 마스터의 주소/담당자 JSON — WPF 객체 배열과 자동생성 문자열 배열 모두 지원
function parseVendorRef(v: Vendor): VendorRef {
  const ref: VendorRef = { addresses: [], managers: [] };
  try {
    const a = JSON.parse(v.addresses || '[]');
    if (Array.isArray(a)) for (const x of a) {
      if (typeof x === 'string') { if (x.trim()) ref.addresses.push({ label: x, full: x, main: ref.addresses.length === 0 }); }
      else if (x && typeof x === 'object') {
        const full = x.FullAddress ?? x.fullAddress ?? '';
        if (full) ref.addresses.push({ label: x.LocationName ?? x.locationName ?? '', full, main: !!(x.IsMain ?? x.isMain) });
      }
    }
  } catch { /* 형식 불량 무시 */ }
  try {
    const m = JSON.parse(v.managers || '[]');
    if (Array.isArray(m)) for (const x of m) {
      if (typeof x === 'string') { if (x.trim()) ref.managers.push({ name: x, contact: '' }); }
      else if (x && typeof x === 'object') {
        const name = x.ManagerName ?? x.managerName ?? '';
        const contact = x.ContactNumber ?? x.contactNumber ?? '';
        if (name || contact) ref.managers.push({ name, contact });
      }
    }
  } catch { /* 형식 불량 무시 */ }
  return ref;
}

// ── 행 검증 (WPF와 동일 규칙) ──
const isEmptyRow = (r: Row) =>
  !r.vendorName.trim() && (!r.outgoingDetails.trim() || r.outgoingDetails.trim() === '-')
  && !r.incomingDetails.trim() && !r.managerName.trim() && !r.contactNumber.trim()
  && !r.fullAddress.trim() && !r.note.trim();
const isUrgent = (r: Row) => /긴급|당일|확인/.test(r.note);
const hasIssue = (r: Row) => !isEmptyRow(r) &&
  (!r.vendorName.trim() || !r.managerName.trim() || !r.contactNumber.trim() || !r.fullAddress.trim());

export default function Dispatch() {
  const location = useLocation();
  const [date, setDate] = useState(() => ymd(new Date(Date.now() + 86400000)));  // 기본: 내일
  const [rows, setRows] = useState<Row[]>([]);
  const [vendors, setVendors] = useState<Map<string, VendorRef>>(new Map());
  const [vendorNames, setVendorNames] = useState<string[]>([]);
  const [dirty, setDirty] = useState(false);
  const [saveState, setSaveState] = useState('날짜별 배차표');
  const captureRef = useRef<HTMLDivElement>(null);
  const importedRef = useRef(false);

  // 업체 마스터 (자동완성용) — 한 번만
  useEffect(() => {
    (async () => {
      try {
        const list = await api.get<Vendor[]>('/api/vendor');
        const map = new Map<string, VendorRef>();
        for (const v of list) map.set(v.vendorName.trim(), parseVendorRef(v));
        setVendors(map);
        setVendorNames(list.map(v => v.vendorName).filter(Boolean).sort());
      } catch { /* 자동완성 없이도 동작 */ }
    })();
  }, []);

  const toRow = (d: D): Row => ({
    key: newKey(), id: d.id, vendorName: d.vendorName, outgoingDetails: d.outgoingDetails,
    incomingDetails: d.incomingDetails, managerName: d.managerName, contactNumber: d.contactNumber,
    fullAddress: d.fullAddress, note: d.note,
  });

  // 인수인계에서 넘어온 항목 → 업체 마스터로 담당자/주소 자동 채움
  const buildImported = useCallback((items: ImportRow[]): Row[] => items.map(it => {
    const r = { ...blankRow(), vendorName: it.vendorName, incomingDetails: it.incomingDetails };
    const ref = vendors.get(it.vendorName.trim());
    if (ref) {
      const main = ref.addresses.find(a => a.main) ?? ref.addresses[0];
      if (main) r.fullAddress = main.full;
      if (ref.managers.length === 1) { r.managerName = ref.managers[0].name; r.contactNumber = ref.managers[0].contact; }
    }
    return r;
  }), [vendors]);

  const loadDay = useCallback(async (target: string) => {
    const list = await api.get<D[]>(`/api/dispatch/day?date=${target}`);
    setRows(list.map(toRow));
    setDirty(false);
    setSaveState('날짜별 배차표를 불러왔습니다.');
  }, []);

  useEffect(() => { loadDay(date); }, [date, loadDay]);

  // 인수인계 → 배차표 작성으로 넘어온 항목 병합 (업체 마스터 로딩 후 1회)
  useEffect(() => {
    const items = (location.state as { rows?: ImportRow[] } | null)?.rows;
    if (!items?.length || importedRef.current || vendors.size === 0) return;
    importedRef.current = true;
    window.history.replaceState({}, '');   // 새로고침 시 중복 병합 방지
    setRows(prev => [...prev, ...buildImported(items)]);
    setDirty(true);
    setSaveState(`인수인계 ${items.length}건을 가져왔습니다 · 저장 필요`);
  }, [vendors, location.state, buildImported]);

  function changeDate(next: string) {
    if (dirty && !confirm('저장하지 않은 변경이 있습니다. 날짜를 이동할까요?')) return;
    setDate(next);
  }

  function patch(key: string, p: Partial<Row>) {
    setRows(prev => prev.map(r => (r.key === key ? { ...r, ...p } : r)));
    setDirty(true);
    setSaveState('변경됨 · 저장 필요');
  }

  // 업체명 확정 시 담당자/연락처/주소 자동 채움 (WPF LoadComboboxData)
  function onVendorCommit(r: Row) {
    const ref = vendors.get(r.vendorName.trim());
    if (!ref) return;
    const p: Partial<Row> = {};
    if (!r.fullAddress.trim()) {
      const main = ref.addresses.find(a => a.main) ?? ref.addresses[0];
      if (main) p.fullAddress = main.full;
    }
    if (!r.managerName.trim() && !r.contactNumber.trim() && ref.managers.length === 1) {
      p.managerName = ref.managers[0].name;
      p.contactNumber = ref.managers[0].contact;
    }
    if (Object.keys(p).length) patch(r.key, p);
  }
  // 담당자 ↔ 연락처 동기화 (WPF CommitReferenceSelection)
  function onManagerCommit(r: Row) {
    const ref = vendors.get(r.vendorName.trim());
    const m = ref?.managers.find(x => x.name === r.managerName.trim());
    if (m && m.contact && m.contact !== r.contactNumber) patch(r.key, { contactNumber: m.contact });
  }
  function onContactCommit(r: Row) {
    const ref = vendors.get(r.vendorName.trim());
    const m = ref?.managers.find(x => x.contact === r.contactNumber.trim());
    if (m && m.name && m.name !== r.managerName) patch(r.key, { managerName: m.name });
  }

  function addRow() {
    setRows(prev => [...prev, blankRow()]);
    setDirty(true);
    setSaveState('새 행 추가 · 저장 필요');
  }
  function removeRow(r: Row) {
    if (!isEmptyRow(r) && !confirm(`${r.vendorName || '(빈 행)'} 행을 삭제하시겠습니까?`)) return;
    setRows(prev => prev.filter(x => x.key !== r.key));
    setDirty(true);
    setSaveState('행 삭제됨 · 저장 필요');
  }

  async function save(silent = false): Promise<boolean> {
    try {
      const body = { rows: rows.filter(r => !isEmptyRow(r)).map(({ key: _k, ...rest }) => rest) };
      const saved = await api.put<D[]>(`/api/dispatch/day?date=${date}`, body);
      setRows(saved.map(toRow));
      setDirty(false);
      const invalid = saved.filter(d => !d.managerName || !d.contactNumber || !d.fullAddress).length;
      setSaveState(invalid > 0 ? `저장 완료 · 확인 필요 행 ${invalid}건` : `저장 완료 (${saved.length}건)`);
      if (!silent && invalid > 0) alert(`저장했습니다. 담당자/연락처/주소 확인이 필요한 행이 ${invalid}건 있습니다.`);
      return true;
    } catch (e) {
      setSaveState('저장 실패');
      if (!silent) alert(`저장 오류: ${e instanceof Error ? e.message : e}`);
      return false;
    }
  }

  // 이월: 다음 날로 이동 (신규 행은 저장 후 가능)
  async function carryOver(r: Row) {
    const target = addDays(date, 1);
    if (r.id === 0) { alert('저장하지 않은 행입니다. 먼저 저장한 뒤 이월해 주세요.'); return; }
    if (!confirm(`[${r.vendorName}] 항목을 ${target} 배차표로 이월할까요?\n(현재 날짜 표에서는 사라집니다)`)) return;
    await api.patch(`/api/dispatch/${r.id}/move`, { targetDate: target });
    setRows(prev => prev.filter(x => x.key !== r.key));
    setSaveState(`${r.vendorName} → ${target} 이월 완료`);
  }

  // 📸 캡처: 깔끔한 표만 이미지로 클립보드 복사
  async function capture() {
    if (!captureRef.current) return;
    try {
      const canvas = await html2canvas(captureRef.current, { backgroundColor: '#ffffff', scale: 2 });
      const blob: Blob | null = await new Promise(res => canvas.toBlob(res, 'image/png'));
      if (!blob) throw new Error('이미지 생성 실패');
      await navigator.clipboard.write([new ClipboardItem({ 'image/png': blob })]);
      alert('배차표 이미지가 클립보드에 복사되었습니다.\n메신저에 붙여넣기(Ctrl+V) 하세요.');
    } catch {
      // 클립보드 권한 실패 시 다운로드로 대체
      const canvas = await html2canvas(captureRef.current, { backgroundColor: '#ffffff', scale: 2 });
      const a = document.createElement('a');
      a.href = canvas.toDataURL('image/png');
      a.download = `배차표_${date}.png`;
      a.click();
    }
  }

  const total = rows.filter(r => !isEmptyRow(r)).length;
  const invalid = rows.filter(hasIssue).length;
  const urgent = rows.filter(r => !isEmptyRow(r) && isUrgent(r)).length;
  const visibleRows = rows.filter(r => !isEmptyRow(r));
  const dowIdx = new Date(date + 'T00:00:00').getDay();

  return (
    <div>
      <header className="pg-header">
        <div>
          <h2>🚚 배차표</h2>
          <p>날짜별 배차 작성·조회 — 과거 이력과 작성이 한 화면입니다</p>
        </div>
        <button className="btn btn-ghost" onClick={addRow}>＋ 행 추가</button>
        <button className="btn btn-ghost" onClick={capture}>📸 캡처</button>
        <button className="btn btn-primary" onClick={() => save()}>💾 저장</button>
      </header>

      <div className="pg-body">
        {/* 날짜 내비게이션 */}
        <div className="dp-nav">
          <button className="dp-btn" onClick={() => changeDate(addDays(date, -1))}>◀ 이전</button>
          <button className="dp-btn" onClick={() => changeDate(ymd(new Date()))}>오늘</button>
          <button className="dp-btn" onClick={() => changeDate(ymd(new Date(Date.now() + 86400000)))}>내일</button>
          <button className="dp-btn" onClick={() => changeDate(addDays(date, 1))}>다음 ▶</button>
          <input className="dp-date" type="date" value={date} onChange={e => e.target.value && changeDate(e.target.value)} />
          <h3 className={`dp-title ${dowIdx === 0 ? 'sun' : dowIdx === 6 ? 'sat' : ''}`}>{titleOf(date)}</h3>
          <span className="dp-state">{saveState}</span>
        </div>

        {/* 요약 */}
        <div className="dp-summary">
          <span className="dp-chip">총 <b>{total}</b>건</span>
          <span className={`dp-chip ${invalid ? 'warn' : ''}`}>누락 행 <b>{invalid}</b>건</span>
          <span className={`dp-chip ${urgent ? 'urgent' : ''}`}>긴급/확인 비고 <b>{urgent}</b>건</span>
          <span className="dp-dim">업체를 선택하면 담당자·연락처·주소가 자동으로 채워집니다</span>
        </div>

        <div className="dp-wrap">
          <table className="dp-table">
            <thead>
              <tr>
                <th className="c-no">No</th><th className="c-vendor">업체</th>
                <th>반출</th><th>반입</th>
                <th className="c-mgr">담당자</th><th className="c-tel">연락처</th>
                <th className="c-addr">주소</th><th className="c-note">비고</th>
                <th className="c-mng">관리</th>
              </tr>
            </thead>
            <tbody>
              {rows.length === 0 && (
                <tr><td colSpan={9} className="dp-empty">이 날짜의 배차가 없습니다. [＋ 행 추가] 또는 인수인계에서 항목을 선택해 배차표를 작성하세요.</td></tr>
              )}
              {rows.map((r, i) => {
                const ref = vendors.get(r.vendorName.trim());
                return (
                  <tr key={r.key} className={hasIssue(r) ? 'row-warn' : isUrgent(r) && !isEmptyRow(r) ? 'row-urgent' : ''}>
                    <td className="c-no">{i + 1}</td>
                    <td>
                      <input className="dp-in" list="dp-vendors" value={r.vendorName}
                        onChange={e => patch(r.key, { vendorName: e.target.value })}
                        onBlur={() => onVendorCommit(r)} placeholder="업체명" />
                    </td>
                    <td><textarea className="dp-ta" value={r.outgoingDetails} onChange={e => patch(r.key, { outgoingDetails: e.target.value })} /></td>
                    <td><textarea className="dp-ta" value={r.incomingDetails} onChange={e => patch(r.key, { incomingDetails: e.target.value })} /></td>
                    <td>
                      <input className="dp-in" list={`dl-m-${r.key}`} value={r.managerName}
                        onChange={e => patch(r.key, { managerName: e.target.value })}
                        onBlur={() => onManagerCommit(r)} />
                      <datalist id={`dl-m-${r.key}`}>{ref?.managers.map((m, j) => <option key={j} value={m.name} />)}</datalist>
                    </td>
                    <td>
                      <input className="dp-in" list={`dl-c-${r.key}`} value={r.contactNumber}
                        onChange={e => patch(r.key, { contactNumber: e.target.value })}
                        onBlur={() => onContactCommit(r)} />
                      <datalist id={`dl-c-${r.key}`}>{ref?.managers.map((m, j) => <option key={j} value={m.contact} />)}</datalist>
                    </td>
                    <td>
                      <input className="dp-in" list={`dl-a-${r.key}`} value={r.fullAddress}
                        onChange={e => patch(r.key, { fullAddress: e.target.value })} />
                      <datalist id={`dl-a-${r.key}`}>{ref?.addresses.map((a, j) => <option key={j} value={a.full}>{a.label}</option>)}</datalist>
                    </td>
                    <td><textarea className="dp-ta" value={r.note} onChange={e => patch(r.key, { note: e.target.value })} placeholder="긴급/당일/확인 시 강조" /></td>
                    <td className="c-mng">
                      <div className="dp-actions">
                        <button className="dp-sm" title="다음 날로 이월" onClick={() => carryOver(r)}>이월</button>
                        <button className="dp-sm danger" onClick={() => removeRow(r)}>삭제</button>
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
        <datalist id="dp-vendors">{vendorNames.map(n => <option key={n} value={n} />)}</datalist>
      </div>

      {/* 캡처 전용 읽기 뷰 (화면 밖) */}
      <div className="dp-capture-holder" aria-hidden>
        <div ref={captureRef} className="dp-capture">
          <h2>{titleOf(date)}</h2>
          <table>
            <thead>
              <tr><th>No</th><th>업체</th><th>반출</th><th>반입</th><th>담당자</th><th>연락처</th><th>주소</th><th>비고</th></tr>
            </thead>
            <tbody>
              {visibleRows.map((r, i) => (
                <tr key={r.key}>
                  <td>{i + 1}</td><td className="cv">{r.vendorName}</td>
                  <td className="cl">{r.outgoingDetails}</td><td className="cl">{r.incomingDetails}</td>
                  <td>{r.managerName}</td><td>{r.contactNumber}</td>
                  <td className="cl">{r.fullAddress}</td><td className="cl urgent-note">{r.note}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
