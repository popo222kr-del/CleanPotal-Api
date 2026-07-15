import { useEffect, useState, useCallback } from 'react';
import { api } from '../api/client';
import { useIsMobile } from '../hooks/useIsMobile';
import type { InventoryZone, InventoryItem } from '../api/types';
import './Inventory.css';

const emptyForm = {
  itemCode: '', itemName: '', category: '', unit: 'EA', storageLocation: '',
  currentStock: 0, appropriateStock: 0, previousStock: 0, orderDate: '', expectedDate: '', note: '',
};

export default function Inventory() {
  const isMobile = useIsMobile();
  const [zones, setZones] = useState<InventoryZone[]>([]);
  const [search, setSearch] = useState('');
  const [modal, setModal] = useState(false);
  const [editId, setEditId] = useState<number | null>(null);
  const [form, setForm] = useState(emptyForm);

  const load = useCallback(async () => {
    setZones(await api.get<InventoryZone[]>(`/api/inventory?search=${encodeURIComponent(search)}`));
  }, [search]);
  useEffect(() => { load(); }, [load]);

  function openAdd() { setEditId(null); setForm(emptyForm); setModal(true); }
  function openEdit(it: InventoryItem) {
    setEditId(it.id);
    setForm({
      itemCode: it.itemCode, itemName: it.itemName, category: it.category, unit: it.unit,
      storageLocation: it.storageLocation, currentStock: it.currentStock, appropriateStock: it.appropriateStock,
      previousStock: it.previousStock, orderDate: it.orderDate ?? '', expectedDate: it.expectedDate ?? '', note: it.note,
    });
    setModal(true);
  }
  async function save(e: React.FormEvent) {
    e.preventDefault();
    const body = { ...form, orderDate: form.orderDate || null, expectedDate: form.expectedDate || null };
    if (editId) await api.put(`/api/inventory/${editId}`, body);
    else await api.post('/api/inventory', body);
    setModal(false); load();
  }
  async function setStock(it: InventoryItem, val: number) {
    await api.patch(`/api/inventory/${it.id}/stock`, { currentStock: val });
    load();
  }
  async function remove(it: InventoryItem) {
    if (!confirm('삭제하시겠습니까?')) return;
    await api.del(`/api/inventory/${it.id}`); load();
  }

  const totalItems = zones.reduce((s, z) => s + z.items.length, 0);

  return (
    <div>
      <header className="pg-header">
        <div><h2>현장 재고관리</h2></div>
        <input className="iv-search" placeholder="품목/코드/분류 검색" value={search} onChange={e => setSearch(e.target.value)} />
        <button className="btn btn-primary" onClick={openAdd}>+ 품목 등록</button>
      </header>
      <div className="pg-body">
        {totalItems === 0 && <div className="iv-empty">{search ? '검색 결과가 없습니다' : '등록된 재고가 없습니다'}</div>}
        <div className="iv-zones">
          {zones.map(z => (
            <div key={z.location} className="iv-zone">
              <div className="iv-zone-head">{z.location} <span>{z.items.length}품목</span></div>
              {isMobile ? (
                <div className="iv-mlist">
                  {z.items.map(it => (
                    <div key={it.id} className={`iv-mcard ${it.needsOrder ? 'low' : ''}`}>
                      <div className="iv-mc-top">
                        <span className="iv-mc-name">{it.itemName}{it.itemCode && <small> {it.itemCode}</small>}</span>
                        {it.category && <span className="iv-mc-cat">{it.category}</span>}
                      </div>
                      <div className="iv-mc-body">
                        <label className="iv-mc-stock">
                          현재고
                          <input className="iv-stock" type="number" defaultValue={it.currentStock}
                            onBlur={e => { const v = Number(e.target.value); if (v !== it.currentStock) setStock(it, v); }} />
                          <span className="iv-unit">{it.unit}</span>
                          {it.needsOrder && <span className="iv-warn" title="적정재고 미달">⚠</span>}
                        </label>
                        <span className="iv-mc-info">적정 {it.appropriateStock}</span>
                        <span className="iv-mc-info">입고예정 {it.expectedDate ?? '-'}</span>
                      </div>
                      <div className="iv-mc-foot">
                        <button className="iv-sm" onClick={() => openEdit(it)}>수정</button>
                        <button className="iv-sm danger" onClick={() => remove(it)}>삭제</button>
                      </div>
                    </div>
                  ))}
                </div>
              ) : (
              <table className="iv-table">
                <thead><tr><th>품목</th><th>분류</th><th>현재고</th><th>적정</th><th>입고예정</th><th></th></tr></thead>
                <tbody>
                  {z.items.map(it => (
                    <tr key={it.id} className={it.needsOrder ? 'low' : ''}>
                      <td className="iv-name">{it.itemName}{it.itemCode && <small> {it.itemCode}</small>}</td>
                      <td>{it.category || '-'}</td>
                      <td>
                        <input className="iv-stock" type="number" defaultValue={it.currentStock}
                          onBlur={e => { const v = Number(e.target.value); if (v !== it.currentStock) setStock(it, v); }} />
                        <span className="iv-unit">{it.unit}</span>
                        {it.needsOrder && <span className="iv-warn" title="적정재고 미달">⚠</span>}
                      </td>
                      <td className="iv-appro">{it.appropriateStock}</td>
                      <td>{it.expectedDate ?? '-'}</td>
                      <td><div className="iv-actions"><button className="iv-sm" onClick={() => openEdit(it)}>수정</button><button className="iv-sm danger" onClick={() => remove(it)}>×</button></div></td>
                    </tr>
                  ))}
                </tbody>
              </table>
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
              <L l="품목코드"><input className="input" value={form.itemCode} onChange={e => setForm({ ...form, itemCode: e.target.value })} /></L>
              <L l="분류"><input className="input" value={form.category} onChange={e => setForm({ ...form, category: e.target.value })} /></L>
              <L l="보관 구역"><input className="input" value={form.storageLocation} onChange={e => setForm({ ...form, storageLocation: e.target.value })} placeholder="반입구 / 오피스 등" /></L>
              <L l="단위"><input className="input" value={form.unit} onChange={e => setForm({ ...form, unit: e.target.value })} /></L>
              <L l="현재고"><input className="input" type="number" value={form.currentStock} onChange={e => setForm({ ...form, currentStock: Number(e.target.value) })} /></L>
              <L l="적정재고"><input className="input" type="number" value={form.appropriateStock} onChange={e => setForm({ ...form, appropriateStock: Number(e.target.value) })} /></L>
              <L l="이전재고"><input className="input" type="number" value={form.previousStock} onChange={e => setForm({ ...form, previousStock: Number(e.target.value) })} /></L>
              <L l="발주일"><input className="input" type="date" value={form.orderDate} onChange={e => setForm({ ...form, orderDate: e.target.value })} /></L>
              <L l="입고예정일"><input className="input" type="date" value={form.expectedDate} onChange={e => setForm({ ...form, expectedDate: e.target.value })} /></L>
            </div>
            <L l="비고"><input className="input" value={form.note} onChange={e => setForm({ ...form, note: e.target.value })} /></L>
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
