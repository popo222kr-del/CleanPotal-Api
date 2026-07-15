import { useEffect, useState, useCallback, useRef } from 'react';
import { api } from '../api/client';
import { useIsMobile } from '../hooks/useIsMobile';
import type { InventoryZone, InventoryItem } from '../api/types';
import { exportInventory, parseInventoryUpload, type StagedRow } from './inventoryExcel';
import './Inventory.css';

const emptyForm = {
  itemCode: '', category: '', unit: '', storageLocation: '', itemName: '',
  currentStock: '', appropriateStock: '', minOrderQty: '', supplier: '',
  orderDate: '', orderQty: '', expectedReceipt: '', memo: '', isOrdered: false,
};
type Form = typeof emptyForm;

export default function Inventory() {
  const isMobile = useIsMobile();
  const [zones, setZones] = useState<InventoryZone[]>([]);
  const [search, setSearch] = useState('');
  const [modal, setModal] = useState(false);
  const [editId, setEditId] = useState<number | null>(null);
  const [form, setForm] = useState<Form>(emptyForm);
  const [locations, setLocations] = useState<string[]>([]);
  const [staged, setStaged] = useState<StagedRow[]>([]);   // 엑셀 실사 스테이징 (DB 미반영)
  const fileRef = useRef<HTMLInputElement>(null);

  const load = useCallback(async () => {
    setZones(await api.get<InventoryZone[]>(`/api/inventory?search=${encodeURIComponent(search)}`));
  }, [search]);
  useEffect(() => { load(); }, [load]);
  useEffect(() => { api.get<string[]>('/api/inventory/locations').then(setLocations).catch(() => {}); }, [zones]);

  function toForm(it: InventoryItem): Form {
    return {
      itemCode: it.itemCode, category: it.category, unit: it.unit, storageLocation: it.storageLocation, itemName: it.itemName,
      currentStock: it.currentStock, appropriateStock: it.appropriateStock, minOrderQty: it.minOrderQty, supplier: it.supplier,
      orderDate: it.orderDate, orderQty: it.orderQty, expectedReceipt: it.expectedReceipt, memo: it.memo, isOrdered: it.isOrdered,
    };
  }
  function openAdd() { setEditId(null); setForm(emptyForm); setModal(true); }
  function openEdit(it: InventoryItem) { setEditId(it.id); setForm(toForm(it)); setModal(true); }

  async function save(e: React.FormEvent) {
    e.preventDefault();
    if (editId) await api.put(`/api/inventory/${editId}`, form);
    else await api.post('/api/inventory', form);
    setModal(false); load();
  }
  // 인라인 수정: 넘긴 필드만 덮어써 전체 업서트
  async function patchItem(it: InventoryItem, patch: Partial<Form>) {
    await api.put(`/api/inventory/${it.id}`, { ...toForm(it), ...patch });
    load();
  }
  async function setOrdered(it: InventoryItem, isOrdered: boolean) {
    await api.patch(`/api/inventory/${it.id}/ordered`, { isOrdered }); load();
  }
  async function remove(it: InventoryItem) {
    if (!confirm(`삭제하시겠습니까?\n${it.itemName}`)) return;
    await api.del(`/api/inventory/${it.id}`); load();
  }
  async function weeklyClose() {
    if (!confirm('현재 재고를 이번 주 마감 스냅샷으로 저장할까요?\n(이후 "이전 재고 / 이전 대비 증감"의 기준이 됩니다)')) return;
    const r = await api.post<{ count: number }>('/api/inventory/snapshot', { date: null });
    alert(`주간 마감 완료: ${r.count}품목 스냅샷 저장`);
    load();
  }
  // 엑셀 실사: ① 내보내기 ② 업로드(스테이징) ③ 확정(스냅샷+반영)
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
    const r = await api.post<{ count: number }>('/api/inventory/import/confirm', {
      items: staged.map(s => ({ id: s.id, stock: s.newStock })),
    });
    alert(`실사 반영 완료: ${r.count}품목 (오늘 마감 스냅샷 후 증감 산정)`);
    setStaged([]); load();
  }

  const allItems = zones.flatMap(z => z.items);
  const totalItems = allItems.length;
  const lowCount = allItems.filter(i => i.isLow).length;
  const lastUpdated = allItems.reduce((m, i) => i.updatedAt > m ? i.updatedAt : m, '');

  return (
    <div className="iv-page">
      <header className="pg-header">
        <div><h2>현장 재고관리</h2></div>
        <input className="iv-search" placeholder="품목/코드/분류/발주처 검색" value={search} onChange={e => setSearch(e.target.value)} />
        <button className="btn btn-ghost" onClick={excelExport}>엑셀 내보내기</button>
        <button className="btn btn-ghost" onClick={() => fileRef.current?.click()}>엑셀 업로드</button>
        <input ref={fileRef} type="file" accept=".xlsx" style={{ display: 'none' }} onChange={onUploadFile} />
        <button className="btn btn-ghost" onClick={weeklyClose}>주간 마감</button>
        <button className="btn btn-primary" onClick={openAdd}>+ 품목 등록</button>
      </header>

      <div className="pg-body">
        {staged.length > 0 && (
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
                        <th className="l">품목명</th><th>카테고리</th><th>현재 재고</th><th>이전 재고</th><th>이전 대비</th>
                        <th>안전재고</th><th>최소발주</th><th>단위</th><th>품목코드</th><th className="l">발주 회사</th><th>발주완료</th><th></th>
                      </tr>
                    </thead>
                    <tbody>
                      {z.items.map(it => (
                        <tr key={it.id} className={it.isLow ? 'low' : ''}>
                          <td className="l iv-name">{it.itemName}</td>
                          <td>{it.category || '-'}</td>
                          <td>
                            <input className="iv-stock" defaultValue={it.currentStock} placeholder="-"
                              onBlur={e => { const v = e.target.value.trim(); if (v !== it.currentStock) patchItem(it, { currentStock: v }); }} />
                          </td>
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
    </div>
  );
}

function L({ l, children }: { l: string; children: React.ReactNode }) {
  return <div className="iv-field"><label>{l}</label>{children}</div>;
}
