import { useEffect, useState, useCallback, useRef } from 'react';
import { useAccess } from '../auth/useAccess';
import { useAuth } from '../auth/AuthContext';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { api } from '../api/client';
import type { QuotationSummary, Quotation as Q, ProductMaster, QuotationConfig } from '../api/types';
import stampUrl from '../assets/stamp.png';
import logoUrl from '../assets/aets-logo.png';
import './Quotation.css';

// 양식 기준정보 기본값 — DB에 저장된 값이 있으면 그 값 우선
const DEFAULT_CFG: QuotationConfig = {
  businessNo: '',
  address: '충청남도 천안시 풍세산단로 222. (주)에이텍솔루션',
  tel: '070-4352-7427',
  fax: '031-8015-2078',
  signer: 'BH.PARK',
  companyName: 'AETS 주식회사',
};
const CFG_LABELS: [keyof QuotationConfig, string][] = [
  ['address', '주소'], ['tel', 'TEL'], ['fax', 'FAX'],
  ['businessNo', '사업자번호'], ['signer', '서명자'], ['companyName', '회사명'],
];

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
  // 단가표 / 견적 설정 (기준정보: 사업자번호·주소·전화·서명자 등)
  const [products, setProducts] = useState<ProductMaster[]>([]);
  const [cfg, setCfg] = useState<QuotationConfig>(DEFAULT_CFG);
  const [cfgOpen, setCfgOpen] = useState(false);         // 양식 기준정보 모달
  const [cfgDraft, setCfgDraft] = useState<QuotationConfig>(DEFAULT_CFG);
  const [cfgSaving, setCfgSaving] = useState(false);
  const cfgFileRef = useRef<HTMLInputElement>(null);
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
    api.get<QuotationConfig>('/api/quotationmaster/config').then(c => {
      // 저장된 값이 비어 있으면 기본값 유지
      setCfg(prev => {
        const merged = { ...prev };
        (Object.keys(DEFAULT_CFG) as (keyof QuotationConfig)[]).forEach(k => { if (c[k]) merged[k] = c[k]; });
        return merged;
      });
    }).catch(() => {});
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
      businessNo: cfg.businessNo,
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
      const next = { ...cfg, businessNo: head.businessNo };
      await api.put('/api/quotationmaster/config', next);
      setCfg(next);
      alert('사업자번호 기본값이 저장되었습니다.');
    } catch (err) {
      alert(err instanceof Error ? err.message : '기본값 저장에 실패했습니다.');
    }
  }

  // ── 양식 기준정보 (주소·TEL·FAX·서명자 등) — 엑셀 기준파일 다운로드/업로드 ──
  function openCfg() { setCfgDraft(cfg); setCfgOpen(true); }
  async function saveCfg() {
    if (cfgSaving) return;
    setCfgSaving(true);
    try {
      await api.put('/api/quotationmaster/config', cfgDraft);
      setCfg(cfgDraft);
      setCfgOpen(false);
    } catch (err) {
      alert(err instanceof Error ? err.message : '기준정보 저장에 실패했습니다.');
    } finally {
      setCfgSaving(false);
    }
  }
  // 현재 기준정보를 엑셀 원본(기준파일)으로 다운로드
  async function downloadCfgExcel() {
    try {
      const ExcelJS = (await import('exceljs')).default;
      const wb = new ExcelJS.Workbook();
      const ws = wb.addWorksheet('기준정보');
      ws.columns = [{ width: 16 }, { width: 60 }];
      const hd = ws.addRow(['항목', '값']);
      hd.font = { bold: true };
      CFG_LABELS.forEach(([key, label]) => ws.addRow([label, cfgDraft[key]]));
      ws.eachRow(r => r.eachCell(c => { c.border = { bottom: { style: 'thin' } }; }));
      const buf = await wb.xlsx.writeBuffer();
      const a = document.createElement('a');
      a.href = URL.createObjectURL(new Blob([buf], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }));
      a.download = '견적서_기준정보.xlsx';
      a.click();
      URL.revokeObjectURL(a.href);
    } catch (err) {
      alert(err instanceof Error ? err.message : '기준파일 다운로드에 실패했습니다.');
    }
  }
  // 수정한 기준파일 업로드 → 항목별 값 반영 (저장 버튼으로 확정)
  async function uploadCfgExcel(file: File) {
    try {
      const ExcelJS = (await import('exceljs')).default;
      const wb = new ExcelJS.Workbook();
      await wb.xlsx.load(await file.arrayBuffer());
      const ws = wb.worksheets[0];
      if (!ws) throw new Error('시트를 찾을 수 없습니다.');
      const next = { ...cfgDraft };
      let matched = 0;
      ws.eachRow(row => {
        const label = String(row.getCell(1).value ?? '').trim();
        const value = String(row.getCell(2).value ?? '').trim();
        const hit = CFG_LABELS.find(([, l]) => l === label);
        if (hit) { next[hit[0]] = value; matched++; }
      });
      if (matched === 0) throw new Error('기준파일 형식이 아닙니다. (항목/값 시트 필요)');
      setCfgDraft(next);
      alert(`기준파일에서 ${matched}개 항목을 불러왔습니다. 저장을 눌러 반영하세요.`);
    } catch (err) {
      alert(err instanceof Error ? err.message : '기준파일을 읽지 못했습니다.');
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

  // PDF 문서 HTML — 상단 기준정보 박스 + FIRM QUOTATION(수신/발신) + 하단 서명부(용지 맨 아래 고정)
  function buildPdfHtml(): string {
    const esc = (s: string) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    const items = rows.filter(r => r.description.trim() || r.partCode.trim());
    const itemHtml = items.map((r, i) => `
      <tr><td>${i + 1}</td><td class="l">${esc(r.description)}</td><td>${esc(r.partCode)}</td>
      <td>${esc(r.standardSpec)}</td><td class="r">${r.qty}</td>
      <td class="r">${won(r.listPrice)}</td><td class="r">${won(r.listPrice * r.qty)}</td></tr>`).join('');
    const kv = (l: string, v: string) => `<div class="kv"><b>${esc(l)}</b><span>${esc(v)}</span></div>`;
    return `
<style>
  .qpdf { box-sizing: border-box; width: 794px; min-height: 1123px; padding: 34px 42px 34px; background: #fff; color: #111;
    font-family: 'Malgun Gothic', 'Apple SD Gothic Neo', sans-serif; font-size: 12px; display: flex; flex-direction: column; }
  .qpdf * { box-sizing: border-box; margin: 0; }
  .qpdf .hd { display: flex; align-items: flex-end; justify-content: space-between;
    border-bottom: 2.5px solid #1C4390; padding: 0 2px 10px; }
  .qpdf .hd img { width: 118px; }
  .qpdf .hd-r { text-align: right; font-size: 12.5px; line-height: 1.8; font-weight: 400; color: #333; }
  .qpdf h1 { text-align: center; font-size: 25px; letter-spacing: 7px; margin-top: 46px; }
  .qpdf .rule { border-bottom: 3px solid #111; margin: 10px 0 18px; }
  .qpdf .meta { display: grid; grid-template-columns: 1fr 1fr; gap: 2px 40px; margin-bottom: 16px; }
  .qpdf .cols { display: grid; grid-template-columns: 1fr 1fr; gap: 0 40px; margin-bottom: 18px; }
  .qpdf .cols h3 { font-size: 12px; border-bottom: 2px solid #111; padding-bottom: 4px; margin-bottom: 4px; }
  .qpdf .kv { display: flex; border-bottom: 1px solid #ddd; padding: 5px 2px; }
  .qpdf .kv b { width: 105px; flex-shrink: 0; }
  .qpdf .kv span { flex: 1; word-break: break-all; }
  .qpdf table { width: 100%; border-collapse: collapse; margin-bottom: 16px; }
  .qpdf th { border-top: 2px solid #111; border-bottom: 1px solid #111; padding: 7px 6px; font-size: 11.5px; }
  .qpdf td { border-bottom: 1px solid #ddd; padding: 6px; text-align: center; font-size: 11.5px; }
  .qpdf td.l { text-align: left; } .qpdf td.r { text-align: right; white-space: nowrap; }
  .qpdf tfoot td { border-top: 2px solid #111; border-bottom: none; font-weight: 700; padding: 8px 6px; }
  .qpdf h4 { font-size: 12px; border-bottom: 2px solid #111; padding-bottom: 4px; margin-bottom: 6px; }
  .qpdf .remarks { white-space: pre-wrap; padding: 2px 2px 8px; min-height: 48px; line-height: 1.8; }
  .qpdf .foot { margin-top: auto; padding-top: 30px; }
  .qpdf .sign { display: flex; justify-content: space-between; align-items: flex-end; margin-bottom: 24px; }
  .qpdf .sg { width: 300px; position: relative; }
  .qpdf .sg-t { font-style: italic; font-weight: 700; font-size: 13px; margin-bottom: 26px; }
  .qpdf .sg-name { font-style: italic; font-weight: 800; font-size: 21px; margin-bottom: 2px; height: 28px; }
  .qpdf .sg.to .sg-name { visibility: hidden; }
  .qpdf .sg-line { border-bottom: 1.5px solid #111; }
  .qpdf .sg.from { text-align: center; }
  .qpdf .sg-sub { font-weight: 700; font-size: 12px; padding-top: 4px; height: 21px; }
  .qpdf .sg.to .sg-sub { visibility: hidden; }
  .qpdf .sg-stamp { position: absolute; right: 46px; bottom: 8px; width: 88px; }
  .qpdf .co { text-align: center; font-size: 13px; font-weight: 600; }
</style>
<div class="qpdf">
  <div class="hd">
    <img src="${logoUrl}" alt="AETS"/>
    <div class="hd-r">${esc(cfg.address)}<br/>TEL : ${esc(cfg.tel)}&nbsp;&nbsp;FAX : ${esc(cfg.fax)}</div>
  </div>
  <h1>FIRM QUOTATION</h1><div class="rule"></div>
  <div class="meta">
    ${kv('Quote No.', head.quoteNo)}${kv('Date', head.quoteDate)}
    ${kv('R(F)Q No.', head.rfqNo)}${kv('Valid Until', head.validity)}
  </div>
  <div class="cols">
    <div><h3>To</h3>
      ${kv('Company', head.company)}${kv('Attention', head.attention)}${kv('Tel', head.phone)}
    </div>
    <div><h3>From</h3>
      ${kv('Contact', head.aetsManager)}${kv('Tel', head.aetsPhone)}${kv('Biz. Reg. No.', head.businessNo)}
    </div>
  </div>
  <table>
    <thead><tr><th>No</th><th>Description</th><th>Part No.</th><th>Specification</th><th>Q'ty</th><th>Unit Price (₩)</th><th>Amount (₩)</th></tr></thead>
    <tbody>${itemHtml}</tbody>
    <tfoot><tr><td colspan="4" class="r"><b>Total</b></td><td class="r">${totalQty}</td><td></td><td class="r">₩ ${won(total)}</td></tr></tfoot>
  </table>
  <h4>Remarks</h4><div class="remarks">${esc(head.remarks)}</div>
  <div class="foot">
    <div class="sign">
      <div class="sg to">
        <div class="sg-t">Accepted by ;</div>
        <div class="sg-name">.</div>
        <div class="sg-line"></div>
        <div class="sg-sub">.</div>
      </div>
      <div class="sg from">
        <div class="sg-t">Very truly yours ;</div>
        <div class="sg-name">${esc(cfg.signer)}</div>
        <div class="sg-line"></div>
        <div class="sg-sub">Advanced Energy Technology Solution</div>
        <img class="sg-stamp" src="${stampUrl}" alt=""/>
      </div>
    </div>
    <div class="co">${esc(cfg.companyName)}</div>
  </div>
</div>`;
  }

  // PDF 내보내기 — 무조건 A4 한 장에 맞춰 저장 (내용이 길면 축소)
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
        const imgHmm = canvas.height / (canvas.width / pageW);
        const s = Math.min(1, pageH / imgHmm);
        pdf.addImage(canvas.toDataURL('image/jpeg', 0.95), 'JPEG', (pageW - pageW * s) / 2, 0, pageW * s, imgHmm * s);
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

  // 엑셀 내보내기 — PDF와 동일한 FIRM QUOTATION 양식(.xlsx)
  async function exportExcel() {
    if (exporting) return;
    setExporting(true);
    try {
      const ExcelJS = (await import('exceljs')).default;
      const wb = new ExcelJS.Workbook();
      const ws = wb.addWorksheet('견적서', {
        views: [{ showGridLines: false }],
        pageSetup: {
          paperSize: 9,               // A4
          orientation: 'portrait',
          fitToPage: true,            // 한 페이지에 맞춤
          fitToWidth: 1,
          fitToHeight: 1,
          horizontalCentered: true,
          margins: { left: 0.35, right: 0.35, top: 0.4, bottom: 0.4, header: 0.2, footer: 0.2 },
        },
      });
      ws.columns = [{ width: 7 }, { width: 24 }, { width: 18 }, { width: 20 }, { width: 8 }, { width: 14 }, { width: 16 }];
      const C = (r: number, c: number) => ws.getCell(r, c);
      const thin = { style: 'thin' as const };
      const medium = { style: 'medium' as const };
      const thick = { style: 'thick' as const };
      // 지정 열 범위에 아래 테두리
      const bottom = (row: number, c1: number, c2: number, edge: { style: 'thin' | 'medium' | 'thick' }, argb?: string) => {
        for (let c = c1; c <= c2; c++) {
          const cell = C(row, c);
          cell.border = { ...(cell.border || {}), bottom: argb ? { ...edge, color: { argb } } : edge };
        }
      };

      // 이미지 로드 (실패해도 진행)
      let logoId: number | null = null, stampId: number | null = null;
      try { logoId = wb.addImage({ buffer: await (await fetch(logoUrl)).arrayBuffer(), extension: 'png' }); } catch { /* noop */ }
      try { stampId = wb.addImage({ buffer: await (await fetch(stampUrl)).arrayBuffer(), extension: 'png' }); } catch { /* noop */ }

      // ── 1) 레터헤드 (로고 좌 / 주소·연락처 우 + 파란 밑줄) ──
      ws.getRow(1).height = 24; ws.getRow(2).height = 20; ws.getRow(3).height = 8;
      ws.mergeCells('D1:G1'); Object.assign(C(1, 4), { value: cfg.address, alignment: { horizontal: 'right', vertical: 'middle' }, font: { bold: true, size: 11, color: { argb: 'FF333333' } } });
      ws.mergeCells('D2:G2'); Object.assign(C(2, 4), { value: `TEL : ${cfg.tel}     FAX : ${cfg.fax}`, alignment: { horizontal: 'right', vertical: 'middle' }, font: { size: 10.5, color: { argb: 'FF333333' } } });
      if (logoId != null) ws.addImage(logoId, { tl: { col: 0.1, row: 0.1 }, ext: { width: 132, height: 92 } });
      bottom(3, 1, 7, medium, 'FF1C4390');

      // ── 2) 제목 ──
      ws.getRow(4).height = 8;
      ws.mergeCells('A5:G5'); Object.assign(C(5, 1), { value: 'FIRM QUOTATION', font: { bold: true, size: 18 }, alignment: { horizontal: 'center', vertical: 'middle' } });
      ws.getRow(5).height = 30;
      bottom(5, 1, 7, thick);

      // kv 행: 좌(A:B 라벨 / C:D 값) · 우(E:F 라벨 / G 값)
      const kvRow = (row: number, la: string, va: string, lb: string, vb: string) => {
        ws.mergeCells(row, 1, row, 2); Object.assign(C(row, 1), { value: la, font: { bold: true } });
        ws.mergeCells(row, 3, row, 4); C(row, 3).value = va;
        ws.mergeCells(row, 5, row, 6); Object.assign(C(row, 5), { value: lb, font: { bold: true } });
        C(row, 7).value = vb;
        bottom(row, 1, 7, thin, 'FFE5E5E5');
        ws.getRow(row).height = 20;
      };

      // ── 3) meta ──
      ws.getRow(6).height = 8;
      kvRow(7, 'Quote No.', head.quoteNo, 'Date', head.quoteDate);
      kvRow(8, 'R(F)Q No.', head.rfqNo, 'Valid Until', head.validity);

      // ── 4) To / From ──
      ws.getRow(9).height = 8;
      ws.mergeCells('A10:D10'); Object.assign(C(10, 1), { value: 'To', font: { bold: true, size: 12 } });
      ws.mergeCells('E10:G10'); Object.assign(C(10, 5), { value: 'From', font: { bold: true, size: 12 } });
      bottom(10, 1, 4, medium); bottom(10, 5, 7, medium);
      kvRow(11, 'Company', head.company, 'Contact', head.aetsManager);
      kvRow(12, 'Attention', head.attention, 'Tel', head.aetsPhone);
      kvRow(13, 'Tel', head.phone, 'Biz. Reg. No.', head.businessNo);

      // ── 5) 품목 표 ──
      ws.getRow(14).height = 8;
      let r = 15;
      const hd = ws.getRow(r);
      hd.values = ['No', 'Description', 'Part No.', 'Specification', "Q'ty", 'Unit Price (₩)', 'Amount (₩)'];
      hd.font = { bold: true };
      hd.eachCell(c => { c.alignment = { horizontal: 'center', vertical: 'middle' }; c.border = { top: medium, bottom: medium }; });
      hd.height = 22;
      r++;
      const items = rows.filter(x => x.description.trim() || x.partCode.trim());
      items.forEach((x, i) => {
        const row = ws.getRow(r);
        row.values = [i + 1, x.description, x.partCode, x.standardSpec, x.qty, x.listPrice, x.listPrice * x.qty];
        row.getCell(1).alignment = { horizontal: 'center' };
        row.getCell(2).alignment = { horizontal: 'left' };
        row.getCell(3).alignment = { horizontal: 'center' };
        row.getCell(4).alignment = { horizontal: 'center' };
        row.getCell(5).alignment = { horizontal: 'right' };
        row.getCell(6).alignment = { horizontal: 'right' }; row.getCell(6).numFmt = '#,##0';
        row.getCell(7).alignment = { horizontal: 'right' }; row.getCell(7).numFmt = '#,##0';
        bottom(r, 1, 7, thin, 'FFDDDDDD');
        r++;
      });
      // 합계
      const totRow = ws.getRow(r);
      ws.mergeCells(r, 1, r, 4);
      Object.assign(C(r, 1), { value: 'Total', font: { bold: true }, alignment: { horizontal: 'right' } });
      Object.assign(C(r, 5), { value: totalQty, font: { bold: true }, alignment: { horizontal: 'right' } });
      Object.assign(C(r, 7), { value: total, font: { bold: true }, alignment: { horizontal: 'right' } });
      C(r, 7).numFmt = '₩ #,##0';
      totRow.eachCell(c => { c.border = { top: medium, bottom: medium }; });
      r++;

      // ── 6) 비고 ──
      ws.getRow(r).height = 8; r++;
      ws.mergeCells(r, 1, r, 7); Object.assign(C(r, 1), { value: 'Remarks', font: { bold: true } });
      bottom(r, 1, 7, medium); r++;
      const rmRow = r;
      ws.mergeCells(rmRow, 1, rmRow, 7);
      Object.assign(C(rmRow, 1), { value: head.remarks, alignment: { wrapText: true, vertical: 'top' } });
      ws.getRow(rmRow).height = Math.max(48, (head.remarks.split('\n').length) * 16 + 8);
      r = rmRow + 1;

      // ── 7) 서명부 — PDF처럼 A4 세로 용지 최하단에 붙인다 ──
      // 지금까지 사용된 행 높이를 합산하고, 서명 블록 높이를 빼서 남는 공간만큼
      // 스페이서를 넣어 서명부를 페이지 하단으로 밀어낸다.
      let usedH = 0;
      for (let i = 1; i < r; i++) usedH += (ws.getRow(i).height || 15);
      const SIG_H = 15 + 15 + 26 + 15 + 15 + 15 + 15;  // 서명 블록 총 높이(pt)
      // A4 세로 인쇄영역(≈784pt)을 fit-to-width 축소 여유까지 고려한 콘텐츠 높이 기준.
      // 넉넉히 잡아 서명부가 항상 최하단에 닿도록 하고, fitToHeight=1 이 한 페이지로 축소.
      const PAGE_CONTENT_H = 868;
      const spacerH = Math.max(18, PAGE_CONTENT_H - usedH - SIG_H);
      ws.getRow(r).height = spacerH; r++;   // 하단 정렬용 스페이서
      const accRow = r;
      Object.assign(C(accRow, 1), { value: 'Accepted by ;', font: { italic: true, bold: true } });
      Object.assign(C(accRow, 5), { value: 'Very truly yours ;', font: { italic: true, bold: true }, alignment: { horizontal: 'center' } });
      ws.mergeCells(accRow, 5, accRow, 7);
      const nameRow = accRow + 2;   // 서명 여백 후
      ws.mergeCells(nameRow, 5, nameRow, 7);
      Object.assign(C(nameRow, 5), { value: cfg.signer, font: { italic: true, bold: true, size: 16 }, alignment: { horizontal: 'center', vertical: 'bottom' } });
      ws.getRow(nameRow).height = 26;
      const lineRow = nameRow + 1;
      bottom(lineRow, 1, 3, medium); bottom(lineRow, 5, 7, medium);
      const subRow = lineRow + 1;
      ws.mergeCells(subRow, 5, subRow, 7);
      Object.assign(C(subRow, 5), { value: 'Advanced Energy Technology Solution', font: { bold: true, size: 10.5 }, alignment: { horizontal: 'center' } });
      const coRow = subRow + 2;
      ws.mergeCells(coRow, 1, coRow, 7);
      Object.assign(C(coRow, 1), { value: cfg.companyName, font: { bold: true, size: 12 }, alignment: { horizontal: 'center' } });
      // 도장 — 서명(BH.PARK) 위에 겹치게
      if (stampId != null) ws.addImage(stampId, { tl: { col: 6.15, row: nameRow - 1.1 }, ext: { width: 72, height: 72 } });

      // 인쇄 영역을 실제 내용 끝까지로 고정 → A4 세로 한 장에 맞춰 출력
      ws.pageSetup.printArea = `A1:G${coRow}`;

      const buf = await wb.xlsx.writeBuffer();
      const a = document.createElement('a');
      a.href = URL.createObjectURL(new Blob([buf], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }));
      a.download = `${head.quoteNo || '견적서'}.xlsx`;
      a.click();
      URL.revokeObjectURL(a.href);
    } catch (err) {
      alert(err instanceof Error ? err.message : '엑셀 내보내기에 실패했습니다.');
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
          <button className="btn btn-ghost" onClick={exportExcel} disabled={exporting}>{exporting ? '생성 중...' : '엑셀 내보내기'}</button>
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
        {canEdit && <button className="btn btn-ghost" onClick={openCfg}>양식 기준정보</button>}
        <button className="btn btn-ghost" onClick={() => nav('/product-master')}>품목 단가표</button>
        {canEdit && <button className="btn btn-primary" onClick={startNew}>+ 견적 작성</button>}
      </header>
      {cfgOpen && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setCfgOpen(false); }}>
          <div className="modal-box qt-cfg">
            <h3>견적서 양식 기준정보</h3>
            <p className="qt-cfg-hint">PDF 상단 회사 정보와 하단 서명부에 사용됩니다. 엑셀 기준파일로 내려받아 수정 후 다시 불러올 수 있습니다.</p>
            <div className="qt-cfg-grid">
              {CFG_LABELS.map(([key, label]) => (
                <div className="qt-field" key={key}>
                  <label>{label}</label>
                  <input className="input" value={cfgDraft[key]} onChange={e => setCfgDraft({ ...cfgDraft, [key]: e.target.value })} />
                </div>
              ))}
            </div>
            <input ref={cfgFileRef} type="file" accept=".xlsx" style={{ display: 'none' }}
              onChange={e => { const f = e.target.files?.[0]; if (f) uploadCfgExcel(f); e.target.value = ''; }} />
            <div className="modal-actions qt-cfg-actions">
              <button className="btn btn-ghost" onClick={downloadCfgExcel}>엑셀 기준파일 받기</button>
              <button className="btn btn-ghost" onClick={() => cfgFileRef.current?.click()}>엑셀 불러오기</button>
              <span className="qt-cfg-spacer" />
              <button className="btn btn-ghost" onClick={() => setCfgOpen(false)}>취소</button>
              <button className="btn btn-primary" onClick={saveCfg} disabled={cfgSaving}>{cfgSaving ? '저장 중...' : '저장'}</button>
            </div>
          </div>
        </div>
      )}
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
