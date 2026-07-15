import { useEffect, useState, useCallback } from 'react';
import { api } from '../api/client';
import type { ProductMaster as PM, GlobalTemplate as GT, QuotationConfig } from '../api/types';
import './ProductMaster.css';

type Tab = 'products' | 'templates' | 'config';
const won = (n: number) => n.toLocaleString('ko-KR');

const blankPM = (): Omit<PM, 'id' | 'updatedBy' | 'updatedAt'> =>
  ({ productName: '', partCode: '', spec: '', unitPrice: 0, vendorName: '', unit: '' });
const blankGT = (): Omit<GT, 'id'> => ({ productCode: '', productName: '', templatePath: '' });

export default function ProductMaster() {
  const [tab, setTab] = useState<Tab>('products');
  return (
    <div>
      <header className="pg-header">
        <div><h2>품목 단가표</h2></div>
      </header>
      <div className="pg-body">
        <div className="pm-tabs">
          <button className={tab === 'products' ? 'active' : ''} onClick={() => setTab('products')}>품목 단가표</button>
          <button className={tab === 'templates' ? 'active' : ''} onClick={() => setTab('templates')}>전역 템플릿</button>
          <button className={tab === 'config' ? 'active' : ''} onClick={() => setTab('config')}>견적 설정</button>
        </div>
        {tab === 'products' && <Products />}
        {tab === 'templates' && <Templates />}
        {tab === 'config' && <Config />}
      </div>
    </div>
  );
}

