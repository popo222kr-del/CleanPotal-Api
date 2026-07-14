import { useEffect, useState, useCallback } from 'react';
import { api } from '../api/client';
import type { QuotationSummary, Quotation as Q } from '../api/types';
import './Quotation.css';

type Row = { no: number; description: string; partCode: string; standardSpec: string; listPrice: number; qty: number };
const blankRow = (): Row => ({ no: 0, description: '', partCode: '', standardSpec: '', listPrice: 0, qty: 1 });
const won = (n: number) => n.toLocaleString('ko-KR');

type Head = {
  quoteNo: string; rfqNo: string; company: string; attention: string; email: string; phone: string;
  quoteDate: string; validity: string; aetsManager: string; aetsPhone: string; businessNo: string;
  remarks: string; memo: string;
};
const blankHead = (): Head => ({
  quoteNo: '', rfqNo: '', company: '', attention: '', email: '', phone: '',
  quoteDate: new Date(Date.now() - new Date().getTimezoneOffset() * 60000).toISOString().slice(0, 10), validity: '', aetsManager: '', aetsPhone: '', businessNo: '',
  remarks: '', memo: '',
});

export default function Quotation() {
  const [list, setList] = useState<QuotationSummary[]>([]);
  const [search, setSearch] = useState('');
  const [editing, setEditing] = useState<Q | 'new' | null>(null);
  const [head, setHead] = useState<Head>(blankHead());
  const [rows, setRows] = useState<Row[]>([blankRow()]);

  const load = useCallback(async () => {
    setList(await api.get<QuotationSummary[]>(`/api/quotation?search=${encodeURIComponent(search)}`));
  }, [search]);
  useEffect(() => { load(); }, [load]);

  function startNew() {
    setEditing('new');
    setHead(blankHead());
    setRows([blankRow()]);
  }
  async function open(id: number) {
    const q = await api.get<Q>(`/api/quotation/${id}`);
    setEditing(q);
    setHead({
      quoteNo: q.quoteNo, rfqNo: q.rfqNo, company: q.company, attention: q.attention,
      email: q.email, phone: q.phone, quoteDate: q.quoteDate ?? '', validity: q.validity,
      aetsManager: q.aetsManager, aetsPhone: q.aetsPhone, businessNo: q.businessNo,
      remarks: q.remarks, memo: q.memo,
    });
    setRows(q.items.length
      ? q.items.map(i => ({ no: i.no, description: i.description, partCode: i.partCode, standardSpec: i.standardSpec, listPrice: i.listPrice, qty: i.qty }))
      : [blankRow()]);
  }
  function setRow(i: number, patch: Partial<Row>) { setRows(rs => rs.map((r, idx) => idx === i ? { ...r, ...patch } : r)); }
  function addRow() { setRows(rs => [...rs, blankRow()]); }
  function delRow(i: number) { setRows(rs => rs.length > 1 ? rs.filter((_, idx) => idx !== i) : rs); }

  const total = rows.reduce((s, r) => s + r.listPrice * r.qty, 0);

  async function save() {
    const body = {
      ...head,
      quoteDate: head.quoteDate || null,
      items: rows.filter(r => r.description.trim() || r.partCode.trim())
        .map((r, i) => ({ ...r, no: r.no > 0 ? r.no : i + 1 })),
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
          <div><h2>💰 {editing === 'new' ? '견적서 작성' : `견적서 · ${head.quoteNo || head.company}`}</h2></div>
          <button className="btn btn-ghost" onClick={() => setEditing(null)}>목록</button>
        </header>
        <div className="pg-body">
          <div className="qt-head">
            <F l="견적번호"><input className="input" value={head.quoteNo} onChange={e => setHead({ ...head, quoteNo: e.target.value })} /></F>
            <F l="RFQ 번호"><input className="input" value={head.rfqNo} onChange={e => setHead({ ...head, rfqNo: e.target.value })} /></F>
            <F l="업체명"><input className="input" value={head.company} onChange={e => setHead({ ...head, company: e.target.value })} /></F>
            <F l="수신 담당자"><input className="input" value={head.attention} onChange={e => setHead({ ...head, attention: e.target.value })} /></F>
            <F l="이메일"><input className="input" value={head.email} onChange={e => setHead({ ...head, email: e.target.value })} /></F>
            <F l="전화"><input className="input" value={head.phone} onChange={e => setHead({ ...head, phone: e.target.value })} /></F>
            <F l="견적일"><input className="input" type="date" value={head.quoteDate} onChange={e => setHead({ ...head, quoteDate: e.target.value })} /></F>
            <F l="유효기간"><input className="input" value={head.validity} onChange={e => setHead({ ...head, validity: e.target.value })} placeholder="예: 견적일로부터 30일" /></F>
            <F l="당사 담당자"><input className="input" value={head.aetsManager} onChange={e => setHead({ ...head, aetsManager: e.target.value })} /></F>
            <F l="당사 연락처"><input className="input" value={head.aetsPhone} onChange={e => setHead({ ...head, aetsPhone: e.target.value })} /></F>
            <F l="사업자번호"><input className="input" value={head.businessNo} onChange={e => setHead({ ...head, businessNo: e.target.value })} /></F>
          </div>

          <table className="qt-items">
            <thead><tr><th>#</th><th>품명</th><th>품번</th><th>규격</th><th>단가</th><th>수량</th><th>금액</th><th></th></tr></thead>
            <tbody>
              {rows.map((r, i) => (
                <tr key={i} className={r.listPrice === 0 ? 'zero' : ''}>
                  <td>{i + 1}</td>
                  <td><input value={r.description} onChange={e => setRow(i, { description: e.target.value })} /></td>
                  <td><input value={r.partCode} onChange={e => setRow(i, { partCode: e.target.value })} /></td>
                  <td><input value={r.standardSpec} onChange={e => setRow(i, { standardSpec: e.target.value })} /></td>
                  <td><input className="w-price" type="number" value={r.listPrice} onChange={e => setRow(i, { listPrice: Number(e.target.value) })} /></td>
                  <td><input className="w-num" type="number" value={r.qty} onChange={e => setRow(i, { qty: Number(e.target.value) })} /></td>
                  <td className="qt-amt">{won(r.listPrice * r.qty)}</td>
                  <td><button className="qt-del" onClick={() => delRow(i)}>×</button></td>
                </tr>
              ))}
            </tbody>
            <tfoot><tr><td colSpan={6} className="qt-total-l">합계</td><td className="qt-total">{won(total)} 원</td><td /></tr></tfoot>
          </table>
          <button className="btn btn-ghost qt-addrow" onClick={addRow}>+ 품목 행 추가</button>

          <F l="비고"><textarea className="input qt-remarks" value={head.remarks} onChange={e => setHead({ ...head, remarks: e.target.value })} /></F>
          <F l="메모"><textarea className="input qt-remarks" value={head.memo} onChange={e => setHead({ ...head, memo: e.target.value })} /></F>

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
        <input className="qt-search" placeholder="견적번호/업체/RFQ 검색" value={search} onChange={e => setSearch(e.target.value)} />
        <button className="btn btn-primary" onClick={startNew}>+ 견적 작성</button>
      </header>
      <div className="pg-body">
        <table className="qt-list">
          <thead><tr><th>견적번호</th><th>RFQ</th><th>업체</th><th>견적일</th><th>유효기간</th><th>품목</th><th>합계</th><th>담당</th></tr></thead>
          <tbody>
            {list.length === 0 && <tr><td colSpan={8} className="qt-empty">견적서가 없습니다</td></tr>}
            {list.map(q => (
              <tr key={q.id} onClick={() => open(q.id)} className="qt-row">
                <td className="qt-no">{q.quoteNo || '-'}</td>
                <td>{q.rfqNo || '-'}</td>
                <td>{q.company}</td>
                <td>{q.quoteDate ?? '-'}</td>
                <td>{q.validity || '-'}</td>
                <td>{q.itemCount}건</td>
                <td className="qt-amt">{won(q.total)} 원</td>
                <td>{q.aetsManager || '-'}</td>
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
