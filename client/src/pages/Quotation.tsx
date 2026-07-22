import { useEffect, useState, useCallback, useRef } from 'react';
import { useAccess } from '../auth/useAccess';
import { useAuth } from '../auth/AuthContext';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { api } from '../api/client';
import type { QuotationSummary, Quotation as Q, ProductMaster } from '../api/types';
import stampUrl from '../assets/stamp.png';
import './Quotation.css';

// ── WPF QuotationView 이식: 업체 사이드바 + FIRM QUOTATION 양식 ──
type Row = { no: number; description: string; partCode: string; standardSpec: string; listPrice: number; qty: number; sel?: boolean };
const blankRow = (): Row => ({ no: 0, description: '', partCode: '', standardSpec: '', listPrice: 0, qty: 1 });
const won = (n: number) => n.toLocaleString('ko-KR');

type Head = {
  quoteNo: string; rfqNo: string; company: string; attention: string; email: string; phone: string;
  quoteDate: string; validity: string; aetsManager: string; aetsPhone: string; aetsEmail: string; businessNo: string;
  remarks: string; memo: string;
};
type VendorLite = { id: number; vendorName: string; category: string; isFavorite: boolean; managers: string };

function todayYmd(): string {
  return new Date(Date.now() - new Date().getTimezoneOffset() * 60000).toISOString().slice(0, 10);
}
function addDaysYmd(s: string, n: number): string {
  const d = new Date(s + 'T00:00:00');
  if (isNaN(d.getTime())) return '';
  d.setDate(d.getDate() + n);
  const p = (x: number) => String(x).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}
const blankHead = (): Head => ({
  quoteNo: '', rfqNo: '', company: '', attention: '', email: '', phone: '',
  quoteDate: todayYmd(), validity: addDaysYmd(todayYmd(), 7),   // WPF: 유효일 = 견적일 + 7일
  aetsManager: '', aetsPhone: '', aetsEmail: '', businessNo: '',
  remarks: '1. VAT 별도.', memo: '',                             // WPF 기본 비고
});

// 유효기간이 날짜 형식이면 만료 여부 판정 (자유 텍스트면 null)
function isExpired(validity: string): boolean | null {
  const m = /^(\d{4})-(\d{1,2})-(\d{1,2})$/.exec(validity.trim());
  if (!m) return null;
  return `${m[1]}-${m[2].padStart(2, '0')}-${m[3].padStart(2, '0')}` < todayYmd();
}

// 내용만큼 자동으로 늘어나는 textarea (비고/메모)
function GrowArea({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  const ref = useRef<HTMLTextAreaElement>(null);
  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    el.style.height = 'auto';
    el.style.height = `${el.scrollHeight + 4}px`;
  }, [value]);
  return <textarea ref={ref} className="input qt-remarks" rows={2} value={value} onChange={e => onChange(e.target.value)} />;
}

