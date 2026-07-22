import { useEffect, useState, useCallback } from 'react';
import { useAccess } from '../auth/useAccess';
import { useAuth } from '../auth/AuthContext';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import type { QuotationSummary, Quotation as Q, ProductMaster } from '../api/types';
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

// 자동 채번 형식 (AETSyymmdd-NN) — 이 형식일 때만 견적일 변경 시 재제안
const AUTO_NO = /^AETS\d{6}-\d{2}$/;

// 유효기간이 날짜 형식이면 만료 여부 판정 (자유 텍스트면 null)
function isExpired(validity: string): boolean | null {
  const m = /^(\d{4})-(\d{1,2})-(\d{1,2})$/.exec(validity.trim());
  if (!m) return null;
  const v = `${m[1]}-${m[2].padStart(2, '0')}-${m[3].padStart(2, '0')}`;
  const today = new Date(Date.now() - new Date().getTimezoneOffset() * 60000).toISOString().slice(0, 10);
  return v < today;
}

// 부분일치 자동완성 입력 (인수인계와 동일 패턴)
function Suggest({ value, onChange, options, placeholder }: {
  value: string; onChange: (v: string) => void; options: string[]; placeholder?: string;
}) {
  const [open, setOpen] = useState(false);
  const q = value.trim().toLowerCase();
  const filtered = q ? options.filter(o => o.toLowerCase().includes(q)) : options;
  return (
    <div className="qt-suggest">
      <input className="input" value={value} placeholder={placeholder}
        onChange={e => { onChange(e.target.value); setOpen(true); }}
        onFocus={() => setOpen(true)}
        onBlur={() => window.setTimeout(() => setOpen(false), 120)} />
      {open && filtered.length > 0 && (
        <div className="qt-suggest-pop">
          {filtered.slice(0, 30).map(o => (
            <button type="button" key={o} className="qt-suggest-item"
              onMouseDown={e => { e.preventDefault(); onChange(o); setOpen(false); }}>{o}</button>
          ))}
        </div>
      )}
    </div>
  );
}

