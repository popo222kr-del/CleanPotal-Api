import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { api, getToken } from '../../../api/client';
import { useAccess } from '../../../auth/useAccess';
import { dateOnly } from '../lot';
import '../Mes.css';

// 셋업 > 단가/이미지.
// 이미지는 [불러오기] 로 화면에 먼저 올려 보고 [저장] 을 눌러야 실제로 저장된다(MES 와 같다) —
// 고르는 순간 저장되면 잘못 고른 것을 되돌릴 수 없다.

type Product = { productId: number; cleaningCode: string; productName: string; itemCode: string; isActive: boolean };
type Price = { id: number; effectiveDate: string; unitPrice: number };
type PriceImage = { prices: Price[]; imageBase64: string | null; imageMimeType: string | null };
type Result = { success: boolean; message: string };

const MAX_IMAGE_BYTES = 10 * 1024 * 1024;
const isoToday = () => {
  const d = new Date(); const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
};

export default function PriceImageTab() {
  const canEdit = useAccess().canEditMes;
  const fileRef = useRef<HTMLInputElement>(null);

  const [products, setProducts] = useState<Product[]>([]);
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState<Product | null>(null);

  const [prices, setPrices] = useState<Price[]>([]);
  const [selPrice, setSelPrice] = useState<Price | null>(null);
  const [effectiveDate, setEffectiveDate] = useState(isoToday());
  const [unitPrice, setUnitPrice] = useState('');

  // 화면에 올려 둔 이미지. pending 이 true 면 아직 저장 전이다.
  const [imageUrl, setImageUrl] = useState<string | null>(null);
  const [pendingFile, setPendingFile] = useState<File | null>(null);
  const [pendingClear, setPendingClear] = useState(false);

  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  const [isError, setIsError] = useState(false);

  useEffect(() => {
    void (async () => {
      try { setProducts(await api.get<Product[]>('/api/mes/setup/products')); }
      catch { setIsError(true); setMsg('제품 목록을 불러오는 중 문제가 발생했습니다.'); }
    })();
  }, []);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return products;
    return products.filter(p =>
      p.cleaningCode.toLowerCase().includes(q) ||
      p.productName.toLowerCase().includes(q) ||
      p.itemCode.toLowerCase().includes(q));
  }, [products, search]);

  const loadDetail = useCallback(async (productId: number) => {
    const d = await api.get<PriceImage>(`/api/mes/setup/products/${productId}/price-image`);
    setPrices(d.prices);
    setImageUrl(d.imageBase64 ? `data:${d.imageMimeType ?? 'image/png'};base64,${d.imageBase64}` : null);
    setPendingFile(null); setPendingClear(false);
  }, []);

  async function pick(p: Product) {
    setSelected(p); setSelPrice(null); setUnitPrice(''); setMsg(null);
    setBusy(true);
    try { await loadDetail(p.productId); }
    catch { setIsError(true); setMsg('단가 · 이미지를 불러오는 중 문제가 발생했습니다.'); }
    finally { setBusy(false); }
  }

  async function run(work: () => Promise<Result>) {
    if (busy || !selected) return;
    setBusy(true);
    try {
      const r = await work();
      setIsError(!r.success); setMsg(r.message);
      if (r.success) await loadDetail(selected.productId);
    } catch {
      setIsError(true); setMsg('처리 중 문제가 발생했습니다.');
    } finally { setBusy(false); }
  }

  const addPrice = () => {
    const value = Number(unitPrice.replace(/,/g, ''));
    if (!Number.isFinite(value)) { setIsError(true); setMsg('단가를 숫자로 입력하세요.'); return; }
    return run(async () => {
      const r = await api.post<Result>(`/api/mes/setup/products/${selected!.productId}/prices`,
        { effectiveDate, unitPrice: value });
      if (r.success) { setUnitPrice(''); setSelPrice(null); }
      return r;
    });
  };

  const deletePrice = () => {
    if (!selPrice) { setIsError(true); setMsg('삭제할 단가를 목록에서 고르세요.'); return; }
    return run(async () => {
      const r = await api.del<Result>(`/api/mes/setup/prices/${selPrice.id}`);
      if (r.success) setSelPrice(null);
      return r;
    });
  };

  function chooseImage(file: File | undefined) {
    if (!file) return;
    if (file.size > MAX_IMAGE_BYTES) { setIsError(true); setMsg('이미지가 너무 큽니다(최대 10MB).'); return; }
    setPendingFile(file); setPendingClear(false);
    setImageUrl(URL.createObjectURL(file));
    setIsError(false); setMsg('이미지를 불러왔습니다. 저장하려면 [저장] 을 누르세요.');
  }

  function clearImage() {
    setPendingFile(null); setPendingClear(true); setImageUrl(null);
    setIsError(false); setMsg('이미지를 비웠습니다. 저장하려면 [저장] 을 누르세요.');
  }

  const saveImage = () => run(async () => {
    const body = new FormData();
    if (pendingFile) body.append('file', pendingFile);   // 파일이 없으면 서버가 "비우기" 로 본다
    const res = await fetch(`/api/mes/setup/products/${selected!.productId}/image`, {
      method: 'POST', headers: { Authorization: `Bearer ${getToken() ?? ''}` }, body,
    });
    if (!res.ok) throw new Error();
    const payload = await res.json();
    return (payload?.data ?? payload) as Result;
  });

  const imageDirty = pendingFile !== null || pendingClear;

  return (
    <div className="mes-setup">
      <section>
        <div className="mes-title">제품 목록<span className="mes-count">{filtered.length}건</span></div>
        <input className="input mes-search" placeholder="세정코드 / 제품명 / 품목코드로 검색"
               value={search} onChange={e => setSearch(e.target.value)} />
        <div className="mes-scroll tall" style={{ marginTop: 8 }}>
          <table className="mes-table">
            <thead><tr><th>세정코드</th><th>제품명</th><th>품목코드</th></tr></thead>
            <tbody>
              {filtered.map(p => (
                <tr key={p.productId} className={p.productId === selected?.productId ? 'picked' : ''}
                    onClick={() => void pick(p)} style={{ cursor: 'pointer' }}>
                  <td>{p.cleaningCode}</td>
                  <td>{p.productName}</td>
                  <td>{p.itemCode}</td>
                </tr>
              ))}
              {filtered.length === 0 && (
                <tr><td colSpan={3} className="mes-empty">등록된 제품이 없습니다.</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </section>

      <section>
        <div className="mes-title">
          제품 단가 및 이미지
          {selected && <span className="mes-title-sub"> — {selected.cleaningCode} · {selected.productName}</span>}
        </div>

        {!selected ? <p className="mes-dim">왼쪽에서 제품을 고르세요.</p> : (
          <>
            <div className="mes-info">
              <div className="mes-scroll" style={{ maxHeight: 200 }}>
                <table className="mes-table">
                  <thead><tr><th>적용일자</th><th className="num">단가</th></tr></thead>
                  <tbody>
                    {prices.map(p => (
                      <tr key={p.id} className={p.id === selPrice?.id ? 'picked' : ''}
                          onClick={() => { setSelPrice(p); setEffectiveDate(dateOnly(p.effectiveDate)); setUnitPrice(String(p.unitPrice)); }}
                          style={{ cursor: 'pointer' }}>
                        <td>{dateOnly(p.effectiveDate)}</td>
                        <td className="num">{p.unitPrice.toLocaleString('ko-KR')}</td>
                      </tr>
                    ))}
                    {prices.length === 0 && (
                      <tr><td colSpan={2} className="mes-empty">등록된 단가가 없습니다.</td></tr>
                    )}
                  </tbody>
                </table>
              </div>

              <div className="mes-price-add">
                <label><span>적용일자</span>
                  <input type="date" className="input" value={effectiveDate} disabled={!canEdit}
                         onChange={e => setEffectiveDate(e.target.value)} /></label>
                <label><span>단가</span>
                  <input className="input" value={unitPrice} disabled={!canEdit}
                         onChange={e => setUnitPrice(e.target.value)} /></label>
                <button className="btn btn-primary" onClick={() => void addPrice()} disabled={busy || !canEdit}>추가</button>
                <button className="btn btn-ghost" onClick={() => void deletePrice()} disabled={busy || !canEdit || !selPrice}>삭제</button>
              </div>
            </div>

            <div className="mes-info" style={{ marginTop: 12 }}>
              <div className="mes-dim">제품 이미지</div>
              <div className="mes-image-box">
                {imageUrl ? <img src={imageUrl} alt="제품 이미지" /> : <span className="mes-dim">이미지 없음</span>}
              </div>
              <div className="mes-form-actions">
                <button className="btn btn-ghost" onClick={clearImage} disabled={!canEdit}>비우기</button>
                <button className="btn btn-ghost" onClick={() => fileRef.current?.click()} disabled={!canEdit}>불러오기</button>
                <input ref={fileRef} type="file" accept="image/*" style={{ display: 'none' }}
                       onChange={e => { const f = e.target.files?.[0]; e.target.value = ''; chooseImage(f); }} />
                <button className="btn btn-primary" onClick={() => void saveImage()}
                        disabled={busy || !canEdit || !imageDirty}>저장</button>
              </div>
            </div>

            {msg && <p className={`mes-alert ${isError ? 'error' : 'ok'}`}>{msg}</p>}
            {!canEdit && <p className="mes-dim">단가 · 이미지 수정은 MES 편집 권한이 필요합니다.</p>}
          </>
        )}
      </section>
    </div>
  );
}