// 부분일치 자동완성 입력
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
  const [params, setParams] = useSearchParams();
  const [list, setList] = useState<QuotationSummary[]>([]);
  const [search, setSearch] = useState('');
  const [editing, setEditing] = useState<Q | 'new' | null>(null);
  const [head, setHead] = useState<Head>(blankHead());
  const [rows, setRows] = useState<Row[]>([blankRow()]);
  const [baseSnap, setBaseSnap] = useState('');
  const [saving, setSaving] = useState(false);
  const [exporting, setExporting] = useState(false);
  // 업체 사이드바 (WPF 업체 목록)
  const [vendors, setVendors] = useState<VendorLite[]>([]);
  const [vSearch, setVSearch] = useState('');
  const [selVendorName, setSelVendorName] = useState('전체');
  // 단가표 / 견적 설정 (사업자번호 기본값)
  const [products, setProducts] = useState<ProductMaster[]>([]);
  const [bizNoDefault, setBizNoDefault] = useState('');
  const [pickerOpen, setPickerOpen] = useState(false);   // '단가에서 추가' 모달
  const [pickerQ, setPickerQ] = useState('');

  const dirty = editing != null && baseSnap !== '' && JSON.stringify({ head, rows }) !== baseSnap;

  const load = useCallback(async () => {
    setList(await api.get<QuotationSummary[]>('/api/quotation'));
  }, []);
  useEffect(() => { load(); }, [load]);
  useEffect(() => {
    api.get<VendorLite[]>('/api/vendor').then(setVendors).catch(() => {});
    api.get<ProductMaster[]>('/api/quotationmaster/products').then(setProducts).catch(() => {});
    api.get<{ businessNo: string }>('/api/quotationmaster/config').then(c => setBizNoDefault(c.businessNo)).catch(() => {});
  }, []);

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
  const headVendor = vendors.find(v => v.vendorName === head.company);

  // 견적번호 자동 채번 (WPF GenerateQuoteNo): AETS + yymmdd + -NN
  function suggestQuoteNo(dateStr: string): string {
    if (!dateStr) return '';
    const prefix = `AETS${dateStr.replaceAll('-', '').slice(2)}-`;
    const max = list.reduce((m, q) => {
      if (q.quoteNo?.toUpperCase().startsWith(prefix)) {
        const parts = q.quoteNo.split('-');
        const n = parseInt(parts[parts.length - 1], 10);
        if (!isNaN(n)) return Math.max(m, n);
      }
      return m;
    }, 0);
    return `${prefix}${String(max + 1).padStart(2, '0')}`;
  }

  function snap(h: Head, rs: Row[]) { setBaseSnap(JSON.stringify({ head: h, rows: rs })); }

  // 견적 작성 (WPF BtnNewQuotation): 담당자 "이름 직급"·연락처·사업자번호·선택 업체 자동
  function startNew() {
    if (!canEdit) return;
    const mgr = user ? (user.jobTitle ? `${user.realName} ${user.jobTitle}` : user.realName) : '';
    const h: Head = {
      ...blankHead(),
      company: selVendorName !== '전체' ? selVendorName : '',
      aetsManager: mgr,
      aetsPhone: user?.phoneNumber ?? '',
      aetsEmail: user?.email ?? '',
      businessNo: bizNoDefault,
    };
    h.quoteNo = suggestQuoteNo(h.quoteDate);
    const rs = [blankRow()];
    setEditing('new'); setHead(h); setRows(rs); snap(h, rs);
    setParams({ edit: 'new' });
  }
  async function open(id: number) {
    try {
      const q = await api.get<Q>(`/api/quotation/${id}`);
      const h: Head = {
        quoteNo: q.quoteNo, rfqNo: q.rfqNo, company: q.company, attention: q.attention,
        email: q.email, phone: q.phone, quoteDate: q.quoteDate ?? '', validity: q.validity,
        aetsManager: q.aetsManager, aetsPhone: q.aetsPhone, aetsEmail: q.aetsEmail, businessNo: q.businessNo,
        remarks: q.remarks, memo: q.memo,
      };
      const rs = q.items.length
        ? q.items.map(i => ({ no: i.no, description: i.description, partCode: i.partCode, standardSpec: i.standardSpec, listPrice: i.listPrice, qty: i.qty }))
        : [blankRow()];
      setEditing(q); setHead(h); setRows(rs); snap(h, rs);
      setParams({ edit: String(id) });
    } catch (err) {
      alert(err instanceof Error ? err.message : '견적서를 불러오지 못했습니다.');
    }
  }
  function closeEditor() {
    if (dirty && !confirm('저장하지 않은 변경이 있습니다. 저장하지 않고 나갈까요?')) return;
    setEditing(null);
    setParams({}, { replace: true });
  }
  // 브라우저 뒤로 가기 → 편집 화면이면 목록으로 (URL ?edit= 파라미터와 동기화)
  useEffect(() => {
    const e = params.get('edit');
    if (editing && !e) {
      if (dirty && !confirm('저장하지 않은 변경이 있습니다. 저장하지 않고 나갈까요?')) {
        setParams({ edit: editing === 'new' ? 'new' : String(editing.id) }, { replace: true });
        return;
      }
      setEditing(null);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [params]);
  // 새로고침/직접 URL 진입 시 ?edit=N 이면 해당 견적 자동 열기
  const bootRef = useRef(false);
  useEffect(() => {
    if (bootRef.current) return;
    bootRef.current = true;
    const e = params.get('edit');
    if (e && e !== 'new') open(Number(e));
    else if (e === 'new') setParams({}, { replace: true });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  // 견적일 변경 → 유효일 +7일 자동 (WPF AutoFillValidity) + 신규면 견적번호 재제안
  function changeQuoteDate(v: string) {
    setHead(h => ({
      ...h,
      quoteDate: v,
      validity: v ? addDaysYmd(v, 7) : h.validity,
      quoteNo: editing === 'new' && (h.quoteNo === '' || /^AETS\d{6}-\d{2}$/.test(h.quoteNo)) ? suggestQuoteNo(v) : h.quoteNo,
    }));
  }

  function setRow(i: number, patch: Partial<Row>) { setRows(rs => rs.map((r, idx) => idx === i ? { ...r, ...patch } : r)); }
  function addRow() { setRows(rs => [...rs, blankRow()]); }
  function delSelected() {
    setRows(rs => {
      const left = rs.filter(r => !r.sel);
      return left.length > 0 ? left : [blankRow()];
    });
  }
  // 단가표 모달에서 품목 추가 — 마지막 빈 행이 있으면 거기부터 채움
  function addFromMaster(p: ProductMaster) {
    setRows(rs => {
      const idx = rs.findIndex(r => !r.description.trim() && !r.partCode.trim());
      const row: Row = { no: 0, description: p.productName, partCode: p.partCode, standardSpec: p.spec, listPrice: p.unitPrice, qty: 1 };
      return idx >= 0 ? rs.map((r, i) => i === idx ? row : r) : [...rs, row];
    });
  }

  const total = rows.reduce((s, r) => s + r.listPrice * r.qty, 0);
  const totalQty = rows.reduce((s, r) => s + (r.description.trim() || r.partCode.trim() ? r.qty : 0), 0);

  async function saveBizNoDefault() {
    try {
      await api.put('/api/quotationmaster/config', { businessNo: head.businessNo });
      setBizNoDefault(head.businessNo);
      alert('사업자번호 기본값이 저장되었습니다.');
    } catch (err) {
      alert(err instanceof Error ? err.message : '기본값 저장에 실패했습니다.');
    }
  }

  async function save() {
    if (!canEdit || saving) return;
    setSaving(true);
    try {
      const body = {
        ...head,
        quoteDate: head.quoteDate || null,
        items: rows.filter(r => r.description.trim() || r.partCode.trim())
          .map((r, i) => ({ no: r.no > 0 ? r.no : i + 1, description: r.description, partCode: r.partCode, standardSpec: r.standardSpec, listPrice: r.listPrice, qty: r.qty })),
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

  async function toggleVendorFav(e: React.MouseEvent, v: VendorLite) {
    e.stopPropagation();
    try {
      await api.post(`/api/vendor/${v.id}/favorite`, {});
      setVendors(vs => vs.map(x => x.id === v.id ? { ...x, isFavorite: !x.isFavorite } : x));
    } catch { /* 권한 없으면 무시 */ }
  }

  // AETS 로고 (SVG) — WPF 견적서 상단/로고 이미지의 벡터 재현
  const LOGO_SVG = `
<svg width="168" height="58" viewBox="0 0 336 116" xmlns="http://www.w3.org/2000/svg">
  <defs>
    <linearGradient id="aets-g" x1="0" y1="1" x2="1" y2="0">
      <stop offset="0" stop-color="#9CC7EA"/><stop offset="0.55" stop-color="#3D77BE"/><stop offset="1" stop-color="#1C4390"/>
    </linearGradient>
  </defs>
  <path d="M236 6 L236 74 L84 74 Z M212 40 L212 62 L163 62 Z" fill="#1C4390" fill-rule="evenodd"/>
  <polygon points="30,74 148,26 170,26 66,74" fill="url(#aets-g)"/>
  <text x="14" y="108" font-family="Arial, sans-serif" font-size="34" font-weight="bold" font-style="italic" fill="#1C4390" letter-spacing="2">AETS</text>
  <line x1="126" y1="84" x2="126" y2="110" stroke="#1C4390" stroke-width="2"/>
  <text x="136" y="96" font-family="Arial, sans-serif" font-size="13" font-weight="bold" fill="#333">Advanced Energy</text>
  <text x="136" y="111" font-family="Arial, sans-serif" font-size="13" font-weight="bold" fill="#333">Technology Solution</text>
</svg>`;

  // PDF 문서 HTML (WPF 엑셀 양식 재현: 헤더 기준정보 + FIRM QUOTATION + 서명부)
  function buildPdfHtml(): string {
    const esc = (s: string) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    const items = rows.filter(r => r.description.trim() || r.partCode.trim());
    const itemHtml = items.map((r, i) => `
      <tr><td>${i + 1}</td><td class="l">${esc(r.description)}</td><td>${esc(r.partCode)}</td>
      <td>${esc(r.standardSpec)}</td><td class="r">${won(r.listPrice)}</td><td class="r">${r.qty}</td>
      <td class="r">${won(r.listPrice * r.qty)}</td></tr>`).join('');
    const kv = (l: string, v: string) => `<div class="kv"><b>${esc(l)}</b><span>: ${esc(v)}</span></div>`;
    return `
<style>
  .qpdf { box-sizing: border-box; width: 794px; padding: 34px 42px 40px; background: #fff; color: #111;
    font-family: 'Malgun Gothic', 'Apple SD Gothic Neo', sans-serif; font-size: 12px; }
  .qpdf * { box-sizing: border-box; margin: 0; }
  .qpdf .hd { display: flex; align-items: center; justify-content: space-between;
    border: 1.5px solid #111; padding: 12px 16px; }
  .qpdf .hd-r { text-align: right; font-size: 12px; line-height: 1.7; font-weight: 600; }
  .qpdf h1 { text-align: center; font-size: 24px; letter-spacing: 3px; margin: 26px 0 18px; }
  .qpdf .meta { display: grid; grid-template-columns: 1fr 1fr; gap: 3px 46px; margin-bottom: 20px; }
  .qpdf .kv { display: flex; padding: 3px 2px; font-size: 12.5px; }
  .qpdf .kv b { width: 96px; flex-shrink: 0; }
  .qpdf table { width: 100%; border-collapse: collapse; margin-bottom: 18px; }
  .qpdf th { border-top: 2px solid #111; border-bottom: 1px solid #111; padding: 7px 6px; font-size: 11.5px; background: #F4F6F9; }
  .qpdf td { border-bottom: 1px solid #ddd; padding: 6px; text-align: center; font-size: 11.5px; }
  .qpdf td.l { text-align: left; } .qpdf td.r { text-align: right; white-space: nowrap; }
  .qpdf tfoot td { border-top: 2px solid #111; border-bottom: 2px solid #111; font-weight: 700; padding: 8px 6px; }
  .qpdf .rmk { display: flex; gap: 14px; margin-bottom: 44px; }
  .qpdf .rmk b { flex-shrink: 0; }
  .qpdf .rmk div { white-space: pre-wrap; }
  .qpdf .sign { display: flex; justify-content: space-between; align-items: flex-end; margin-bottom: 26px; }
  .qpdf .sg { width: 300px; }
  .qpdf .sg-t { font-style: italic; font-weight: 700; font-size: 13px; margin-bottom: 34px; }
  .qpdf .sg.to .sg-t { margin-bottom: 64px; }
  .qpdf .sg-line { border-bottom: 1.5px solid #111; }
  .qpdf .sg.from { text-align: center; position: relative; }
  .qpdf .sg-name { font-style: italic; font-weight: 800; font-size: 21px; margin-bottom: 2px; }
  .qpdf .sg-sub { font-weight: 700; font-size: 12px; padding-top: 4px; }
  .qpdf .sg-stamp { position: absolute; right: 8px; bottom: 14px; width: 84px; }
  .qpdf .co { text-align: center; font-size: 13px; font-weight: 600; }
</style>
<div class="qpdf">
  <div class="hd">
    ${LOGO_SVG}
    <div class="hd-r">충청남도 천안시 풍세산단로 222. (주)에이텍솔루션<br/>TEL : 070-4352-7427&nbsp;&nbsp;FAX : 031-8015-2078</div>
  </div>
  <h1>FIRM QUOTATION</h1>
  <div class="meta">
    ${kv('Quote No.', head.quoteNo)}${kv('Date', head.quoteDate)}
    ${kv('R(F)Q No', head.rfqNo)}${kv('Valid Until', head.validity)}
    ${kv('Company', head.company)}${kv('Our Contact', head.aetsManager)}
    ${kv('Attention', head.attention)}${kv('Phone', head.aetsPhone)}
    ${kv('Phone', head.phone)}${kv('Biz. No.', head.businessNo)}
  </div>
  <table>
    <thead><tr><th>Item</th><th>Product Description</th><th>Part No.</th><th>Standard Spec</th><th>List Price(₩)</th><th>Q'ty</th><th>Amount(₩)</th></tr></thead>
    <tbody>${itemHtml}</tbody>
    <tfoot><tr><td colspan="5" class="l"><b>Total</b></td><td class="r">${totalQty}</td><td class="r">${won(total)}</td></tr></tfoot>
  </table>
  <div class="rmk"><b>Remark</b><div>${(head.remarks || '').replace(/&/g, '&amp;').replace(/</g, '&lt;')}</div></div>
  <div class="sign">
    <div class="sg to"><div class="sg-t">Accepted by ;</div><div class="sg-line"></div></div>
    <div class="sg from">
      <div class="sg-t">Very truly yours ;</div>
      <div class="sg-name">BH.PARK</div>
      <div class="sg-line"></div>
      <div class="sg-sub">Advanced Energy Technology Solution</div>
      <img class="sg-stamp" src="${stampUrl}" alt=""/>
    </div>
  </div>
  <div class="co">AETS 주식회사</div>
</div>`;
  }

  // PDF 내보내기 — 문서를 그대로 PDF 파일로 저장 (다운로드)
  async function exportPdf() {
    if (exporting) return;
    setExporting(true);
    try {
      const host = document.createElement('div');
      host.style.cssText = 'position:fixed;left:-10000px;top:0;width:794px;background:#fff;';
      host.innerHTML = buildPdfHtml();
      document.body.appendChild(host);
      try {
        await Promise.all(Array.from(host.querySelectorAll('img')).map(img =>
          img.complete ? Promise.resolve() : new Promise(res => { img.onload = img.onerror = () => res(null); })));
        const [{ default: html2canvas }, { jsPDF }] = await Promise.all([import('html2canvas'), import('jspdf')]);
        const canvas = await html2canvas(host, { scale: 2, backgroundColor: '#ffffff' });
        const pdf = new jsPDF('p', 'mm', 'a4');
        const pageW = 210, pageH = 297;
        const pxPerMm = canvas.width / pageW;
        const pageHpx = Math.floor(pageH * pxPerMm);
        for (let y = 0, page = 0; y < canvas.height; y += pageHpx, page++) {
          const sliceH = Math.min(pageHpx, canvas.height - y);
          const c = document.createElement('canvas');
          c.width = canvas.width; c.height = sliceH;
          c.getContext('2d')!.drawImage(canvas, 0, y, canvas.width, sliceH, 0, 0, canvas.width, sliceH);
          if (page > 0) pdf.addPage();
          pdf.addImage(c.toDataURL('image/jpeg', 0.95), 'JPEG', 0, 0, pageW, sliceH / pxPerMm);
        }
        pdf.save(`${head.quoteNo || '견적서'}.pdf`);
      } finally {
        document.body.removeChild(host);
      }
    } catch (err) {
      alert(err instanceof Error ? err.message : 'PDF 내보내기에 실패했습니다.');
    } finally {
      setExporting(false);
    }
  }

  // ── 업체 사이드바 (목록/편집 공용) ──
  const vq = vSearch.trim().toLowerCase();
  const sideVendors = vendors
    .filter(v => vq === '' || v.vendorName.toLowerCase().includes(vq))
    .sort((a, b) => (b.isFavorite ? 1 : 0) - (a.isFavorite ? 1 : 0) || a.vendorName.localeCompare(b.vendorName));
  const sidebar = (
    <aside className="qt-side">
      <h3>업체 목록</h3>
      <input className="qt-vsearch" placeholder="업체 검색" value={vSearch} onChange={e => setVSearch(e.target.value)} />
      <div className="qt-vlist">
        <button className={`qt-vitem all ${selVendorName === '전체' ? 'on' : ''}`} onClick={() => setSelVendorName('전체')}>
          <span className="qt-vname">전체 보기</span>
          <span className="qt-vcat">최근 작성순 전체 견적서</span>
        </button>
        {sideVendors.map(v => (
          <button key={v.id} className={`qt-vitem ${selVendorName === v.vendorName ? 'on' : ''}`} onClick={() => setSelVendorName(v.vendorName)}>
            <span className="qt-vname">{v.vendorName}</span>
            {v.category && <span className="qt-vcat">{v.category}</span>}
            <span className={`qt-vstar ${v.isFavorite ? 'on' : ''}`} onClick={e => toggleVendorFav(e, v)}>{v.isFavorite ? '★' : '☆'}</span>
          </button>
        ))}
      </div>
    </aside>
  );

  if (editing) {
    return (
      <div>
        <header className="pg-header">
          <div>
            <h2>업체 견적서</h2>
            {editing !== 'new'
              ? <p>작성: {editing.createdBy || '-'}{editing.createdAt ? ` · ${editing.createdAt.slice(0, 16).replace('T', ' ')}` : ''}</p>
              : <p>새 견적서 작성</p>}
          </div>
          <button className="btn btn-ghost" onClick={exportPdf} disabled={exporting}>{exporting ? '생성 중...' : 'PDF 내보내기'}</button>
          {canEdit && <button className="btn btn-primary" onClick={save} disabled={saving}>{saving ? '저장 중...' : '저장'}</button>}
        </header>
        <div className="pg-body qt-layout">
          {sidebar}
          <div className="qt-editor">
            <div className="qt-card">
              <div className="qt-head2">
                <F l="Quote No. (견적번호)"><input className="input" value={head.quoteNo} onChange={e => setHead({ ...head, quoteNo: e.target.value })} /></F>
                <F l="Date (견적일)"><input className="input" type="date" value={head.quoteDate} onChange={e => changeQuoteDate(e.target.value)} /></F>
                <F l="R(F)Q No"><input className="input" value={head.rfqNo} onChange={e => setHead({ ...head, rfqNo: e.target.value })} /></F>
                <F l="Valid Until (유효일)"><input className="input" type="date" value={head.validity} onChange={e => setHead({ ...head, validity: e.target.value })} /></F>
                <F l="Company (회사명)">
                  <Suggest value={head.company} options={vendors.map(v => v.vendorName)}
                    onChange={v => setHead({ ...head, company: v })} placeholder="입력하면 등록 업체 검색" />
                </F>
                <F l="Biz. No. (AETS 사업자번호)">
                  <div className="qt-bizrow">
                    <input className="input" value={head.businessNo} onChange={e => setHead({ ...head, businessNo: e.target.value })} />
                    {canEdit && <button type="button" className="btn btn-ghost qt-bizsave" onClick={saveBizNoDefault}>기본값 저장</button>}
                  </div>
                </F>
                <F l="Attention (업체 담당자)">
                  <Suggest value={head.attention} options={headVendor ? mgrNames(headVendor.managers) : []}
                    onChange={v => setHead({ ...head, attention: v })}
                    placeholder={headVendor ? `${head.company} 담당자 검색` : '업체 선택 시 담당자 검색'} />
                </F>
                <F l="Our Contact (당사 담당자)"><input className="input" value={head.aetsManager} onChange={e => setHead({ ...head, aetsManager: e.target.value })} /></F>
                <F l="Phone (업체 연락처)"><input className="input" value={head.phone} onChange={e => setHead({ ...head, phone: e.target.value })} /></F>
                <F l="Phone (당사 연락처)"><input className="input" value={head.aetsPhone} onChange={e => setHead({ ...head, aetsPhone: e.target.value })} /></F>
              </div>
            </div>

            <div className="qt-card">
              <div className="qt-items-head">
                <h3>견적 품목</h3>
                <div className="qt-items-btns">
                  <button type="button" className="btn btn-ghost qt-ibtn" onClick={() => { setPickerQ(''); setPickerOpen(true); }}>단가에서 추가</button>
                  <button type="button" className="btn btn-ghost qt-ibtn" onClick={addRow}>+ 행 추가</button>
                  <button type="button" className="btn btn-ghost qt-ibtn danger" onClick={delSelected} disabled={!rows.some(r => r.sel)}>선택 삭제</button>
                </div>
              </div>
              <table className="qt-items">
                <thead><tr><th className="c-chk" /><th>No</th><th>Part's Name</th><th>품목코드</th><th>규격 (SIZE)</th><th>Q'ty</th><th>단가 (₩)</th><th>금액 (₩)</th></tr></thead>
                <tbody>
                  {rows.map((r, i) => (
                    <tr key={i} className={r.listPrice === 0 && (r.description.trim() || r.partCode.trim()) ? 'zero' : ''}>
                      <td className="c-chk"><input type="checkbox" checked={!!r.sel} onChange={e => setRow(i, { sel: e.target.checked })} /></td>
                      <td>{i + 1}</td>
                      <td>
                        <ProdSuggest value={r.description} products={products}
                          onChange={v => setRow(i, { description: v })}
                          onPick={p => setRow(i, { description: p.productName, partCode: p.partCode, standardSpec: p.spec, listPrice: p.unitPrice })} />
                      </td>
                      <td><input value={r.partCode} onChange={e => setRow(i, { partCode: e.target.value })} /></td>
                      <td><input value={r.standardSpec} onChange={e => setRow(i, { standardSpec: e.target.value })} /></td>
                      <td><input className="w-num" inputMode="numeric" value={r.qty === 0 ? '' : String(r.qty)}
                        onChange={e => { const n = Number(e.target.value.replace(/[^\d.]/g, '')); setRow(i, { qty: isNaN(n) ? 0 : n }); }} /></td>
                      <td><input className="w-price" inputMode="numeric" value={r.listPrice === 0 ? '' : won(r.listPrice)}
                        onChange={e => { const n = Number(e.target.value.replace(/[^\d]/g, '')); setRow(i, { listPrice: isNaN(n) ? 0 : n }); }} /></td>
                      <td className="qt-amt">{won(r.listPrice * r.qty)}</td>
                    </tr>
                  ))}
                </tbody>
                <tfoot>
                  <tr>
                    <td colSpan={5} className="qt-total-l">합계 (Total)</td>
                    <td className="qt-total-qty">수량 {totalQty}</td>
                    <td colSpan={2} className="qt-total">{won(total)} 원</td>
                  </tr>
                </tfoot>
              </table>
            </div>

            <div className="qt-card">
              <F l="비고 (Remarks)"><GrowArea value={head.remarks} onChange={v => setHead({ ...head, remarks: v })} /></F>
              <F l="메모 (내부용)"><GrowArea value={head.memo} onChange={v => setHead({ ...head, memo: v })} /></F>
            </div>

            <div className="qt-actions">
              {editing !== 'new' && canEdit && <button className="btn qt-delbtn" onClick={() => remove(editing.id)}>삭제</button>}
              <button className="btn btn-ghost" onClick={closeEditor}>취소</button>
              {canEdit && <button className="btn btn-primary" onClick={save} disabled={saving}>{saving ? '저장 중...' : '저장'}</button>}
            </div>
          </div>
        </div>

        {/* 단가에서 추가 모달 */}
        {pickerOpen && (
          <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setPickerOpen(false); }}>
            <div className="modal-box qt-picker">
              <h3>단가표에서 품목 추가</h3>
              <input className="input" autoFocus placeholder="품명 / 품번 검색" value={pickerQ} onChange={e => setPickerQ(e.target.value)} />
              <div className="qt-picker-list">
                {(() => {
                  const pq = pickerQ.trim().toLowerCase();
                  const shown = pq
                    ? products.filter(p => p.productName.toLowerCase().includes(pq) || p.partCode.toLowerCase().includes(pq))
                    : products;
                  if (shown.length === 0) return <p className="qt-empty">품목이 없습니다. '품목 단가표'에서 먼저 등록하세요.</p>;
                  return shown.slice(0, 100).map(p => (
                    <button type="button" key={p.id} className="qt-picker-item" onClick={() => addFromMaster(p)}>
                      <b>{p.productName}</b>
                      <span className="qt-sg-sub">{[p.partCode, p.spec].filter(Boolean).join(' · ')}</span>
                      <span className="qt-sg-price">{won(p.unitPrice)}원</span>
                    </button>
                  ));
                })()}
              </div>
              <p className="qt-picker-hint">클릭할 때마다 품목 행에 추가됩니다.</p>
              <div className="modal-actions"><button className="btn btn-ghost" onClick={() => setPickerOpen(false)}>닫기</button></div>
            </div>
          </div>
        )}
      </div>
    );
  }

  // ── 목록: 업체 사이드바 필터 + 검색 ──
  const q = search.trim().toLowerCase();
  const shown = list.filter(x =>
    (selVendorName === '전체' || x.company === selVendorName) &&
    (q === '' || [x.quoteNo, x.company, x.rfqNo, x.aetsManager].some(v => (v ?? '').toLowerCase().includes(q))));

  return (
    <div>
      <header className="pg-header">
        <div>
          <h2>업체 견적서</h2>
          <p>거래처별 견적서를 작성하고 단가를 일괄 관리합니다.</p>
        </div>
        <input className="qt-search" placeholder="견적번호/업체/RFQ 검색" value={search} onChange={e => setSearch(e.target.value)} />
        <button className="btn btn-ghost" onClick={() => nav('/product-master')}>품목 단가표</button>
        {canEdit && <button className="btn btn-primary" onClick={startNew}>+ 견적 작성</button>}
      </header>
      <div className="pg-body qt-layout">
        {sidebar}
        <div className="qt-main">
          <div className="qt-wrap">
            <table className="qt-list">
              <thead><tr><th>견적번호</th><th>RFQ</th><th>업체</th><th>견적일</th><th>유효기간</th><th>품목</th><th>합계</th><th>담당</th></tr></thead>
              <tbody>
                {shown.length === 0 && (
                  <tr><td colSpan={8} className="qt-empty">
                    {q ? '검색 결과가 없습니다' : selVendorName !== '전체' ? `${selVendorName} 견적서가 없습니다` : '견적서가 없습니다'}
                  </td></tr>
                )}
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
    </div>
  );
}

function F({ l, children }: { l: string; children: React.ReactNode }) {
  return <div className="qt-field"><label>{l}</label>{children}</div>;
}
