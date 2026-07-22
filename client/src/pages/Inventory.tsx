import { useEffect, useState, useCallback, useRef } from 'react';
import { api } from '../api/client';
import { useAccess } from '../auth/useAccess';
import { useIsMobile } from '../hooks/useIsMobile';
import type { InventoryZone, InventoryItem, InventorySnapshot } from '../api/types';
import { exportInventory, parseInventoryUpload, type StagedRow } from './inventoryExcel';
import './Inventory.css';

const emptyForm = {
  itemCode: '', category: '', unit: '', storageLocation: '', itemName: '',
  currentStock: '', appropriateStock: '', minOrderQty: '', supplier: '',
  orderDate: '', orderQty: '', expectedReceipt: '', memo: '', isOrdered: false,
};
type Form = typeof emptyForm;
type Tab = 'view' | 'analysis' | 'manage';
const ZONE_COLOR: Record<string, string> = { metal: '#5E8CC4', nonmetal: '#CC7DA6', office: '#5AA477', cleaning: '#9584CF' };
const parseNum = (s: string): number | null => {
  const m = (s ?? '').trim().replace(/,/g, '').match(/^(\d+(?:\.\d+)?)/);
  return m ? Number(m[1]) : null;
};

export default function Inventory() {
  const isMobile = useIsMobile();
  
  const { canEditField: canManage } = useAccess();

  const [tab, setTab] = useState<Tab>('view');
  const [zones, setZones] = useState<InventoryZone[]>([]);
  const [search, setSearch] = useState('');
  const [modal, setModal] = useState(false);
  const [editId, setEditId] = useState<number | null>(null);
  const [form, setForm] = useState<Form>(emptyForm);
  const [locations, setLocations] = useState<string[]>([]);
  const [staged, setStaged] = useState<StagedRow[]>([]);
  const [sel, setSel] = useState<Set<number>>(new Set());
  const [locOpen, setLocOpen] = useState(false);
  const [bulkOpen, setBulkOpen] = useState(false);
  const [snaps, setSnaps] = useState<InventorySnapshot[]>([]);
  const fileRef = useRef<HTMLInputElement>(null);

  const load = useCallback(async () => {
    setZones(await api.get<InventoryZone[]>(`/api/inventory?search=${encodeURIComponent(search)}`));
  }, [search]);
  useEffect(() => { load(); }, [load]);
  useEffect(() => { api.get<string[]>('/api/inventory/locations').then(setLocations).catch(() => {}); }, [zones]);
  useEffect(() => { if (tab === 'analysis') api.get<InventorySnapshot[]>('/api/inventory/snapshots').then(setSnaps).catch(() => {}); }, [tab, zones]);

  const allItems = zones.flatMap(z => z.items);

  function toForm(it: InventoryItem): Form {
    return {
      itemCode: it.itemCode, category: it.category, unit: it.unit, storageLocation: it.storageLocation, itemName: it.itemName,
      currentStock: it.currentStock, appropriateStock: it.appropriateStock, minOrderQty: it.minOrderQty, supplier: it.supplier,
      orderDate: it.orderDate, orderQty: it.orderQty, expectedReceipt: it.expectedReceipt, memo: it.memo, isOrdered: it.isOrdered,
    };
  }
  function openAdd() {
    if (!canManage) return; setEditId(null); setForm(emptyForm); setModal(true); }
  function openEdit(it: InventoryItem) {
    if (!canManage) return; setEditId(it.id); setForm(toForm(it)); setModal(true); }
  async function save(e: React.FormEvent) {
    e.preventDefault();
    if (editId) await api.put(`/api/inventory/${editId}`, form);
    else await api.post('/api/inventory', form);
    setModal(false); load();
  }
  async function patchItem(it: InventoryItem, patch: Partial<Form>) {
    if (!canManage) return;
    await api.put(`/api/inventory/${it.id}`, { ...toForm(it), ...patch }); load();
  }
  async function setOrdered(it: InventoryItem, isOrdered: boolean) {
    if (!canManage) return; await api.patch(`/api/inventory/${it.id}/ordered`, { isOrdered }); load(); }
  async function remove(it: InventoryItem) {
    if (!canManage) return;
    if (!confirm(`삭제하시겠습니까?\n${it.itemName}`)) return;
    await api.del(`/api/inventory/${it.id}`); load();
  }
  async function weeklyClose() {
    if (!canManage) return;
    if (!confirm('현재 재고를 이번 주 마감 스냅샷으로 저장할까요?\n(이후 "이전 재고 / 이전 대비 증감"의 기준이 됩니다)')) return;
    const r = await api.post<{ count: number }>('/api/inventory/snapshot', { date: null });
    alert(`주간 마감 완료: ${r.count}품목 스냅샷 저장`); load();
  }
  async function excelExport() {
    try { await exportInventory(zones); } catch (e) { alert('내보내기 실패: ' + (e instanceof Error ? e.message : e)); }
  }
  async function onUploadFile(e: React.ChangeEvent<HTMLInputElement>) {
    const f = e.target.files?.[0]; e.target.value = '';
    if (!f) return;
    try {
      const rows = await parseInventoryUpload(f, allItems);
      if (rows.length === 0) { alert('반영할 변경(체크 값)이 없습니다.'); return; }
      setStaged(rows);
    } catch (err) { alert('업로드 파싱 실패: ' + (err instanceof Error ? err.message : err)); }
  }
  async function confirmImport() {
    if (!canManage) return;
    const r = await api.post<{ count: number }>('/api/inventory/import/confirm', { items: staged.map(s => ({ id: s.id, stock: s.newStock })) });
    alert(`실사 반영 완료: ${r.count}품목`); setStaged([]); load();
  }

  // 관리 모드: 선택 / 삭제 / 일괄수정 / 위치관리
  function toggleSel(id: number) { setSel(p => { const s = new Set(p); s.has(id) ? s.delete(id) : s.add(id); return s; }); }
  async function deleteSelected() {
    if (sel.size === 0) return;
    if (!confirm(`선택한 ${sel.size}개 품목을 삭제할까요?`)) return;
    for (const id of sel) await api.del(`/api/inventory/${id}`);
    setSel(new Set()); load();
  }

  const totalItems = allItems.length;
  const lowCount = allItems.filter(i => i.isLow).length;
  const lastUpdated = allItems.reduce((m, i) => i.updatedAt > m ? i.updatedAt : m, '');
  const gated = (tab === 'analysis' || tab === 'manage') && !canManage;

  return (
    <div className="iv-page">
      <header className="pg-header">
        <div><h2>현장 재고관리</h2></div>
        <input className="iv-search" placeholder="품목/코드/분류/발주처 검색" value={search} onChange={e => setSearch(e.target.value)} />
        {tab === 'view' && <>
          <button className="btn btn-ghost" onClick={excelExport}>엑셀 내보내기</button>
          {canManage && <button className="btn btn-ghost" onClick={() => fileRef.current?.click()}>엑셀 업로드</button>}
          <input ref={fileRef} type="file" accept=".xlsx" style={{ display: 'none' }} onChange={onUploadFile} />
          {canManage && <button className="btn btn-ghost" onClick={weeklyClose}>주간 마감</button>}
        </>}
        {tab === 'manage' && canManage && <>
          <button className="btn btn-ghost" onClick={deleteSelected} disabled={sel.size === 0}>선택 삭제 ({sel.size})</button>
          <button className="btn btn-ghost" onClick={() => setBulkOpen(true)} disabled={sel.size === 0}>일괄 수정</button>
          <button className="btn btn-ghost" onClick={() => setLocOpen(true)}>위치 관리</button>
          <button className="btn btn-primary" onClick={openAdd}>+ 품목 등록</button>
        </>}
      </header>

      <div className="pg-body">
        <div className="iv-tabs">
          {([['view', '재고 현황'], ['analysis', '재고 분석'], ['manage', '리스트 관리']] as [Tab, string][]).map(([t, l]) => (
            <button key={t} className={`iv-tab ${tab === t ? 'on' : ''}`} onClick={() => setTab(t)}>{l}</button>
          ))}
        </div>

        {gated ? (
          <div className="iv-empty">이 탭은 재고관리 권한이 필요합니다. (관리자에게 문의)</div>
        ) : tab === 'analysis' ? (
          <Analysis zones={zones} items={allItems} snaps={snaps} />
        ) : (
          <>
            {staged.length > 0 && tab === 'view' && (
              <div className="iv-stage">
                <span className="iv-stage-txt">📋 실사 반영 대기 <b>{staged.length}건</b> — {staged.slice(0, 3).map(s => `${s.itemName}(${s.oldStock || '-'}→${s.newStock})`).join(', ')}{staged.length > 3 ? ' 외…' : ''}</span>
                <div className="iv-stage-btns">
                  <button className="btn btn-ghost" onClick={() => setStaged([])}>취소</button>
                  <button className="btn btn-primary" onClick={confirmImport}>업로드 확정</button>
                </div>
              </div>
            )}

            <div className="iv-stats">
              <div className="iv-stat"><span className="iv-stat-n">{totalItems}</span><span className="iv-stat-l">전체 품목</span></div>
              <div className={`iv-stat ${lowCount ? 'danger' : ''}`}><span className="iv-stat-n">{lowCount}</span><span className="iv-stat-l">재고 부족 (즉시 발주)</span></div>
              <div className="iv-stat"><span className="iv-stat-n small">{lastUpdated ? lastUpdated.slice(0, 10) : '-'}</span><span className="iv-stat-l">최근 업데이트</span></div>
            </div>

            {totalItems === 0 && <div className="iv-empty">{search ? '검색 결과가 없습니다' : '등록된 재고가 없습니다'}</div>}

            <div className="iv-zones">
              {zones.filter(z => z.items.length > 0).map(z => (
                <div key={z.zoneKey} className={`iv-zone zone-${z.zoneKey}`}>
                  <div className="iv-zone-head">
                    <span className="iv-zone-title">{z.zoneName}{z.locations && <em> · {z.locations}</em>}</span>
                    <span>{z.items.length}품목</span>
                  </div>
                  {isMobile ? (
                    <div className="iv-mlist">
                      {z.items.map(it => (
                        <div key={it.id} className={`iv-mcard ${it.isLow ? 'low' : ''}`}>
                          <div className="iv-mc-top">
                            <span className="iv-mc-name">{it.itemName}{it.category && <small> · {it.category}</small>}</span>
                            <label className="iv-ord"><input type="checkbox" checked={it.isOrdered} onChange={e => setOrdered(it, e.target.checked)} />발주완료</label>
                          </div>
                          <div className="iv-mc-body">
                            <span className="iv-mc-info">현재고 <b>{it.currentStockDisplay || '-'}</b></span>
                            <span className={`iv-mc-info ${it.weeklyDeltaIsDecrease ? 'down' : ''}`}>대비 {it.weeklyDeltaText}</span>
                            <span className="iv-mc-info">안전 {it.appropriateStock || '-'}</span>
                            <span className="iv-mc-info">발주처 {it.supplier || '-'}</span>
                          </div>
                          <div className="iv-mc-foot">
                            <button className="iv-sm" onClick={() => openEdit(it)}>수정</button>
                            <button className="iv-sm danger" onClick={() => remove(it)}>삭제</button>
                          </div>
                        </div>
                      ))}
                    </div>
                  ) : (
                    <div className="iv-tablewrap">
                      <table className="iv-table">
                        <thead>
                          <tr>
                            {tab === 'manage' && <th style={{ width: 30 }}></th>}
                            <th className="l">품목명</th><th>카테고리</th><th>현재 재고</th><th>이전 재고</th><th>이전 대비</th>
                            <th>안전재고</th><th>최소발주</th><th>단위</th><th>품목코드</th><th className="l">발주 회사</th><th>발주완료</th><th></th>
                          </tr>
                        </thead>
                        <tbody>
                          {z.items.map(it => (
                            <tr key={it.id} className={`${it.isLow ? 'low' : ''} ${sel.has(it.id) ? 'sel' : ''}`}>
                              {tab === 'manage' && <td><input type="checkbox" checked={sel.has(it.id)} onChange={() => toggleSel(it.id)} /></td>}
                              <td className="l iv-name">{it.itemName}</td>
                              <td>{it.category || '-'}</td>
                              <td><input className="iv-stock" defaultValue={it.currentStock} placeholder="-"
                                onBlur={e => { const v = e.target.value.trim(); if (v !== it.currentStock) patchItem(it, { currentStock: v }); }} /></td>
                              <td className="iv-dim">{it.previousStockDisplay || '-'}</td>
                              <td className={`iv-delta ${it.weeklyDeltaIsDecrease ? 'down' : it.weeklyDeltaText !== '-' && it.weeklyDeltaText !== '0' ? 'up' : ''}`}>{it.weeklyDeltaText}</td>
                              <td>{it.appropriateStock || '-'}</td>
                              <td className="iv-dim">{it.minOrderQty || '-'}</td>
                              <td>{it.unit || '-'}</td>
                              <td className="iv-dim">{it.itemCode || '-'}</td>
                              <td className="l">{it.supplier || '-'}</td>
                              <td><input type="checkbox" checked={it.isOrdered} onChange={e => setOrdered(it, e.target.checked)} /></td>
                              <td><div className="iv-actions"><button className="iv-sm" onClick={() => openEdit(it)}>수정</button><button className="iv-sm danger" onClick={() => remove(it)}>×</button></div></td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                </div>
              ))}
            </div>
          </>
        )}
      </div>

      {modal && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setModal(false); }}>
          <form className="modal-box iv-modal" onSubmit={save}>
            <h3>{editId ? '품목 수정' : '품목 등록'}</h3>
            <div className="iv-grid">
              <L l="품목명"><input className="input" required value={form.itemName} onChange={e => setForm({ ...form, itemName: e.target.value })} /></L>
              <L l="구역">
                <input className="input" list="iv-locs" value={form.storageLocation} onChange={e => setForm({ ...form, storageLocation: e.target.value })} placeholder="메탈 반입구 / 논메탈 반입구 …" />
                <datalist id="iv-locs">{locations.map(l => <option key={l} value={l} />)}</datalist>
              </L>
              <L l="카테고리"><input className="input" value={form.category} onChange={e => setForm({ ...form, category: e.target.value })} /></L>
              <L l="현재 재고"><input className="input" value={form.currentStock} onChange={e => setForm({ ...form, currentStock: e.target.value })} placeholder="예: 6 또는 600매 이상" /></L>
              <L l="안전재고"><input className="input" value={form.appropriateStock} onChange={e => setForm({ ...form, appropriateStock: e.target.value })} placeholder="예: 8EA" /></L>
              <L l="최소 발주량"><input className="input" value={form.minOrderQty} onChange={e => setForm({ ...form, minOrderQty: e.target.value })} placeholder="예: 10EA" /></L>
              <L l="단위"><input className="input" value={form.unit} onChange={e => setForm({ ...form, unit: e.target.value })} placeholder="숫자만 입력 시 붙는 단위 (예: EA)" /></L>
              <L l="품목코드"><input className="input" value={form.itemCode} onChange={e => setForm({ ...form, itemCode: e.target.value })} /></L>
              <L l="발주 회사"><input className="input" value={form.supplier} onChange={e => setForm({ ...form, supplier: e.target.value })} /></L>
              <L l="발주일"><input className="input" value={form.orderDate} onChange={e => setForm({ ...form, orderDate: e.target.value })} placeholder="예: 07-15" /></L>
              <L l="발주 수량"><input className="input" value={form.orderQty} onChange={e => setForm({ ...form, orderQty: e.target.value })} /></L>
              <L l="입고 예정"><input className="input" value={form.expectedReceipt} onChange={e => setForm({ ...form, expectedReceipt: e.target.value })} /></L>
            </div>
            <L l="비고"><input className="input" value={form.memo} onChange={e => setForm({ ...form, memo: e.target.value })} /></L>
            <label className="iv-ord modal-ord"><input type="checkbox" checked={form.isOrdered} onChange={e => setForm({ ...form, isOrdered: e.target.checked })} />발주 완료</label>
            <div className="modal-actions">
              <button type="button" className="btn btn-ghost" onClick={() => setModal(false)}>취소</button>
              <button type="submit" className="btn btn-primary">{editId ? '저장' : '등록'}</button>
            </div>
          </form>
        </div>
      )}

      {locOpen && <LocationModal locations={locations} onClose={() => setLocOpen(false)} onDone={() => { setLocOpen(false); load(); }} onAddItem={loc => { setLocOpen(false); setEditId(null); setForm({ ...emptyForm, storageLocation: loc }); setModal(true); }} zones={zones} />}
      {bulkOpen && <BulkModal ids={[...sel]} items={allItems.filter(i => sel.has(i.id))} onClose={() => setBulkOpen(false)} onDone={() => { setBulkOpen(false); setSel(new Set()); load(); }} />}
    </div>
  );
}

function L({ l, children }: { l: string; children: React.ReactNode }) {
  return <div className="iv-field"><label>{l}</label>{children}</div>;
}

// ── 위치 관리 ──
function LocationModal({ locations, zones, onClose, onDone, onAddItem }: {
  locations: string[]; zones: InventoryZone[]; onClose: () => void; onDone: () => void; onAddItem: (loc: string) => void;
}) {
  const count = (loc: string) => zones.flatMap(z => z.items).filter(i => i.storageLocation === loc).length;
  const [newLoc, setNewLoc] = useState('');
  async function rename(loc: string) {
    const nv = prompt(`위치 이름 변경: ${loc} →`, loc);
    if (!nv || nv.trim() === loc) return;
    await api.put(`/api/inventory/locations/${encodeURIComponent(loc)}`, { newName: nv.trim() }); onDone();
  }
  return (
    <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="modal-box iv-locmodal">
        <h3>위치 관리</h3>
        <div className="iv-loclist">
          {locations.map(l => (
            <div key={l} className="iv-locrow">
              <span className="iv-locname">{l} <small>{count(l)}품목</small></span>
              <button className="iv-sm" onClick={() => rename(l)}>이름 변경</button>
              <button className="iv-sm" onClick={() => onAddItem(l)}>품목 추가</button>
            </div>
          ))}
          {locations.length === 0 && <p className="iv-dim">등록된 위치가 없습니다.</p>}
        </div>
        <div className="iv-locadd">
          <input className="input" placeholder="새 위치 이름" value={newLoc} onChange={e => setNewLoc(e.target.value)} />
          <button className="btn btn-primary" disabled={!newLoc.trim()} onClick={() => onAddItem(newLoc.trim())}>새 위치로 품목 등록</button>
        </div>
        <div className="modal-actions"><button className="btn btn-ghost" onClick={onClose}>닫기</button></div>
      </div>
    </div>
  );
}

// ── 일괄 수정 ──
function BulkModal({ ids, items, onClose, onDone }: { ids: number[]; items: InventoryItem[]; onClose: () => void; onDone: () => void }) {
  const [field, setField] = useState<'currentStock' | 'appropriateStock' | 'unit' | 'category' | 'isOrdered'>('currentStock');
  const [value, setValue] = useState('');
  const [ordered, setOrdered] = useState(true);
  async function apply() {
    const body: Record<string, unknown> = { ids };
    if (field === 'isOrdered') body.isOrdered = ordered;
    else body[field] = value;
    await api.post('/api/inventory/bulk', body); onDone();
  }
  const labels: Record<string, string> = { currentStock: '현재 재고', appropriateStock: '안전재고', unit: '단위', category: '카테고리', isOrdered: '발주완료' };
  return (
    <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="modal-box iv-bulkmodal">
        <h3>일괄 수정 ({ids.length}개)</h3>
        <L l="대상 필드">
          <select className="input" value={field} onChange={e => setField(e.target.value as typeof field)}>
            {Object.entries(labels).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
          </select>
        </L>
        {field === 'isOrdered'
          ? <L l="값"><select className="input" value={ordered ? '1' : '0'} onChange={e => setOrdered(e.target.value === '1')}><option value="1">발주완료</option><option value="0">발주해제</option></select></L>
          : <L l="값"><input className="input" value={value} onChange={e => setValue(e.target.value)} placeholder="적용할 값" /></L>}
        <div className="iv-bulk-preview">
          <div className="iv-dim">선택 항목</div>
          {items.slice(0, 8).map(i => <span key={i.id} className="iv-bulk-chip">{i.itemName}</span>)}
          {items.length > 8 && <span className="iv-dim">외 {items.length - 8}개</span>}
        </div>
        <div className="modal-actions">
          <button className="btn btn-ghost" onClick={onClose}>취소</button>
          <button className="btn btn-primary" onClick={apply}>{labels[field]} 일괄 적용</button>
        </div>
      </div>
    </div>
  );
}

// ── 재고 분석 대시보드 ──
function Analysis({ zones, items, snaps }: { zones: InventoryZone[]; items: InventoryItem[]; snaps: InventorySnapshot[] }) {
  const dates = [...new Set(snaps.map(s => s.date))].sort();
  const totalByDate = dates.map(d => snaps.filter(s => s.date === d).reduce((sum, s) => sum + (parseNum(s.stock) ?? 0), 0));
  const zoneCounts = zones.filter(z => z.items.length > 0).map(z => ({ key: z.zoneKey, name: z.zoneName, count: z.items.length, low: z.items.filter(i => i.isLow).length }));
  const totalItems = items.length;
  const consume = items.map(i => ({ name: i.itemName, d: Number(i.weeklyDeltaText) }))
    .filter(x => Number.isFinite(x.d) && x.d < 0).sort((a, b) => a.d - b.d).slice(0, 10);

  return (
    <div className="iv-analysis">
      <div className="iv-stats">
        <div className="iv-stat"><span className="iv-stat-n">{dates.length}</span><span className="iv-stat-l">마감 데이터(주)</span></div>
        <div className="iv-stat danger"><span className="iv-stat-n">{items.filter(i => i.isLow).length}</span><span className="iv-stat-l">현재 부족</span></div>
        <div className="iv-stat"><span className="iv-stat-n">{totalItems}</span><span className="iv-stat-l">전체 품목</span></div>
        <div className="iv-stat"><span className="iv-stat-n">{zoneCounts.length}</span><span className="iv-stat-l">구역 수</span></div>
      </div>
      <div className="iv-charts">
        <div className="iv-chartcard">
          <h4>주간 재고 추이 (전체 합계)</h4>
          {dates.length < 2 ? <div className="iv-empty">주간 마감 스냅샷이 2건 이상 필요합니다.</div>
            : <LineChart dates={dates} values={totalByDate} />}
        </div>
        <div className="iv-chartcard">
          <h4>구역별 품목 분포</h4>
          {zoneCounts.length === 0 ? <div className="iv-empty">데이터 없음</div> : <Pie data={zoneCounts.map(z => ({ label: z.name, value: z.count, color: ZONE_COLOR[z.key] }))} />}
        </div>
        <div className="iv-chartcard">
          <h4>구역별 부족 vs 정상</h4>
          {zoneCounts.length === 0 ? <div className="iv-empty">데이터 없음</div> : <StackBars data={zoneCounts} />}
        </div>
        <div className="iv-chartcard">
          <h4>소비량 Top 10 (주간 감소)</h4>
          {consume.length === 0 ? <div className="iv-empty">주간 마감 후 소비가 잡히면 표시됩니다.</div> : <HBars data={consume.map(c => ({ label: c.name, value: -c.d }))} />}
        </div>
      </div>
    </div>
  );
}

function LineChart({ dates, values }: { dates: string[]; values: number[] }) {
  const W = Math.max(360, dates.length * 90), H = 200, pad = 26;
  const maxV = Math.max(1, ...values);
  const x = (i: number) => 36 + (dates.length === 1 ? W / 2 : (i * (W - 56)) / (dates.length - 1));
  const y = (v: number) => pad + (H - pad) * (1 - v / maxV);
  return (
    <div className="iv-chartsvg"><svg width={W} height={H + 26}>
      {[0, 0.5, 1].map(t => <line key={t} x1={32} x2={W} y1={y(maxV * t)} y2={y(maxV * t)} stroke="#E2E8F0" />)}
      {dates.map((d, i) => <text key={d} x={x(i)} y={H + 14} fontSize={9} fill="#475569" textAnchor="middle">{d.slice(5)}</text>)}
      <polyline fill="none" stroke="#4478AE" strokeWidth={2} points={values.map((v, i) => `${x(i)},${y(v)}`).join(' ')} />
      {values.map((v, i) => <circle key={i} cx={x(i)} cy={y(v)} r={3} fill="#4478AE" />)}
    </svg></div>
  );
}

function Pie({ data }: { data: { label: string; value: number; color: string }[] }) {
  const total = data.reduce((s, d) => s + d.value, 0) || 1;
  const cx = 80, cy = 80, r = 70;
  let a0 = -Math.PI / 2;
  const arcs = data.map(d => {
    const a1 = a0 + (d.value / total) * Math.PI * 2;
    const p = (a: number) => [cx + r * Math.cos(a), cy + r * Math.sin(a)];
    const [x1, y1] = p(a0), [x2, y2] = p(a1);
    const large = a1 - a0 > Math.PI ? 1 : 0;
    const path = `M${cx},${cy} L${x1.toFixed(1)},${y1.toFixed(1)} A${r},${r} 0 ${large} 1 ${x2.toFixed(1)},${y2.toFixed(1)} Z`;
    a0 = a1; return { path, ...d };
  });
  return (
    <div className="iv-pie">
      <svg width={160} height={160}>{arcs.map(a => <path key={a.label} d={a.path} fill={a.color} />)}</svg>
      <div className="iv-pie-leg">{data.map(d => <span key={d.label} className="iv-leg"><i style={{ background: d.color }} />{d.label} <b>{d.value}</b></span>)}</div>
    </div>
  );
}

function StackBars({ data }: { data: { name: string; count: number; low: number }[] }) {
  const maxV = Math.max(1, ...data.map(d => d.count));
  return (
    <div className="iv-stack">
      {data.map(d => (
        <div key={d.name} className="iv-stackrow">
          <span className="iv-stack-l">{d.name}</span>
          <div className="iv-stackbar">
            <div className="iv-seg low" style={{ width: `${(d.low / maxV) * 100}%` }} title={`부족 ${d.low}`} />
            <div className="iv-seg ok" style={{ width: `${((d.count - d.low) / maxV) * 100}%` }} title={`정상 ${d.count - d.low}`} />
          </div>
          <span className="iv-stack-n">{d.low}/{d.count}</span>
        </div>
      ))}
      <div className="iv-leg-row"><span className="iv-leg"><i style={{ background: '#C0453E' }} />부족</span><span className="iv-leg"><i style={{ background: '#94A3B8' }} />정상</span></div>
    </div>
  );
}

function HBars({ data }: { data: { label: string; value: number }[] }) {
  const maxV = Math.max(1, ...data.map(d => d.value));
  return (
    <div className="iv-hbars">
      {data.map(d => (
        <div key={d.label} className="iv-hbrow">
          <span className="iv-hb-l" title={d.label}>{d.label}</span>
          <div className="iv-hbtrack"><div className="iv-hbfill" style={{ width: `${(d.value / maxV) * 100}%` }} /></div>
          <span className="iv-hb-n">-{d.value}</span>
        </div>
      ))}
    </div>
  );
}
