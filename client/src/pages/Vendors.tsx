import { Fragment, useEffect, useRef, useState, useCallback } from 'react';
import { api } from '../api/client';
import { useAccess } from '../auth/useAccess';
import { useIsMobile } from '../hooks/useIsMobile';
import type { MesCustomer, MesLine, Vendor } from '../api/types';

type Result = { success: boolean; message: string };
import './Vendors.css';

const emptyForm = {
  vendorName: '', category: '일반', isWeekly: false, isFavorite: false,
  basePath: '', linkUrl: '', addresses: '', managers: '',
  mesCustomerId: null as number | null,
};

/** 모달 안의 MES 칸. 연결 안 함 · 기존 업체에 연결 · 새로 만들기 중 하나다. */
const emptyMes = { code: '', exportPrefix: '', lineDefinitionId: '', isActive: true };

/** URL에 스킴이 없으면 https:// 를 붙여 새 탭에서 열 수 있게 */
function withProto(url: string) {
  return /^[a-z]+:\/\//i.test(url) ? url : `https://${url}`;
}

/** 담당자 요약: 첫 담당자 + '외 N명' */
function mgrSummary(json: string): string {
  const rows = parseMgrs(json).filter(m => m.managerName || m.contactNumber);
  if (rows.length === 0) return '';
  const first = rows[0].contactNumber ? `${rows[0].managerName} (${rows[0].contactNumber})` : rows[0].managerName;
  return rows.length > 1 ? `${first} 외 ${rows.length - 1}명` : first;
}

// 주소: [{IsMain, LocationName, FullAddress}], 담당자: [{ManagerName, ContactNumber}]
function pick(o: Record<string, unknown>, ...keys: string[]): string {
  for (const k of keys) { const v = o[k] ?? o[k[0].toLowerCase() + k.slice(1)]; if (v) return String(v); }
  return '';
}

// ── 수정 모달용 구조화 파싱/직렬화 (WPF VendorManagerWindow 그리드와 동일 구조) ──
type AddrRow = { isMain: boolean; locationName: string; fullAddress: string };
type MgrRow = { managerName: string; contactNumber: string };

function parseAddrs(json: string): AddrRow[] {
  if (!json) return [];
  try {
    const v = JSON.parse(json);
    if (!Array.isArray(v)) return [];
    return v.map(item => typeof item === 'string'
      ? { isMain: false, locationName: '', fullAddress: item }
      : {
          isMain: Boolean((item as Record<string, unknown>).IsMain ?? (item as Record<string, unknown>).isMain),
          locationName: pick(item, 'LocationName'),
          fullAddress: pick(item, 'FullAddress'),
        });
  } catch { return json.trim() ? [{ isMain: false, locationName: '', fullAddress: json.trim() }] : []; }
}
function parseMgrs(json: string): MgrRow[] {
  if (!json) return [];
  try {
    const v = JSON.parse(json);
    if (!Array.isArray(v)) return [];
    return v.map(item => typeof item === 'string'
      ? { managerName: item, contactNumber: '' }
      : { managerName: pick(item, 'ManagerName'), contactNumber: pick(item, 'ContactNumber') });
  } catch { return json.trim() ? [{ managerName: json.trim(), contactNumber: '' }] : []; }
}
function addrsToJson(rows: AddrRow[]): string {
  const out = rows.filter(r => r.fullAddress.trim() || r.locationName.trim())
    .map(r => ({ IsMain: r.isMain, LocationName: r.locationName.trim(), FullAddress: r.fullAddress.trim() }));
  return out.length ? JSON.stringify(out) : '';
}
function mgrsToJson(rows: MgrRow[]): string {
  const out = rows.filter(r => r.managerName.trim() || r.contactNumber.trim())
    .map(r => ({ ManagerName: r.managerName.trim(), ContactNumber: r.contactNumber.trim() }));
  return out.length ? JSON.stringify(out) : '';
}
function summarize(json: string): string {
  if (!json) return '';
  try {
    const v = JSON.parse(json);
    if (Array.isArray(v)) {
      return v.map(item => {
        if (typeof item === 'string') return item;
        const o = item as Record<string, unknown>;
        // 주소 형태: FullAddress가 비어 있으면 구분명만 (true/본사 같은 원시값 노출 방지)
        if ('FullAddress' in o || 'fullAddress' in o || 'IsMain' in o || 'isMain' in o || 'LocationName' in o || 'locationName' in o) {
          const addr = pick(o, 'FullAddress');
          const loc = pick(o, 'LocationName');
          return addr ? (loc ? `${loc}: ${addr}` : addr) : loc;
        }
        const mgr = pick(o, 'ManagerName');
        if (mgr || 'ContactNumber' in o || 'contactNumber' in o) {
          const tel = pick(o, 'ContactNumber');
          return tel ? (mgr ? `${mgr} (${tel})` : tel) : mgr;
        }
        return Object.values(o).filter(x => typeof x === 'string' && x).join(' / ');
      }).filter(Boolean).join(', ');
    }
    if (typeof v === 'object' && v) return Object.values(v).filter(Boolean).join(' / ');
    return String(v);
  } catch {
    return json;
  }
}