// ── 품목 단가표 ──
function Products() {
  const [list, setList] = useState<PM[]>([]);
  const [search, setSearch] = useState('');
  const [edit, setEdit] = useState<PM | 'new' | null>(null);
  const [form, setForm] = useState(blankPM());

  const load = useCallback(async () => {
    setList(await api.get<PM[]>(`/api/quotationmaster/products?search=${encodeURIComponent(search)}`));
  }, [search]);
  useEffect(() => { load(); }, [load]);

  function openNew() { setEdit('new'); setForm(blankPM()); }
  function openEdit(p: PM) { setEdit(p); setForm({ ...p }); }
  async function save() {
    if (edit === 'new') await api.post('/api/quotationmaster/products', form);
    else if (edit) await api.put(`/api/quotationmaster/products/${edit.id}`, form);
    setEdit(null); load();
  }
  async function del(id: number) {
    if (!confirm('이 품목을 삭제할까요?')) return;
    await api.del(`/api/quotationmaster/products/${id}`); setEdit(null); load();
  }

  return (
    <>
      <div className="pm-toolbar">
        <input className="input pm-search" placeholder="품명/품번/업체 검색" value={search} onChange={e => setSearch(e.target.value)} />
        <span className="pm-count">{list.length}건</span>
        <button className="btn btn-primary" onClick={openNew}>+ 품목 추가</button>
      </div>
      <div className="pm-tablewrap">
        <table className="pm-table">
          <thead><tr><th>품명</th><th>품번</th><th>규격</th><th className="r">단가</th><th>업체</th><th>단위</th><th>수정</th></tr></thead>
          <tbody>
            {list.length === 0 && <tr><td colSpan={7} className="pm-empty">품목이 없습니다</td></tr>}
            {list.map(p => (
              <tr key={p.id} className="pm-row" onClick={() => openEdit(p)}>
                <td>{p.productName}</td>
                <td>{p.partCode}</td>
                <td>{p.spec}</td>
                <td className="r">{won(p.unitPrice)}</td>
                <td>{p.vendorName}</td>
                <td>{p.unit}</td>
                <td className="pm-upd">{p.updatedBy}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {edit && (
        <Modal title={edit === 'new' ? '품목 추가' : '품목 수정'} onClose={() => setEdit(null)}
          onSave={save} onDelete={edit !== 'new' ? () => del(edit.id) : undefined}>
          <FF l="품명"><input className="input" value={form.productName} onChange={e => setForm({ ...form, productName: e.target.value })} /></FF>
          <FF l="품번"><input className="input" value={form.partCode} onChange={e => setForm({ ...form, partCode: e.target.value })} /></FF>
          <FF l="규격"><input className="input" value={form.spec} onChange={e => setForm({ ...form, spec: e.target.value })} /></FF>
          <FF l="단가"><input className="input" type="number" value={form.unitPrice} onChange={e => setForm({ ...form, unitPrice: Number(e.target.value) })} /></FF>
          <FF l="업체"><input className="input" value={form.vendorName} onChange={e => setForm({ ...form, vendorName: e.target.value })} /></FF>
          <FF l="단위"><input className="input" value={form.unit} onChange={e => setForm({ ...form, unit: e.target.value })} /></FF>
        </Modal>
      )}
    </>
  );
}

// ── 전역 품목 템플릿 ──
function Templates() {
  const [list, setList] = useState<GT[]>([]);
  const [edit, setEdit] = useState<GT | 'new' | null>(null);
  const [form, setForm] = useState(blankGT());

  const load = useCallback(async () => { setList(await api.get<GT[]>('/api/quotationmaster/templates')); }, []);
  useEffect(() => { load(); }, [load]);

  function openNew() { setEdit('new'); setForm(blankGT()); }
  function openEdit(t: GT) { setEdit(t); setForm({ ...t }); }
  async function save() {
    if (edit === 'new') await api.post('/api/quotationmaster/templates', form);
    else if (edit) await api.put(`/api/quotationmaster/templates/${edit.id}`, form);
    setEdit(null); load();
  }
  async function del(id: number) {
    if (!confirm('이 템플릿을 삭제할까요?')) return;
    await api.del(`/api/quotationmaster/templates/${id}`); setEdit(null); load();
  }

  return (
    <>
      <div className="pm-toolbar">
        <span className="pm-count">{list.length}건</span>
        <button className="btn btn-primary" onClick={openNew}>+ 템플릿 추가</button>
      </div>
      <div className="pm-tablewrap">
        <table className="pm-table">
          <thead><tr><th>품목 코드</th><th>품명</th><th>템플릿 경로</th></tr></thead>
          <tbody>
            {list.length === 0 && <tr><td colSpan={3} className="pm-empty">템플릿이 없습니다</td></tr>}
            {list.map(t => (
              <tr key={t.id} className="pm-row" onClick={() => openEdit(t)}>
                <td>{t.productCode}</td>
                <td>{t.productName}</td>
                <td className="pm-path">{t.templatePath}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {edit && (
        <Modal title={edit === 'new' ? '템플릿 추가' : '템플릿 수정'} onClose={() => setEdit(null)}
          onSave={save} onDelete={edit !== 'new' ? () => del(edit.id) : undefined}>
          <FF l="품목 코드"><input className="input" value={form.productCode} onChange={e => setForm({ ...form, productCode: e.target.value })} /></FF>
          <FF l="품명"><input className="input" value={form.productName} onChange={e => setForm({ ...form, productName: e.target.value })} /></FF>
          <FF l="템플릿 경로"><input className="input" value={form.templatePath} onChange={e => setForm({ ...form, templatePath: e.target.value })} /></FF>
        </Modal>
      )}
    </>
  );
}

// ── 견적 설정 ──
function Config() {
  const [businessNo, setBusinessNo] = useState('');
  const [saved, setSaved] = useState(false);

  useEffect(() => { api.get<QuotationConfig>('/api/quotationmaster/config').then(c => setBusinessNo(c.businessNo)); }, []);
  async function save() {
    await api.put('/api/quotationmaster/config', { businessNo });
    setSaved(true); setTimeout(() => setSaved(false), 1500);
  }
  return (
    <div className="pm-config">
      <FF l="사업자번호"><input className="input" value={businessNo} onChange={e => setBusinessNo(e.target.value)} placeholder="000-00-00000" /></FF>
      <button className="btn btn-primary" onClick={save}>{saved ? '저장됨 ✓' : '저장'}</button>
    </div>
  );
}

function Modal({ title, children, onClose, onSave, onDelete }: {
  title: string; children: React.ReactNode; onClose: () => void; onSave: () => void; onDelete?: () => void;
}) {
  return (
    <div className="pm-modal-bg" onClick={onClose}>
      <div className="pm-modal" onClick={e => e.stopPropagation()}>
        <div className="pm-modal-head"><h3>{title}</h3><button className="pm-x" onClick={onClose}>✕</button></div>
        <div className="pm-modal-body">{children}</div>
        <div className="pm-modal-foot">
          {onDelete && <button className="btn pm-del" onClick={onDelete}>삭제</button>}
          <div style={{ flex: 1 }} />
          <button className="btn btn-ghost" onClick={onClose}>취소</button>
          <button className="btn btn-primary" onClick={onSave}>저장</button>
        </div>
      </div>
    </div>
  );
}

function FF({ l, children }: { l: string; children: React.ReactNode }) {
  return <div className="pm-field"><label>{l}</label>{children}</div>;
}
