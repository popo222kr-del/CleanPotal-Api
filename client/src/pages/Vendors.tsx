import { useEffect, useState, useCallback } from 'react';
import { api } from '../api/client';
import { useAccess } from '../auth/useAccess';
import { useIsMobile } from '../hooks/useIsMobile';
import type { Vendor } from '../api/types';
import './Vendors.css';

const emptyForm = {
  vendorName: '', category: '일반', isWeekly: false, isFavorite: false,
  basePath: '', addresses: '', managers: '', contact: '', phone: '', note: '',
};

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
        const addr = pick(item, 'FullAddress');
        if (addr) { const loc = pick(item, 'LocationName'); return loc ? `${loc}: ${addr}` : addr; }
        const mgr = pick(item, 'ManagerName');
        if (mgr) { const tel = pick(item, 'ContactNumber'); return tel ? `${mgr} (${tel})` : mgr; }
        return Object.values(item).filter(Boolean).join(' / ');
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
  const [modal, setModal] = useState(false);
  const [editId, setEditId] = useState<number | null>(null);
  const [form, setForm] = useState(emptyForm);
  const [addrs, setAddrs] = useState<AddrRow[]>([]);
  const [mgrs, setMgrs] = useState<MgrRow[]>([]);

  const load = useCallback(async () => {
    setList(await api.get<Vendor[]>(`/api/vendor?search=${encodeURIComponent(search)}`));
  }, [search]);
  useEffect(() => { load(); }, [load]);

  function openAdd() {
    setEditId(null); setForm(emptyForm);
    setAddrs([]); setMgrs([]);
    setModal(true);
  }
  function openEdit(v: Vendor) {
    setEditId(v.id);
    setForm({
      vendorName: v.vendorName, category: v.category, isWeekly: v.isWeekly, isFavorite: v.isFavorite,
      basePath: v.basePath, addresses: v.addresses, managers: v.managers,
      contact: v.contact, phone: v.phone, note: v.note,
    });
    setAddrs(parseAddrs(v.addresses));
    setMgrs(parseMgrs(v.managers));
    setModal(true);
  }
  async function save(e: React.FormEvent) {
    e.preventDefault();
    const body = { ...form, addresses: addrsToJson(addrs), managers: mgrsToJson(mgrs) };
    if (editId) await api.put(`/api/vendor/${editId}`, body);
    else await api.post('/api/vendor', body);
    setModal(false); load();
  }
  async function remove(v: Vendor) {
    if (!confirm(`'${v.vendorName}' 업체를 삭제할까요?`)) return;
    await api.del(`/api/vendor/${v.id}`); load();
  }
  async function toggleFav(e: React.MouseEvent, v: Vendor) {
    e.stopPropagation();
    if (!canManage) return;
    await api.put(`/api/vendor/${v.id}`, { ...v, isFavorite: !v.isFavorite });
    load();
  }

  return (
    <div>
      <header className="pg-header">
        <div><h2>업체 관리</h2></div>
        <input className="vd-search" placeholder="업체/분류/담당자/주소 검색" value={search} onChange={e => setSearch(e.target.value)} />
        {canManage && <button className="btn btn-primary" onClick={openAdd}>+ 업체 등록</button>}
      </header>
      <div className="pg-body">
        {isMobile ? (
          <div className="vd-mlist">
            {list.length === 0 && <div className="vd-empty">등록된 업체가 없습니다</div>}
            {list.map(v => (
              <div key={v.id} className="vd-mcard">
                <div className="vd-mc-top">
                  <button className="vd-star" onClick={e => toggleFav(e, v)}>{v.isFavorite ? '★' : '☆'}</button>
                  <span className="vd-mc-name">{v.vendorName}</span>
                  {v.isWeekly ? <span className="vd-weekly">주간세정</span> : <span className="vd-normal">일반</span>}
                </div>
                {v.category && <div className="vd-mc-cat">{v.category}</div>}
                {summarize(v.addresses) && <div className="vd-mc-row">📍 {summarize(v.addresses)}</div>}
                {summarize(v.managers) && <div className="vd-mc-row">👤 {summarize(v.managers)}</div>}
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
        <div className="vd-table-wrap">
          <table className="vd-table">
            <thead><tr><th style={{ width: 36 }}>★</th><th>업체명</th><th>분류</th><th>주간세정</th><th>주소</th><th>담당자</th>{canManage && <th>관리</th>}</tr></thead>
            <tbody>
              {list.length === 0 && <tr><td colSpan={canManage ? 7 : 6} className="vd-empty">등록된 업체가 없습니다</td></tr>}
              {list.map(v => (
                <tr key={v.id}>
                  <td style={{ textAlign: 'center' }}><button className="vd-star" onClick={e => toggleFav(e, v)}>{v.isFavorite ? '★' : '☆'}</button></td>
                  <td className="vd-name">{v.vendorName}</td>
                  <td>{v.category}</td>
                  <td>{v.isWeekly ? <span className="vd-weekly">주간세정</span> : <span className="vd-normal">일반</span>}</td>
                  <td className="vd-note">{summarize(v.addresses) || '-'}</td>
                  <td className="vd-note">{summarize(v.managers) || '-'}</td>
                  {canManage && <td><div className="vd-actions"><button className="vd-sm" onClick={() => openEdit(v)}>수정</button><button className="vd-sm danger" onClick={() => remove(v)}>삭제</button></div></td>}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
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

            <details className="vd-adv" open={Boolean(form.basePath)}>
              <summary>기본 저장 폴더 (선택)</summary>
              <input className="input" value={form.basePath} onChange={e => setForm({ ...form, basePath: e.target.value })} placeholder="\\서버\공유폴더\… (파일 정리용)" />
            </details>

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