export default function Vendors() {
  const isMobile = useIsMobile();
  
  const { canEditHandover: canManage } = useAccess();
  const [list, setList] = useState<Vendor[]>([]);
  const [search, setSearch] = useState('');
  const [cat, setCat] = useState('전체');
  const [modal, setModal] = useState(false);
  const [expand, setExpand] = useState<number | null>(null);
  const [editId, setEditId] = useState<number | null>(null);
  const [form, setForm] = useState(emptyForm);
  const [addrs, setAddrs] = useState<AddrRow[]>([]);
  const [mgrs, setMgrs] = useState<MgrRow[]>([]);

  // ── MES 업체(생산관리) — 같은 업체의 다른 쪽 자료다 ──
  // 업체 관리는 OFFICE 메뉴에 있고 MES 자료는 MES 권한이 있어야 읽힌다. 권한이 없으면 그 칸만 접는다.
  const [mesCustomers, setMesCustomers] = useState<MesCustomer[]>([]);
  const [mesLines, setMesLines] = useState<MesLine[]>([]);
  const [mesReadable, setMesReadable] = useState(true);
  const [mesMode, setMesMode] = useState<'none' | 'link' | 'new'>('none');
  const [mesLinkId, setMesLinkId] = useState<number | null>(null);
  const [mesForm, setMesForm] = useState(emptyMes);
  /** 창을 열었을 때의 MES 칸. 이것과 같으면 MES 쪽은 건드리지 않는다. */
  const [mesInitial, setMesInitial] = useState<{ mode: 'none' | 'link' | 'new'; linkId: number | null; form: typeof emptyMes }>(
    { mode: 'none', linkId: null, form: emptyMes },
  );

  // 머리글이 화면 상단에 '붙은' 순간에만 라운드 → 직각 전환 (평소엔 라운드 유지)
  const [stuck, setStuck] = useState(false);
  const sentinelRef = useRef<HTMLDivElement | null>(null);
  useEffect(() => {
    const el = sentinelRef.current;
    if (!el) return;
    const ob = new IntersectionObserver(([e]) => setStuck(!e.isIntersecting), { threshold: 0 });
    ob.observe(el);
    return () => ob.disconnect();
  }, [isMobile]);

  // 전체 1회 로드 후 클라이언트 필터 (검색 즉시 반응, 타이핑마다 서버 호출 없음)
  const load = useCallback(async () => {
    setList(await api.get<Vendor[]>('/api/vendor'));
  }, []);
  useEffect(() => { load(); }, [load]);

  const loadMes = useCallback(async () => {
    try {
      const d = await api.get<{ customers: MesCustomer[]; lines: MesLine[] }>('/api/mes/setup/customers');
      setMesCustomers(d.customers); setMesLines(d.lines); setMesReadable(true);
    } catch {
      // MES 권한이 없는 계정도 업체 관리는 쓴다 — 그 칸만 감추고 나머지는 그대로 쓰게 둔다.
      setMesReadable(false);
    }
  }, []);
  useEffect(() => { void loadMes(); }, [loadMes]);

  const mesById = useCallback(
    (id: number | null) => (id === null ? null : mesCustomers.find(c => c.customerId === id) ?? null),
    [mesCustomers],
  );

  function openAdd() {
    setEditId(null); setForm(emptyForm);
    setAddrs([]); setMgrs([]);
    setMesMode('none'); setMesLinkId(null); setMesForm(emptyMes);
    setMesInitial({ mode: 'none', linkId: null, form: emptyMes });
    setModal(true);
  }
  function openEdit(v: Vendor) {
    setEditId(v.id);
    setForm({
      vendorName: v.vendorName, category: v.category, isWeekly: v.isWeekly, isFavorite: v.isFavorite,
      basePath: v.basePath, linkUrl: v.linkUrl, addresses: v.addresses, managers: v.managers,
      mesCustomerId: v.mesCustomerId,
    });
    setAddrs(parseAddrs(v.addresses));
    setMgrs(parseMgrs(v.managers));

    const linked = mesById(v.mesCustomerId);
    setMesMode(linked ? 'link' : 'none');
    setMesLinkId(linked?.customerId ?? null);
    setMesForm(linked
      ? {
          code: linked.customerCode,
          exportPrefix: linked.exportPrefix ?? '',
          lineDefinitionId: linked.lineDefinitionId === null ? '' : String(linked.lineDefinitionId),
          isActive: linked.isActive,
        }
      : emptyMes);
    setMesInitial({
      mode: linked ? 'link' : 'none',
      linkId: linked?.customerId ?? null,
      form: linked
        ? {
            code: linked.customerCode,
            exportPrefix: linked.exportPrefix ?? '',
            lineDefinitionId: linked.lineDefinitionId === null ? '' : String(linked.lineDefinitionId),
            isActive: linked.isActive,
          }
        : emptyMes,
    });
    setModal(true);
  }

  // ── 폴더 경로/링크 클립보드 복사 (http 환경 대비 fallback 포함) ──
  const [copied, setCopied] = useState('');
  async function copyText(key: string, text: string) {
    try {
      await navigator.clipboard.writeText(text);
    } catch {
      const ta = document.createElement('textarea');
      ta.value = text;
      document.body.appendChild(ta);
      ta.select();
      document.execCommand('copy');
      document.body.removeChild(ta);
    }
    setCopied(key);
    window.setTimeout(() => setCopied(c => (c === key ? '' : c)), 1500);
  }

  function copyChips(v: Vendor) {
    if (!v.basePath && !v.linkUrl) return <span className="vd-none">-</span>;
    return (
      <div className="vd-copy">
        {v.basePath && (
          <button type="button" className={`vd-chip ${copied === `${v.id}-f` ? 'ok' : ''}`} title={v.basePath}
            onClick={e => { e.stopPropagation(); copyText(`${v.id}-f`, v.basePath); }}>
            {copied === `${v.id}-f` ? '✓ 복사됨' : '폴더 복사'}
          </button>
        )}
        {v.linkUrl && (
          <a className="vd-chip vd-open" href={withProto(v.linkUrl)} target="_blank" rel="noreferrer"
            title="새 탭에서 열기" onClick={e => e.stopPropagation()}>열기 ↗</a>
        )}
      </div>
    );
  }
  /**
   * MES 쪽 자료를 먼저 맞추고, 이어진 번호를 돌려준다(없으면 null).
   * 순서가 중요하다 — 새로 만든 MES 업체의 번호를 알아야 업체에 이어 둘 수 있다.
   */
  async function syncMesCustomer(): Promise<number | null> {
    if (!mesReadable || mesMode === 'none') return null;

    // MES 칸을 건드리지 않았으면 MES 를 부르지 않는다 — 주소만 고친 사람이 MES 권한이 없다는 이유로
    // 저장이 막히면 안 된다(MES 자료를 고치는 것은 따로 켜 주는 권한이다).
    const unchanged = mesMode === mesInitial.mode
      && mesLinkId === mesInitial.linkId
      && mesForm.code.trim() === mesInitial.form.code.trim()
      && mesForm.exportPrefix.trim() === mesInitial.form.exportPrefix.trim()
      && mesForm.lineDefinitionId === mesInitial.form.lineDefinitionId
      && mesForm.isActive === mesInitial.form.isActive
      && form.vendorName.trim() === (mesById(mesLinkId)?.customerName ?? form.vendorName.trim());
    if (unchanged) return mesLinkId;

    const body = {
      customerCode: mesForm.code.trim(),
      customerName: form.vendorName.trim(),          // 이름은 포털 업체명을 따른다(한 업체이므로)
      exportPrefix: mesForm.exportPrefix.trim(),
      lineDefinitionId: mesForm.lineDefinitionId ? Number(mesForm.lineDefinitionId) : null,
    };

    if (mesMode === 'link' && mesLinkId !== null) {
      const before = mesById(mesLinkId);
      await api.put<Result>(`/api/mes/setup/customers/${mesLinkId}`, body);
      if (before && before.isActive !== mesForm.isActive) {
        await api.post<Result>(`/api/mes/setup/customers/${mesLinkId}/active`, { isActive: mesForm.isActive });
      }
      return mesLinkId;
    }

    // 새로 만들기 — 만든 번호를 돌려주지 않는 API 라, 만든 뒤 업체 코드로 찾아 잇는다.
    await api.post<Result>('/api/mes/setup/customers', body);
    const fresh = await api.get<{ customers: MesCustomer[]; lines: MesLine[] }>('/api/mes/setup/customers');
    setMesCustomers(fresh.customers); setMesLines(fresh.lines);
    return fresh.customers.find(c => c.customerCode === body.customerCode)?.customerId ?? null;
  }

  async function save(e: React.FormEvent) {
    e.preventDefault();
    try {
      if (mesMode !== 'none' && !mesForm.code.trim()) {
        alert('MES 업체 코드를 입력하세요. MES 를 쓰지 않는 업체면 연결을 "연결 안 함" 으로 두세요.');
        return;
      }

      const mesCustomerId = await syncMesCustomer();
      const body = {
        ...form,
        addresses: addrsToJson(addrs),
        managers: mgrsToJson(mgrs),
        // MES 를 읽을 수 없는 계정이 저장해도 이미 이어 둔 연결이 풀리면 안 된다.
        mesCustomerId: mesReadable ? mesCustomerId : form.mesCustomerId,
      };
      if (editId) await api.put(`/api/vendor/${editId}`, body);
      else await api.post('/api/vendor', body);
      setModal(false);
      await load();
      if (mesReadable) await loadMes();
    } catch (err) {
      alert(err instanceof Error ? err.message : '저장에 실패했습니다.');
    }
  }
  async function remove(v: Vendor) {
    if (!confirm(`'${v.vendorName}' 업체를 삭제할까요?`)) return;
    try {
      await api.del(`/api/vendor/${v.id}`);
      await load();
    } catch (err) {
      alert(err instanceof Error ? err.message : '삭제에 실패했습니다.');
    }
  }
  async function toggleFav(e: React.MouseEvent, v: Vendor) {
    e.stopPropagation();
    if (!canManage) return;
    try {
      // 즐겨찾기 전용 엔드포인트 — 다른 필드를 덮어쓰지 않음
      await api.post(`/api/vendor/${v.id}/favorite`, {});
      await load();
    } catch (err) {
      alert(err instanceof Error ? err.message : '즐겨찾기 변경에 실패했습니다.');
    }
  }

  const q = search.trim().toLowerCase();
  const shown = list.filter(v =>
    (cat === '전체' || (v.category || '일반') === cat) &&
    (q === '' ||
      v.vendorName.toLowerCase().includes(q) ||
      (v.category || '').toLowerCase().includes(q) ||
      summarize(v.addresses).toLowerCase().includes(q) ||
      summarize(v.managers).toLowerCase().includes(q)));

  return (
    <div>
      <header className="pg-header">
        <div><h2>업체 관리</h2></div>
        <input className="vd-search" placeholder="업체/분류/담당자/주소 검색" value={search} onChange={e => setSearch(e.target.value)} />
        {canManage && <button className="btn btn-primary" onClick={openAdd}>+ 업체 등록</button>}
      </header>
      <div className="pg-body">
        {(() => {
          const cats = [...new Set(list.map(v => v.category || '일반'))]
            .sort((a, b) => list.filter(v => (v.category || '일반') === b).length - list.filter(v => (v.category || '일반') === a).length);
          return (
            <div className="vd-cats-bar">
              <button className={`vd-cat ${cat === '전체' ? 'on' : ''}`} onClick={() => setCat('전체')}>전체 <i>{list.length}</i></button>
              {cats.map(c => (
                <button key={c} className={`vd-cat ${cat === c ? 'on' : ''}`} onClick={() => setCat(c)}>
                  {c} <i>{list.filter(v => (v.category || '일반') === c).length}</i>
                </button>
              ))}
            </div>
          );
        })()}
        {isMobile ? (
          <div className="vd-mlist">
            {shown.length === 0 && <div className="vd-empty">등록된 업체가 없습니다</div>}
            {shown.map(v => (
              <div key={v.id} className="vd-mcard">
                <div className="vd-mc-top">
                  <button className="vd-star" onClick={e => toggleFav(e, v)}>{v.isFavorite ? '★' : '☆'}</button>
                  <span className="vd-mc-name">{v.vendorName}</span>
                  {v.isWeekly && <span className="vd-weekly">주간세정</span>}
                </div>
                {v.category && <div className="vd-mc-cat">{v.category}</div>}
                {summarize(v.addresses) && <div className="vd-mc-row">📍 {summarize(v.addresses)}</div>}
                {mgrSummary(v.managers) && <div className="vd-mc-row">👤 {mgrSummary(v.managers)}</div>}
                {(v.basePath || v.linkUrl) && <div className="vd-mc-row">{copyChips(v)}</div>}
                {canManage && (
                  <div className="vd-mc-foot">
                    <button className="vd-sm" onClick={() => openEdit(v)}>수정</button>
                    <button className="vd-sm danger" onClick={() => remove(v)}>삭제</button>
                  </div>
                )}
              </div>
            ))}
          </div>
        ) : (
        <>
        <div ref={sentinelRef} aria-hidden style={{ height: 1 }} />
        <div className={`vd-table-wrap ${stuck ? 'stuck' : ''}`}>
          <table className="vd-table">
            <thead><tr><th style={{ width: 36 }}>★</th><th>업체명</th><th>분류</th>{mesReadable && <th>MES 코드</th>}<th>주간세정</th><th>주소</th><th>담당자</th><th>폴더/링크</th>{canManage && <th>관리</th>}</tr></thead>
            <tbody>
              {shown.length === 0 && <tr><td colSpan={canManage ? 8 : 7} className="vd-empty">등록된 업체가 없습니다</td></tr>}
              {shown.map(v => (
                <Fragment key={v.id}>
                  <tr className={`vd-clickable ${expand === v.id ? 'sel' : ''}`} onClick={() => setExpand(x => x === v.id ? null : v.id)}>
                    <td style={{ textAlign: 'center' }}><button className="vd-star" onClick={e => toggleFav(e, v)}>{v.isFavorite ? '★' : '☆'}</button></td>
                    <td className="vd-name">{v.vendorName}</td>
                    <td>{v.category}</td>
                    {mesReadable && (
                      <td className="vd-mes">
                        {mesById(v.mesCustomerId)?.customerCode ?? <span className="vd-mes-none">-</span>}
                      </td>
                    )}
                    <td>{v.isWeekly && <span className="vd-weekly">주간세정</span>}</td>
                    <td className="vd-note">{expand === v.id ? '' : (summarize(v.addresses) || '-')}</td>
                    <td className="vd-note">{expand === v.id ? '' : (mgrSummary(v.managers) || '-')}</td>
                    <td>{expand === v.id ? '' : copyChips(v)}</td>
                    {canManage && <td onClick={e => e.stopPropagation()}><div className="vd-actions"><button className="vd-sm" onClick={() => openEdit(v)}>수정</button><button className="vd-sm danger" onClick={() => remove(v)}>삭제</button></div></td>}
                  </tr>
                  {expand === v.id && (
                    <tr className="vd-detail-tr">
                      <td colSpan={canManage ? 8 : 7}>
                        <div className="vd-detail">
                          <div className="vd-d-col">
                            <h4>주소지 및 공장 정보</h4>
                            {parseAddrs(v.addresses).length === 0 && <p className="vd-none">등록된 주소 없음</p>}
                            {parseAddrs(v.addresses).map((a, i) => (
                              <div key={i} className="vd-d-row">
                                {a.isMain && <span className="vd-d-main">본사</span>}
                                {a.locationName && <b>{a.locationName}</b>}
                                <span className="vd-d-txt">{a.fullAddress || '-'}</span>
                                {a.fullAddress && (
                                  <button type="button" className={`vd-chip ${copied === `${v.id}-a${i}` ? 'ok' : ''}`}
                                    onClick={() => copyText(`${v.id}-a${i}`, a.fullAddress)}>
                                    {copied === `${v.id}-a${i}` ? '✓ 복사됨' : '복사'}
                                  </button>
                                )}
                              </div>
                            ))}
                          </div>
                          <div className="vd-d-col">
                            <h4>담당자 연락처</h4>
                            {parseMgrs(v.managers).length === 0 && <p className="vd-none">등록된 담당자 없음</p>}
                            {parseMgrs(v.managers).map((m, i) => (
                              <div key={i} className="vd-d-row">
                                <span className="vd-d-txt">{m.managerName || '-'}{m.contactNumber && ` (${m.contactNumber})`}</span>
                              </div>
                            ))}
                          </div>
                          <div className="vd-d-col">
                            <h4>폴더 / 링크</h4>
                            {!v.basePath && !v.linkUrl && <p className="vd-none">등록된 폴더/링크 없음</p>}
                            {v.basePath && (
                              <div className="vd-d-row">
                                <span className="vd-d-txt vd-d-path">{v.basePath}</span>
                                <button type="button" className={`vd-chip ${copied === `${v.id}-f2` ? 'ok' : ''}`}
                                  onClick={() => copyText(`${v.id}-f2`, v.basePath)}>
                                  {copied === `${v.id}-f2` ? '✓ 복사됨' : '복사'}
                                </button>
                              </div>
                            )}
                            {v.linkUrl && (
                              <div className="vd-d-row">
                                <span className="vd-d-txt vd-d-path">{v.linkUrl}</span>
                                <a className="vd-chip" href={withProto(v.linkUrl)} target="_blank" rel="noreferrer">열기 ↗</a>
                              </div>
                            )}
                          </div>
                        </div>
                      </td>
                    </tr>
                  )}
                </Fragment>
              ))}
            </tbody>
          </table>
        </div>
        </>
        )}
      </div>

      {modal && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setModal(false); }}>
          <form className="modal-box vd-modal" onSubmit={save}>
            <h3>{editId ? '업체 수정' : '업체 등록'}</h3>

            <div className="vd-f-grid">
              <div className="vd-f">
                <label>업체명 *</label>
                <input className="input" required autoFocus={!editId} value={form.vendorName} onChange={e => setForm({ ...form, vendorName: e.target.value })} />
              </div>
              <div className="vd-f">
                <label>분류</label>
                <input className="input" list="vd-cats" value={form.category} onChange={e => setForm({ ...form, category: e.target.value })} placeholder="일반 / QTZ / SEMES" />
                <datalist id="vd-cats">
                  {[...new Set(['일반', 'QTZ', 'SEMES', ...list.map(v => v.category).filter(Boolean)])].map(c => <option key={c} value={c} />)}
                </datalist>
              </div>
            </div>

            <div className="vd-checks">
              <label className="vd-check"><input type="checkbox" checked={form.isWeekly} onChange={e => setForm({ ...form, isWeekly: e.target.checked })} /> 주간세정 대상</label>
              <label className="vd-check"><input type="checkbox" checked={form.isFavorite} onChange={e => setForm({ ...form, isFavorite: e.target.checked })} /> 즐겨찾기</label>
            </div>

            {mesReadable && (
              <div className="vd-sec">
                <div className="vd-sec-head"><b>MES 업체 (생산관리)</b></div>

                <div className="vd-f">
                  <label>연결</label>
                  <select className="input" value={mesMode === 'new' ? 'new' : (mesLinkId === null ? '' : String(mesLinkId))}
                          onChange={e => {
                            const v = e.target.value;
                            if (v === '') { setMesMode('none'); setMesLinkId(null); setMesForm(emptyMes); return; }
                            if (v === 'new') {
                              setMesMode('new'); setMesLinkId(null); setMesForm(emptyMes); return;
                            }
                            const picked = mesById(Number(v));
                            setMesMode('link'); setMesLinkId(Number(v));
                            setMesForm(picked
                              ? {
                                  code: picked.customerCode,
                                  exportPrefix: picked.exportPrefix ?? '',
                                  lineDefinitionId: picked.lineDefinitionId === null ? '' : String(picked.lineDefinitionId),
                                  isActive: picked.isActive,
                                }
                              : emptyMes);
                          }}>
                    <option value="">연결 안 함</option>
                    <option value="new">+ MES 업체 새로 만들기</option>
                    {mesCustomers.map(c => (
                      <option key={c.customerId} value={c.customerId}>
                        {c.customerCode} — {c.customerName}{c.isActive ? '' : ' (중지)'}
                      </option>
                    ))}
                  </select>
                </div>

                {mesMode !== 'none' && (
                  <>
                    <div className="vd-f-grid">
                      <div className="vd-f">
                        <label>업체 코드 *</label>
                        <input className="input" value={mesForm.code}
                               onChange={e => setMesForm({ ...mesForm, code: e.target.value })} />
                      </div>
                      <div className="vd-f">
                        <label>반출번호 약어</label>
                        <input className="input" value={mesForm.exportPrefix}
                               onChange={e => setMesForm({ ...mesForm, exportPrefix: e.target.value })} />
                      </div>
                    </div>
                    <div className="vd-f">
                      <label>LINE</label>
                      <select className="input" value={mesForm.lineDefinitionId}
                              onChange={e => setMesForm({ ...mesForm, lineDefinitionId: e.target.value })}>
                        <option value="">-</option>
                        {mesLines.map(l => (
                          <option key={l.lineId} value={l.lineId}>{l.code} {l.description}</option>
                        ))}
                      </select>
                    </div>
                    {mesMode === 'link' && (
                      <label className="vd-check">
                        <input type="checkbox" checked={mesForm.isActive}
                               onChange={e => setMesForm({ ...mesForm, isActive: e.target.checked })} />
                        MES 에서 사용 (끄면 중지 — 지우지 않는다. 과거 LOT 이 이 업체를 가리킨다)
                      </label>
                    )}
                    <p className="vd-hint">MES 업체 이름은 위의 업체명을 그대로 따른다(한 업체이므로).</p>
                  </>
                )}
              </div>
            )}

            <div className="vd-sec">
              <div className="vd-sec-head">
                <b>주소지 및 공장 정보</b>
                <button type="button" className="vd-sm" onClick={() => setAddrs(a => [...a, { isMain: a.length === 0, locationName: '', fullAddress: '' }])}>+ 추가</button>
              </div>
              {addrs.length === 0 && <p className="vd-sec-empty">등록된 주소가 없습니다. '추가'를 눌러 입력하세요.</p>}
              {addrs.map((r, i) => (
                <div key={i} className="vd-row vd-row-addr">
                  <label className="vd-main-chk" title="본사">
                    <input type="checkbox" checked={r.isMain} onChange={e => setAddrs(a => a.map((x, xi) => xi === i ? { ...x, isMain: e.target.checked } : x))} />본사
                  </label>
                  <input className="input" placeholder="구분 (예: 본사/1공장)" value={r.locationName} onChange={e => setAddrs(a => a.map((x, xi) => xi === i ? { ...x, locationName: e.target.value } : x))} />
                  <input className="input" placeholder="전체 주소" value={r.fullAddress} onChange={e => setAddrs(a => a.map((x, xi) => xi === i ? { ...x, fullAddress: e.target.value } : x))} />
                  <button type="button" className="vd-row-del" onClick={() => setAddrs(a => a.filter((_, xi) => xi !== i))}>✕</button>
                </div>
              ))}
            </div>

            <div className="vd-sec">
              <div className="vd-sec-head">
                <b>담당자 연락처</b>
                <button type="button" className="vd-sm" onClick={() => setMgrs(m => [...m, { managerName: '', contactNumber: '' }])}>+ 추가</button>
              </div>
              {mgrs.length === 0 && <p className="vd-sec-empty">등록된 담당자가 없습니다. '추가'를 눌러 입력하세요.</p>}
              {mgrs.map((r, i) => (
                <div key={i} className="vd-row vd-row-mgr">
                  <input className="input" placeholder="성함" value={r.managerName} onChange={e => setMgrs(m => m.map((x, xi) => xi === i ? { ...x, managerName: e.target.value } : x))} />
                  <input className="input" placeholder="연락처 (010-0000-0000)" value={r.contactNumber} onChange={e => setMgrs(m => m.map((x, xi) => xi === i ? { ...x, contactNumber: e.target.value } : x))} />
                  <button type="button" className="vd-row-del" onClick={() => setMgrs(m => m.filter((_, xi) => xi !== i))}>✕</button>
                </div>
              ))}
            </div>

            <div className="vd-sec">
              <div className="vd-sec-head"><b>기본 저장 폴더 / 시스템 링크</b></div>
              <div className="vd-f">
                <label>폴더 경로</label>
                <input className="input" value={form.basePath} onChange={e => setForm({ ...form, basePath: e.target.value })}
                  placeholder="\\10.10.40.98\부서 공유 폴더\…" />
                <p className="vd-hint">탐색기에서 해당 폴더를 연 뒤 주소창의 경로를 복사해 붙여넣으세요. 목록의 '폴더 복사' 버튼으로 바로 복사해 탐색기에 붙여넣어 쓸 수 있습니다.</p>
              </div>
              <div className="vd-f">
                <label>업체 시스템 링크 (URL)</label>
                <input className="input" value={form.linkUrl} onChange={e => setForm({ ...form, linkUrl: e.target.value })}
                  placeholder="https://vendor.example.com (업체 자체 관리 시스템)" />
              </div>
            </div>

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
