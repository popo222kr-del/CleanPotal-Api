import { useEffect, useState, useCallback } from 'react';
import { api } from '../api/client';
import type { QuotationSummary, Quotation as Q } from '../api/types';
import './Quotation.css';

const STATUSES = ['작성중', '발송', '확정'];
type Row = { itemName: string; spec: string; unit: string; quantity: number; unitPrice: number; remarks: string };
const blankRow = (): Row => ({ itemName: '', spec: '', unit: 'EA', quantity: 1, unitPrice: 0, remarks: '' });
const won = (n: number) => n.toLocaleString('ko-KR');

export default function Quotation() {
  const [list, setList] = useState<QuotationSummary[]>([]);
  const [search, setSearch] = useState('');
  const [editing, setEditing] = useState<Q | 'new' | null>(null);
  const [head, setHead] = useState({ quoteNo: '', vendorName: '', quoteDate: '', validUntil: '', status: '작성중', remarks: '' });
  const [rows, setRows] = useState<Row[]>([blankRow()]);

  const load = useCallback(async () => {
    setList(await api.get<QuotationSummary[]>(`/api/quotation?search=${encodeURIComponent(search)}`));
  }, [search]);
  useEffect(() => { load(); }, [load]);

  function startNew() {
    setEditing('new');
    setHead({ quoteNo: '', vendorName: '', quoteDate: new Date().toISOString().slice(0, 10), validUntil: '', status: '작성중', remarks: '' });
    setRows([blankRow()]);
  }
  async function open(id: number) {
    const q = await api.get<Q>(`/api/quotation/${id}`);
    setEditing(q);
    setHead({ quoteNo: q.quoteNo, vendorName: q.vendorName, quoteDate: q.quoteDate ?? '', validUntil: q.validUntil ?? '', status: q.status, remarks: q.remarks });
    setRows(q.items.length ? q.items.map(i => ({ itemName: i.itemName, spec: i.spec, unit: i.unit, quantity: i.quantity, unitPrice: i.unitPrice, remarks: i.remarks })) : [blankRow()]);
  }
  function setRow(i: number, patch: Partial<Row>) { setRows(rs => rs.map((r, idx) => idx === i ? { ...r, ...patch } : r)); }
  function addRow() { setRows(rs => [...rs, blankRow()]); }
  function delRow(i: number) { setRows(rs => rs.length > 1 ? rs.filter((_, idx) => idx !== i) : rs); }

  const total = rows.reduce((s, r) => s + r.quantity * r.unitPrice, 0);

  async function save() {
    const body = {
      ...head,
      quoteDate: head.quoteDate || null,
      validUntil: head.validUntil || null,
      items: rows.filter(r => r.itemName.trim()),
    };
    if (editing === 'new') await api.post('/api/quotation', body);
    else if (editing) await api.put(`/api/quotation/${editing.id}`, body);
    setEditing(null); load();
  }
  async function remove(id: number) {
    if (!confirm('견적서를 삭제할까요?')) return;
    await api.del(`/api/quotation/${id}`); setEditing(null); load();
  }

  if (editing) {
    return (
      <div>
        <header className="pg-header">
          <div><h2>💰 {editing === 'new' ? '견적서 작성' : `견적서 - ${head.quoteNo || head.vendorName}`}</h2></div>
          <button className="btn btn-ghost" onClick={() => setEditing(null)}>목록</button>
        </header>
        <div className="pg-body">
          <div className="qt-head">
            <F l="견적번호"><input className="input" value={head.quoteNo} onChange={e => setHead({ ...head, quoteNo: e.target.value })} /></F>
            <F l="업체명"><input className="input" value={head.vendorName} onChange={e => setHead({ ...head, vendorName: e.target.value })} /></F>
            <F l="견적일"><input className="input" type="date" value={head.quoteDate} onChange={e => setHead({ ...head, quoteDate: e.target.value })} /></F>
            <F l="유효일"><input className="input" type="date" value={head.validUntil} onChange={e => setHead({ ...head, validUntil: e.target.value })} /></F>
            <F l="상태"><select className="input" value={head.status} onChange={e => setHead({ ...head, status: e.target.value })}>{STATUSES.map(s => <option key={s}>{s}</option>)}</select></F>
          </div>

          <table className="qt-items">
            <thead><tr><th>#</th><th>품목</th><th>규격</th><th>단위</th><th>수량</th><th>단가</th><th>금액</th><th>비고</th><th></th></tr></thead>
            <tbody>
              {rows.map((r, i) => (
                <tr key={i} className={r.unitPrice === 0 ? 'zero' : ''}>
                  <td>{i + 1}</td>
                  <td><input value={r.itemName} onChange={e => setRow(i, { itemName: e.target.value })} /></td>
                  <td><input value={r.spec} onChange={e => setRow(i, { spec: e.target.value })} /></td>
                  <td><input className="w-unit" value={r.unit} onChange={e => setRow(i, { unit: e.target.value })} /></td>
                  <td><input className="w-num" type="number" value={r.quantity} onChange={e => setRow(i, { quantity: Number(e.target.value) })} /></td>
                  <td><input className="w-price" type="number" value={r.unitPrice} onChange={e => setRow(i, { unitPrice: Number(e.target.value) })} /></td>
                  <td className="qt-amt">{won(r.quantity * r.unitPrice)}</td>
                  <td><input value={r.remarks} onChange={e => setRow(i, { remarks: e.target.value })} /></td>
                  <td><button className="qt-del" onClick={() => delRow(i)}>×</button></td>
                </tr>
              ))}
            </tbody>
            <tfoot><tr><td colSpan={6} className="qt-total-l">합계</td><td className="qt-total">{won(total)} 원</td><td colSpan={2} /></tr></tfoot>
          </table>
          <button className="btn btn-ghost qt-addrow" onClick={addRow}>+ 품목 행 추가</button>

          <F l="비고"><textarea className="input qt-remarks" value={head.remarks} onChange={e => setHead({ ...head, remarks: e.target.value })} /></F>

          <div className="qt-actions">
            {editing !== 'new' && <button className="btn qt-delbtn" onClick={() => remove(editing.id)}>삭제</button>}
            <button className="btn btn-ghost" onClick={() => setEditing(null)}>취소</button>
            <button className="btn btn-primary" onClick={save}>저장</button>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div>
      <header className="pg-header">
        <div><h2>💰 업체 견적서</h2><p>견적 작성 및 단가 관리</p></div>
        <input className="qt-search" placeholder="견적번호/업체 검색" value={search} onChange={e => setSearch(e.target.value)} />
        <button className="btn btn-primary" onClick={startNew}>+ 견적 작성</button>
      </header>
      <div className="pg-body">
        <table className="qt-list">
          <thead><tr><th>견적번호</th><th>업체</th><th>견적일</th><th>유효일</th><th>품목</th><th>합계</th><th>상태</th><th>작성자</th></tr></thead>
          <tbody>
            {list.length === 0 && <tr><td colSpan={8} className="qt-empty">견적서가 없습니다</td></tr>}
            {list.map(q => (
              <tr key={q.id} onClick={() => open(q.id)} className="qt-row">
                <td className="qt-no">{q.quoteNo || '-'}</td>
                <td>{q.vendorName}</td>
                <td>{q.quoteDate ?? '-'}</td>
                <td>{q.validUntil ?? '-'}</td>
                <td>{q.itemCount}건</td>
                <td className="qt-amt">{won(q.total)} 원</td>
                <td><span className={`qt-status s-${q.status}`}>{q.status}</span></td>
                <td>{q.createdBy}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function F({ l, children }: { l: string; children: React.ReactNode }) {
  return <div className="qt-field"><label>{l}</label>{children}</div>;
}