// 품명 자동완성 — 단가표에서 선택하면 품번·규격·단가 자동 채움
function ProdSuggest({ value, onChange, products, onPick }: {
  value: string; onChange: (v: string) => void; products: ProductMaster[]; onPick: (p: ProductMaster) => void;
}) {
  const [open, setOpen] = useState(false);
  const q = value.trim().toLowerCase();
  const filtered = q
    ? products.filter(p => p.productName.toLowerCase().includes(q) || p.partCode.toLowerCase().includes(q))
    : [];
  return (
    <div className="qt-suggest">
      <input value={value} placeholder="품명 입력 → 단가표 검색"
        onChange={e => { onChange(e.target.value); setOpen(true); }}
        onFocus={() => setOpen(true)}
        onBlur={() => window.setTimeout(() => setOpen(false), 120)} />
      {open && filtered.length > 0 && (
        <div className="qt-suggest-pop wide">
          {filtered.slice(0, 20).map(p => (
            <button type="button" key={p.id} className="qt-suggest-item"
              onMouseDown={e => { e.preventDefault(); onPick(p); setOpen(false); }}>
              <b>{p.productName}</b>
              <span className="qt-sg-sub">{[p.partCode, p.spec].filter(Boolean).join(' · ')}</span>
              <span className="qt-sg-price">{won(p.unitPrice)}원</span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

export default function Quotation() {
  const { canEditOffice: canEdit } = useAccess();
  const { user } = useAuth();
  const nav = useNavigate();
  const [list, setList] = useState<QuotationSummary[]>([]);
  const [search, setSearch] = useState('');
  const [editing, setEditing] = useState<Q | 'new' | null>(null);
  const [head, setHead] = useState<Head>(blankHead());
  const [rows, setRows] = useState<Row[]>([blankRow()]);
  const [baseSnap, setBaseSnap] = useState('');   // 열림 시점 스냅샷 — dirty 판정
  const [saving, setSaving] = useState(false);
  // 자동완성 소스: 업체 관리 / 품목 단가표
  const [vendors, setVendors] = useState<{ vendorName: string; managers: string }[]>([]);
  const [products, setProducts] = useState<ProductMaster[]>([]);

  const dirty = editing != null && baseSnap !== '' && JSON.stringify({ head, rows }) !== baseSnap;

  const load = useCallback(async () => {
    setList(await api.get<QuotationSummary[]>('/api/quotation'));
  }, []);
  useEffect(() => { load(); }, [load]);
  useEffect(() => {
    api.get<{ vendorName: string; managers: string }[]>('/api/vendor')
      .then(vs => setVendors(vs.map(v => ({ vendorName: v.vendorName, managers: v.managers }))))
      .catch(() => { /* 자동완성은 부가 기능 */ });
    api.get<ProductMaster[]>('/api/quotationmaster/products').then(setProducts).catch(() => {});
  }, []);

  // 편집 중 미저장 이탈 경고 (새로고침/창 닫기)
  useEffect(() => {
    const h = (e: BeforeUnloadEvent) => { if (dirty) { e.preventDefault(); e.returnValue = ''; } };
    window.addEventListener('beforeunload', h);
    return () => window.removeEventListener('beforeunload', h);
  }, [dirty]);

  function mgrNames(json: string): string[] {
    if (!json) return [];
    try {
      const v = JSON.parse(json);
      if (!Array.isArray(v)) return [];
      return v
        .map(o => typeof o === 'string' ? o : String((o as Record<string, unknown>)?.ManagerName ?? (o as Record<string, unknown>)?.managerName ?? ''))
        .filter(Boolean);
    } catch { return []; }
  }
  const selVendor = vendors.find(v => v.vendorName === head.company);

  // 견적번호 자동 제안: AETS + yymmdd + -NN (해당 날짜 최대 순번 + 1)
  function suggestQuoteNo(dateStr: string): string {
    if (!dateStr) return '';
    const prefix = `AETS${dateStr.replaceAll('-', '').slice(2)}-`;
    const max = list.reduce((m, q) => {
      if (q.quoteNo?.startsWith(prefix)) {
        const n = parseInt(q.quoteNo.slice(prefix.length), 10);
        if (!isNaN(n)) return Math.max(m, n);
      }
      return m;
    }, 0);
    return `${prefix}${String(max + 1).padStart(2, '0')}`;
  }

  function snap(h: Head, rs: Row[]) { setBaseSnap(JSON.stringify({ head: h, rows: rs })); }

  function startNew() {
    if (!canEdit) return;
    const h = { ...blankHead(), aetsManager: user?.realName ?? '' };
    h.quoteNo = suggestQuoteNo(h.quoteDate);
    const rs = [blankRow()];
    setEditing('new'); setHead(h); setRows(rs); snap(h, rs);
  }
  async function open(id: number) {
    try {
      const q = await api.get<Q>(`/api/quotation/${id}`);
      const h: Head = {
        quoteNo: q.quoteNo, rfqNo: q.rfqNo, company: q.company, attention: q.attention,
        email: q.email, phone: q.phone, quoteDate: q.quoteDate ?? '', validity: q.validity,
        aetsManager: q.aetsManager, aetsPhone: q.aetsPhone, businessNo: q.businessNo,
        remarks: q.remarks, memo: q.memo,
      };
      const rs = q.items.length
        ? q.items.map(i => ({ no: i.no, description: i.description, partCode: i.partCode, standardSpec: i.standardSpec, listPrice: i.listPrice, qty: i.qty }))
        : [blankRow()];
      setEditing(q); setHead(h); setRows(rs); snap(h, rs);
    } catch (err) {
      alert(err instanceof Error ? err.message : '견적서를 불러오지 못했습니다.');
    }
  }
  // 편집 종료 — 변경사항 있으면 확인
  function closeEditor() {
    if (dirty && !confirm('저장하지 않은 변경이 있습니다. 저장하지 않고 나갈까요?')) return;
    setEditing(null);
  }
  // 견적일 변경: 신규 작성 중이고 번호가 자동 형식이면 재제안
  function changeQuoteDate(v: string) {
    setHead(h => ({
      ...h,
      quoteDate: v,
      quoteNo: editing === 'new' && (h.quoteNo === '' || AUTO_NO.test(h.quoteNo)) ? suggestQuoteNo(v) : h.quoteNo,
    }));
  }

  function setRow(i: number, patch: Partial<Row>) { setRows(rs => rs.map((r, idx) => idx === i ? { ...r, ...patch } : r)); }
  function addRow() { setRows(rs => [...rs, blankRow()]); }
  function delRow(i: number) { setRows(rs => rs.length > 1 ? rs.filter((_, idx) => idx !== i) : rs); }

  const total = rows.reduce((s, r) => s + r.listPrice * r.qty, 0);

  async function save() {
    if (!canEdit || saving) return;
    setSaving(true);
    try {
      const body = {
        ...head,
        quoteDate: head.quoteDate || null,
        items: rows.filter(r => r.description.trim() || r.partCode.trim())
          .map((r, i) => ({ ...r, no: r.no > 0 ? r.no : i + 1 })),
      };
      if (editing === 'new') await api.post('/api/quotation', body);
      else if (editing) await api.put(`/api/quotation/${editing.id}`, body);
      setEditing(null);
      await load();
    } catch (err) {
      alert(err instanceof Error ? err.message : '저장에 실패했습니다.');
    } finally {
      setSaving(false);
    }
  }
  async function remove(id: number) {
    if (!canEdit) return;
    if (!confirm('견적서를 삭제할까요?')) return;
    try {
      await api.del(`/api/quotation/${id}`);
      setEditing(null);
      await load();
    } catch (err) {
      alert(err instanceof Error ? err.message : '삭제에 실패했습니다.');
    }
  }

  if (editing) {
    return (
      <div>
        <header className="pg-header">
          <div><h2>{editing === 'new' ? '견적서 작성' : `견적서 · ${head.quoteNo || head.company}`}</h2></div>
          <button className="btn btn-ghost" onClick={closeEditor}>목록</button>
        </header>
        <div className="pg-body">
          <div className="qt-head">
            <F l="견적번호"><input className="input" value={head.quoteNo} onChange={e => setHead({ ...head, quoteNo: e.target.value })} /></F>
            <F l="RFQ 번호"><input className="input" value={head.rfqNo} onChange={e => setHead({ ...head, rfqNo: e.target.value })} /></F>
            <F l="업체명">
              <Suggest value={head.company} options={vendors.map(v => v.vendorName)}
                onChange={v => setHead({ ...head, company: v })} placeholder="입력하면 등록 업체 검색" />
            </F>
            <F l="수신 담당자">
              <Suggest value={head.attention} options={selVendor ? mgrNames(selVendor.managers) : []}
                onChange={v => setHead({ ...head, attention: v })}
                placeholder={selVendor ? `${head.company} 담당자 검색` : '업체 선택 시 담당자 검색'} />
            </F>
            <F l="이메일"><input className="input" value={head.email} onChange={e => setHead({ ...head, email: e.target.value })} /></F>
            <F l="전화"><input className="input" value={head.phone} onChange={e => setHead({ ...head, phone: e.target.value })} /></F>
            <F l="견적일"><input className="input" type="date" value={head.quoteDate} onChange={e => changeQuoteDate(e.target.value)} /></F>
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
                  <td>
                    <ProdSuggest value={r.description} products={products}
                      onChange={v => setRow(i, { description: v })}
                      onPick={p => setRow(i, { description: p.productName, partCode: p.partCode, standardSpec: p.spec, listPrice: p.unitPrice })} />
                  </td>
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
            <button className="btn btn-ghost" onClick={closeEditor}>취소</button>
            <button className="btn btn-primary" onClick={save} disabled={saving}>{saving ? '저장 중...' : '저장'}</button>
          </div>
        </div>
      </div>
    );
  }

  // 목록: 클라이언트 즉시 검색 (견적번호/업체/RFQ/담당)
  const q = search.trim().toLowerCase();
  const shown = q
    ? list.filter(x => [x.quoteNo, x.company, x.rfqNo, x.aetsManager].some(v => (v ?? '').toLowerCase().includes(q)))
    : list;

  return (
    <div>
      <header className="pg-header">
        <div><h2>업체 견적서</h2></div>
        <input className="qt-search" placeholder="견적번호/업체/RFQ 검색" value={search} onChange={e => setSearch(e.target.value)} />
        <button className="btn btn-ghost" onClick={() => nav('/product-master')}>품목 단가표</button>
        {canEdit && <button className="btn btn-primary" onClick={startNew}>+ 견적 작성</button>}
      </header>
      <div className="pg-body">
        <div className="qt-wrap">
        <table className="qt-list">
          <thead><tr><th>견적번호</th><th>RFQ</th><th>업체</th><th>견적일</th><th>유효기간</th><th>품목</th><th>합계</th><th>담당</th></tr></thead>
          <tbody>
            {shown.length === 0 && <tr><td colSpan={8} className="qt-empty">{q ? '검색 결과가 없습니다' : '견적서가 없습니다'}</td></tr>}
            {shown.map(x => (
              <tr key={x.id} onClick={() => open(x.id)} className={`qt-row ${isExpired(x.validity) === true ? 'expired' : ''}`}>
                <td className="qt-no">{x.quoteNo || '-'}</td>
                <td>{x.rfqNo || '-'}</td>
                <td>{x.company}</td>
                <td>{x.quoteDate ?? '-'}</td>
                <td>{x.validity || '-'}{isExpired(x.validity) === true && <span className="qt-expired-tag">만료</span>}</td>
                <td>{x.itemCount}건</td>
                <td className="qt-amt">{won(x.total)} 원</td>
                <td>{x.aetsManager || '-'}</td>
              </tr>
            ))}
          </tbody>
        </table>
        </div>
      </div>
    </div>
  );
}

function F({ l, children }: { l: string; children: React.ReactNode }) {
  return <div className="qt-field"><label>{l}</label>{children}</div>;
}
