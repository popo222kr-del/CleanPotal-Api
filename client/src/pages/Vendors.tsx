import { Fragment, useEffect, useState, useCallback } from 'react';
import { api } from '../api/client';
import { useAccess } from '../auth/useAccess';
import { useIsMobile } from '../hooks/useIsMobile';
import type { Vendor } from '../api/types';
import './Vendors.css';

const emptyForm = {
  vendorName: '', category: '일반', isWeekly: false, isFavorite: false,
  basePath: '', linkUrl: '', addresses: '', managers: '', contact: '', phone: '', note: '',
};

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
      basePath: v.basePath, linkUrl: v.linkUrl, addresses: v.addresses, managers: v.managers,
      contact: v.contact, phone: v.phone, note: v.note,
    });
    setAddrs(parseAddrs(v.addresses));
    setMgrs(parseMgrs(v.managers));
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

  const shown = cat === '전체' ? list : list.filter(v => (v.category || '일반') === cat);

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
                  {v.isWeekly ? <span className="vd-weekly">주간세정</span> : <span className="vd-normal">일반</span>}
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
        <div className="vd-table-wrap">
          <table className="vd-table">
            <thead><tr><th style={{ width: 36 }}>★</th><th>업체명</th><th>분류</th><th>주간세정</th><th>주소</th><th>담당자</th><th>폴더/링크</th>{canManage && <th>관리</th>}</tr></thead>
            <tbody>
              {shown.length === 0 && <tr><td colSpan={canManage ? 8 : 7} className="vd-empty">등록된 업체가 없습니다</td></tr>}
              {shown.map(v => (
                <Fragment key={v.id}>
                  <tr className={`vd-clickable ${expand === v.id ? 'sel' : ''}`} onClick={() => setExpand(x => x === v.id ? null : v.id)}>
                    <td style={{ textAlign: 'center' }}><button className="vd-star" onClick={e => toggleFav(e, v)}>{v.isFavorite ? '★' : '☆'}</button></td>
                    <td className="vd-name">{v.vendorName}</td>
                    <td>{v.category}</td>
                    <td>{v.isWeekly ? <span className="vd-weekly">주간세정</span> : <span className="vd-normal">일반</span>}</td>
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
