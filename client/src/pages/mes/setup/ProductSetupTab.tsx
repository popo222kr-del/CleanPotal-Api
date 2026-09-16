import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { api, getToken } from '../../../api/client';
import { useAccess } from '../../../auth/useAccess';
import '../Mes.css';

// 셋업 > 제품 셋업.
// 1열 제품 목록 / 2열 제품 등록·수정(성적서 양식 · 공정 플로우) / 3열 공정별 레시피 · 검사 파라미터.
//
// 제품에 딸린 설정이 다섯 갈래(플로우 · 레시피 · 파라미터 · 양식 · 기본 LINE)다. 조회는 한 번에 받고
// 바꾸는 것은 갈래별로 나눠 보낸다 — 한 덩어리로 저장하면 레시피 하나 고치는 데 파라미터까지 덮어쓴다.
//
// LINE · 양식 · 플로우 · 레시피 · 파라미터는 저장된 제품을 골랐을 때만 다룰 수 있다.
// 아직 만들지 않은 제품에는 붙일 곳이 없다.

type Product = {
  productId: number; cleaningCode: string; productCode: string; itemCode: string; productName: string;
  serialNumber: string | null; customerId: number; customerName: string; isActive: boolean; itemCategory: string | null;
};
type Customer = { customerId: number; customerCode: string; customerName: string };
type Line = { lineId: number; code: string; description: string };
type RecipeOption = { recipeId: number; code: string; description: string; operCode: number; readTimeMinutes: number | null };
type ParameterCatalog = { code: string; description: string };
type OperOption = { operCode: number; processName: string };
type Reference = {
  customers: Customer[]; lines: Line[]; recipes: RecipeOption[];
  parameterCatalog: ParameterCatalog[]; opers: OperOption[];
};
type Flow = { processRouteId: number; routeCode: string; routeName: string };
type ProductRecipe = {
  assignmentId: number; recipeDefinitionId: number; recipeCode: string; recipeDescription: string;
  operCode: number; minValue: number | null; maxValue: number | null;
  isMain: boolean; isActive: boolean; readTimeMinutes: number | null;
};
type ParameterRow = {
  parameterId: number; code: string; description: string; parameterType: number;
  oper: string | null; valueCount: number; minValue: number | null; maxValue: number | null;
  unit: string | null; isActive: boolean; certificateLabel: string | null;
};
type Detail = {
  assignedFlows: Flow[]; availableFlows: Flow[]; defaultLineId: number | null;
  recipes: ProductRecipe[]; parameters: ParameterRow[]; templateFileName: string | null;
};
type Result = { success: boolean; message: string };

const PARAM_TYPES = [
  { value: 0, label: '계측(Numeric)' },
  { value: 1, label: '외관 Y/N(Boolean)' },
  { value: 2, label: '판정 OK/NG/CC(Choice)' },
];

const emptyProduct = {
  cleaningCode: '', itemCode: '', productName: '', itemCategory: '', productCode: '', customerId: '',
};
const emptyRecipe = { recipeDefinitionId: '', minValue: '', maxValue: '', isMain: false, isActive: true };
const emptyParam = {
  code: '', description: '', parameterType: 0, oper: '', valueCount: 1,
  minValue: '', maxValue: '', unit: '', isActive: true, certificateLabel: '',
};

const num = (v: string) => (v.trim() === '' ? null : Number(v));
const show = (v: number | null) => (v === null || v === undefined ? '-' : String(v));

export default function ProductSetupTab() {
  const canEdit = useAccess().canEditMes;
  const templateRef = useRef<HTMLInputElement>(null);

  const [ref, setRef] = useState<Reference | null>(null);
  const [products, setProducts] = useState<Product[]>([]);
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState<Product | null>(null);
  const [detail, setDetail] = useState<Detail | null>(null);

  const [form, setForm] = useState(emptyProduct);
  const [defaultLineId, setDefaultLineId] = useState('');
  const [templateName, setTemplateName] = useState('');
  const [selAssigned, setSelAssigned] = useState<number | null>(null);
  const [selAvailable, setSelAvailable] = useState<number | null>(null);

  const [rightTab, setRightTab] = useState<'recipe' | 'param'>('recipe');
  const [selRecipe, setSelRecipe] = useState<ProductRecipe | null>(null);
  const [recipeForm, setRecipeForm] = useState(emptyRecipe);
  const [recipeCopy, setRecipeCopy] = useState('');
  const [selParam, setSelParam] = useState<ParameterRow | null>(null);
  const [paramForm, setParamForm] = useState(emptyParam);
  const [paramCopy, setParamCopy] = useState('');

  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  const [isError, setIsError] = useState(false);

  const loadProducts = useCallback(async () => {
    setProducts(await api.get<Product[]>('/api/mes/setup/product'));
  }, []);

  const loadDetail = useCallback(async (productId: number) => {
    const d = await api.get<Detail>(`/api/mes/setup/product/${productId}`);
    setDetail(d);
    setDefaultLineId(d.defaultLineId === null ? '' : String(d.defaultLineId));
    setTemplateName(d.templateFileName ?? '');
    setSelAssigned(null); setSelAvailable(null);
    setSelRecipe(null); setRecipeForm(emptyRecipe);
    setSelParam(null); setParamForm(emptyParam);
  }, []);

  useEffect(() => {
    void (async () => {
      try {
        setRef(await api.get<Reference>('/api/mes/setup/product/reference'));
        await loadProducts();
      } catch { setIsError(true); setMsg('제품 셋업을 불러오는 중 문제가 발생했습니다.'); }
    })();
  }, [loadProducts]);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return products;
    return products.filter(p =>
      p.cleaningCode.toLowerCase().includes(q) ||
      p.productName.toLowerCase().includes(q) ||
      p.itemCode.toLowerCase().includes(q));
  }, [products, search]);

  async function pick(p: Product) {
    setSelected(p);
    setForm({
      cleaningCode: p.cleaningCode, itemCode: p.itemCode, productName: p.productName,
      itemCategory: p.itemCategory ?? '', productCode: p.productCode, customerId: String(p.customerId),
    });
    setMsg(null); setBusy(true);
    try { await loadDetail(p.productId); }
    catch { setIsError(true); setMsg('제품 설정을 불러오는 중 문제가 발생했습니다.'); }
    finally { setBusy(false); }
  }

  function newForm() {
    setSelected(null); setDetail(null); setForm(emptyProduct);
    setDefaultLineId(''); setTemplateName(''); setMsg(null);
  }

  /** 마스터를 바꾸고 목록·상세를 다시 읽는다. 무엇이 바뀌었는지는 서버가 준 문구를 그대로 보여 준다. */
  async function run(work: () => Promise<Result>, reloadList = false) {
    if (busy) return;
    setBusy(true);
    try {
      const r = await work();
      setIsError(!r.success); setMsg(r.message);
      if (r.success) {
        if (reloadList) await loadProducts();
        if (selected) await loadDetail(selected.productId);
      }
    } catch {
      setIsError(true); setMsg('처리 중 문제가 발생했습니다.');
    } finally { setBusy(false); }
  }

  const productBody = () => ({
    cleaningCode: form.cleaningCode.trim(),
    productCode: form.productCode.trim(),
    itemCode: form.itemCode.trim(),
    productName: form.productName.trim(),
    serialNumber: null,
    customerId: Number(form.customerId),
    itemCategory: form.itemCategory.trim() || null,
  });

  const saveProduct = () => run(async () => {
    const r = selected
      ? await api.put<Result>(`/api/mes/setup/product/${selected.productId}`, productBody())
      : await api.post<Result>('/api/mes/setup/product', productBody());
    return r;
  }, true);

  /**
   * 고른 제품을 본떠 새 세정코드를 만든다 — 레시피 · 검사 항목 · 기본 LINE 까지 따라온다.
   * 비슷한 제품이 계속 들어오는 일이라, 매번 손으로 다시 넣으면 빠뜨린다.
   */
  const createFrom = () => run(async () => {
    const r = await api.post<Result>('/api/mes/setup/product/create-from', {
      product: productBody(),
      sourceCleaningCode: selected?.cleaningCode ?? null,
      sourceProductId: selected?.productId ?? null,
    });
    if (r.success) { setSelected(null); setDetail(null); }   // 새로 만든 것을 목록에서 고르게 둔다
    return r;
  }, true);

  const toggleActive = () => {
    if (!selected) return;
    return run(() => api.post<Result>(`/api/mes/setup/product/${selected.productId}/active`,
      { isActive: !selected.isActive }), true);
  };

  const saveDefaultLine = (value: string) => {
    setDefaultLineId(value);
    if (!selected) return;
    return run(() => api.post<Result>(`/api/mes/setup/product/${selected.productId}/default-line`,
      { lineId: value ? Number(value) : null }));
  };

  const assignFlow = (routeId: number | null) => {
    const id = routeId ?? selAvailable;
    if (!selected || id === null) return;
    return run(() => api.post<Result>(`/api/mes/setup/product/${selected.productId}/flows/${id}`, {}));
  };
  const unassignFlow = (routeId: number | null) => {
    const id = routeId ?? selAssigned;
    if (!selected || id === null) return;
    return run(() => api.del<Result>(`/api/mes/setup/product/${selected.productId}/flows/${id}`));
  };

  const saveTemplate = (file?: File) => {
    if (!selected) return;
    return run(async () => {
      const body = new FormData();
      if (file) body.append('file', file);
      body.append('fileName', templateName.trim());
      const res = await fetch(`/api/mes/setup/product/${selected.productId}/template`, {
        method: 'POST', headers: { Authorization: `Bearer ${getToken() ?? ''}` }, body,
      });
      if (!res.ok) throw new Error();
      const payload = await res.json();
      return (payload?.data ?? payload) as Result;
    });
  };

  // ── 레시피 ──
  function pickRecipe(r: ProductRecipe) {
    setSelRecipe(r);
    setRecipeForm({
      recipeDefinitionId: String(r.recipeDefinitionId),
      minValue: r.minValue === null ? '' : String(r.minValue),
      maxValue: r.maxValue === null ? '' : String(r.maxValue),
      isMain: r.isMain, isActive: r.isActive,
    });
  }
  const recipeBody = () => ({
    recipeDefinitionId: Number(recipeForm.recipeDefinitionId),
    minValue: num(recipeForm.minValue), maxValue: num(recipeForm.maxValue),
    isMain: recipeForm.isMain, isActive: recipeForm.isActive,
  });
  const saveRecipe = (update: boolean) => {
    if (!selected || !recipeForm.recipeDefinitionId) return;
    if (update && !selRecipe) return;
    return run(() => update
      ? api.put<Result>(`/api/mes/setup/product/${selected.productId}/recipes/${selRecipe!.assignmentId}`, recipeBody())
      : api.post<Result>(`/api/mes/setup/product/${selected.productId}/recipes`, recipeBody()));
  };
  const deleteRecipe = () => selRecipe && run(() =>
    api.del<Result>(`/api/mes/setup/product/recipes/${selRecipe.assignmentId}`));
  const copyRecipes = () => selected && run(() =>
    api.post<Result>(`/api/mes/setup/product/${selected.productId}/recipes/copy`, { sourceCleaningCode: recipeCopy }));

  // ── 파라미터 ──
  function pickParam(p: ParameterRow) {
    setSelParam(p);
    setParamForm({
      code: p.code, description: p.description, parameterType: p.parameterType,
      oper: p.oper ?? '', valueCount: p.valueCount,
      minValue: p.minValue === null ? '' : String(p.minValue),
      maxValue: p.maxValue === null ? '' : String(p.maxValue),
      unit: p.unit ?? '', isActive: p.isActive, certificateLabel: p.certificateLabel ?? '',
    });
  }
  const paramBody = () => ({
    code: paramForm.code.trim(), description: paramForm.description.trim(),
    parameterType: paramForm.parameterType, oper: paramForm.oper || null,
    valueCount: paramForm.valueCount, minValue: num(paramForm.minValue), maxValue: num(paramForm.maxValue),
    unit: paramForm.unit.trim() || null, isActive: paramForm.isActive,
    certificateLabel: paramForm.certificateLabel.trim() || null,
  });
  const saveParam = (update: boolean) => {
    if (!selected || !paramForm.code.trim()) return;
    if (update && !selParam) return;
    return run(() => update
      ? api.put<Result>(`/api/mes/setup/product/${selected.productId}/parameters/${selParam!.parameterId}`, paramBody())
      : api.post<Result>(`/api/mes/setup/product/${selected.productId}/parameters`, paramBody()));
  };
  const deleteParam = () => selParam && run(() =>
    api.del<Result>(`/api/mes/setup/product/parameters/${selParam.parameterId}`));
  const copyParams = () => selected && run(() =>
    api.post<Result>(`/api/mes/setup/product/${selected.productId}/parameters/copy`, { sourceCleaningCode: paramCopy }));

  const locked = !selected || !canEdit;   // 저장된 제품을 골랐을 때만 딸린 설정을 다룰 수 있다
  const recipesByOper = useMemo(() => {
    const groups = new Map<number, RecipeOption[]>();
    for (const r of ref?.recipes ?? []) {
      const list = groups.get(r.operCode) ?? [];
      list.push(r);
      groups.set(r.operCode, list);
    }
    return [...groups.entries()].sort((a, b) => a[0] - b[0]);
  }, [ref]);

  return (
    <div className="mes-product-setup">
      {/* 1열 — 제품 목록 */}
      <section>
        <div className="mes-title">제품 목록<span className="mes-count">{filtered.length}건</span></div>
        <input className="input mes-search" placeholder="세정코드 / 제품명 / 품목코드로 검색"
               value={search} onChange={e => setSearch(e.target.value)} />
        <div className="mes-scroll tall" style={{ marginTop: 8 }}>
          <table className="mes-table">
            <thead><tr><th>세정코드</th><th>제품명</th></tr></thead>
            <tbody>
              {filtered.map(p => (
                <tr key={p.productId} className={p.productId === selected?.productId ? 'picked' : ''}
                    onClick={() => void pick(p)} style={{ cursor: 'pointer' }}>
                  <td>{p.cleaningCode}</td>
                  <td>
                    {p.productName}
                    {!p.isActive && <span className="mes-badge off" style={{ marginLeft: 4 }}>중지</span>}
                  </td>
                </tr>
              ))}
              {filtered.length === 0 && (
                <tr><td colSpan={2} className="mes-empty">등록된 제품이 없습니다.</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </section>

      {/* 2열 — 제품 등록/수정 */}
      <section>
        <div className="mes-title">{selected ? '제품 수정' : '제품 등록'}</div>
        <div className="mes-info">
          <div className="mes-form">
            <label><span>세정코드 (고유값)</span>
              <input className="input" value={form.cleaningCode} disabled={!canEdit}
                     onChange={e => setForm(f => ({ ...f, cleaningCode: e.target.value }))} /></label>
            <label><span>품목코드</span>
              <input className="input" value={form.itemCode} disabled={!canEdit}
                     onChange={e => setForm(f => ({ ...f, itemCode: e.target.value }))} /></label>
            <label><span>제품명</span>
              <input className="input" value={form.productName} disabled={!canEdit}
                     onChange={e => setForm(f => ({ ...f, productName: e.target.value }))} /></label>
            <label><span>품목 구분</span>
              <input className="input" placeholder="예: Accessory / QTZ Boat" value={form.itemCategory} disabled={!canEdit}
                     onChange={e => setForm(f => ({ ...f, itemCategory: e.target.value }))} /></label>
            <label><span>제품 규격</span>
              <input className="input" value={form.productCode} disabled={!canEdit}
                     onChange={e => setForm(f => ({ ...f, productCode: e.target.value }))} /></label>
            <label><span>업체명</span>
              <select value={form.customerId} disabled={!canEdit}
                      onChange={e => setForm(f => ({ ...f, customerId: e.target.value }))}>
                <option value="">-- 선택 --</option>
                {ref?.customers.map(c => (
                  <option key={c.customerId} value={c.customerId}>{c.customerCode} {c.customerName}</option>
                ))}
              </select></label>
            <label><span>LINE (고르면 바로 저장됩니다)</span>
              <select value={defaultLineId} disabled={locked}
                      onChange={e => void saveDefaultLine(e.target.value)}>
                <option value="">-</option>
                {ref?.lines.map(l => <option key={l.lineId} value={l.lineId}>{l.description}</option>)}
              </select></label>
          </div>

          <div className="mes-form-actions">
            <button className="btn btn-ghost" onClick={newForm} disabled={busy}>새로 작성</button>
            <button className="btn btn-ghost" onClick={() => void createFrom()}
                    disabled={busy || !canEdit || !form.cleaningCode.trim() || !form.customerId}
                    title="지금 폼 값으로 새 세정코드를 만듭니다. 제품을 고른 상태면 그 제품의 레시피 · 검사 항목 · LINE 이 따라옵니다.">
              생성
            </button>
            <button className="btn btn-ghost" onClick={() => void toggleActive()} disabled={busy || locked}>
              {selected?.isActive ? '중지하기' : '사용하기'}
            </button>
            <button className="btn btn-primary" onClick={() => void saveProduct()}
                    disabled={busy || !canEdit || !form.cleaningCode.trim() || !form.customerId}>저장</button>
          </div>
        </div>

        <div className="mes-info" style={{ marginTop: 12 }}>
          <div className="mes-dim">성적서 기본 양식 (Excel)</div>
          <div className="mes-template-row">
            <input className="input" value={templateName} disabled={locked}
                   title="INSPECTION 폴더에서 찾을 성적서 양식 파일명. 직접 칠 수도 있습니다."
                   onChange={e => setTemplateName(e.target.value)} />
            <button className="btn btn-ghost" onClick={() => templateRef.current?.click()} disabled={locked}>불러오기</button>
            <input ref={templateRef} type="file" accept=".xlsx,.xlsm,.xls" style={{ display: 'none' }}
                   onChange={e => { const f = e.target.files?.[0]; e.target.value = ''; if (f) { setTemplateName(f.name); void saveTemplate(f); } }} />
            <button className="btn btn-primary" onClick={() => void saveTemplate()} disabled={locked}>이름만 저장</button>
          </div>

          <div className="mes-dim" style={{ marginTop: 12 }}>공정 플로우</div>
          <div className="mes-transfer">
            <div>
              <div className="mes-dim">부여된 플로우</div>
              <div className="mes-scroll" style={{ maxHeight: 180 }}>
                <table className="mes-table">
                  <tbody>
                    {(detail?.assignedFlows ?? []).map(f => (
                      <tr key={f.processRouteId} className={f.processRouteId === selAssigned ? 'picked' : ''}
                          onClick={() => setSelAssigned(f.processRouteId)}
                          onDoubleClick={() => void unassignFlow(f.processRouteId)} style={{ cursor: 'pointer' }}>
                        <td>{f.routeCode}</td><td>{f.routeName}</td>
                      </tr>
                    ))}
                    {(detail?.assignedFlows.length ?? 0) === 0 && (
                      <tr><td colSpan={2} className="mes-empty">부여된 플로우 없음</td></tr>
                    )}
                  </tbody>
                </table>
              </div>
            </div>
            <div className="mes-transfer-move">
              <button className="mes-sm" onClick={() => void assignFlow(null)} disabled={locked || selAvailable === null} title="부여">◀</button>
              <button className="mes-sm" onClick={() => void unassignFlow(null)} disabled={locked || selAssigned === null} title="해제">▶</button>
            </div>
            <div>
              <div className="mes-dim">추가 가능 플로우</div>
              <div className="mes-scroll" style={{ maxHeight: 180 }}>
                <table className="mes-table">
                  <tbody>
                    {(detail?.availableFlows ?? []).map(f => (
                      <tr key={f.processRouteId} className={f.processRouteId === selAvailable ? 'picked' : ''}
                          onClick={() => setSelAvailable(f.processRouteId)}
                          onDoubleClick={() => void assignFlow(f.processRouteId)} style={{ cursor: 'pointer' }}>
                        <td>{f.routeCode}</td><td>{f.routeName}</td>
                      </tr>
                    ))}
                    {(detail?.availableFlows.length ?? 0) === 0 && (
                      <tr><td colSpan={2} className="mes-empty">추가 가능한 플로우 없음</td></tr>
                    )}
                  </tbody>
                </table>
              </div>
            </div>
          </div>
        </div>

        {msg && <p className={`mes-alert ${isError ? 'error' : 'ok'}`}>{msg}</p>}
        {!selected && <p className="mes-dim">LINE · 양식 · 플로우 · 레시피 · 파라미터는 저장된 제품을 골라야 다룰 수 있습니다.</p>}
      </section>

      {/* 3열 — 레시피 / 파라미터 */}
      <section>
        <div className="mes-tabs">
          <button className={rightTab === 'recipe' ? 'active' : ''} onClick={() => setRightTab('recipe')}>공정별 레시피</button>
          <button className={rightTab === 'param' ? 'active' : ''} onClick={() => setRightTab('param')}>파라미터 (검사 항목)</button>
        </div>

        {rightTab === 'recipe' ? (
          <>
            <div className="mes-scroll" style={{ maxHeight: 260 }}>
              <table className="mes-table">
                <thead>
                  <tr><th>USE</th><th>RECIPE ID</th><th className="num">OPER</th><th className="num">MIN</th>
                      <th className="num">MAX</th><th>MAIN</th><th className="num">READ TIME</th></tr>
                </thead>
                <tbody>
                  {(detail?.recipes ?? []).map(r => (
                    <tr key={r.assignmentId} className={r.assignmentId === selRecipe?.assignmentId ? 'picked' : ''}
                        onClick={() => pickRecipe(r)} style={{ cursor: 'pointer' }}>
                      <td><span className={`mes-badge ${r.isActive ? 'done' : 'off'}`}>{r.isActive ? '사용' : '중지'}</span></td>
                      <td>{r.recipeCode}</td>
                      <td className="num">{r.operCode}</td>
                      <td className="num">{show(r.minValue)}</td>
                      <td className="num">{show(r.maxValue)}</td>
                      <td>{r.isMain ? 'Y' : ''}</td>
                      <td className="num">{show(r.readTimeMinutes)}</td>
                    </tr>
                  ))}
                  {(detail?.recipes.length ?? 0) === 0 && (
                    <tr><td colSpan={7} className="mes-empty">
                      {selected ? '배정된 레시피가 없습니다.' : '왼쪽에서 세정코드를 고르세요.'}
                    </td></tr>
                  )}
                </tbody>
              </table>
            </div>

            <div className="mes-info" style={{ marginTop: 10 }}>
              <div className="mes-form">
                <label><span>RECIPE ID</span>
                  <select value={recipeForm.recipeDefinitionId} disabled={locked}
                          onChange={e => setRecipeForm(f => ({ ...f, recipeDefinitionId: e.target.value }))}>
                    <option value="">-- 선택 --</option>
                    {recipesByOper.map(([oper, list]) => (
                      <optgroup key={oper} label={`OPER ${oper}`}>
                        {list.map(o => <option key={o.recipeId} value={o.recipeId}>{o.code} ({o.description})</option>)}
                      </optgroup>
                    ))}
                  </select></label>
                <div className="mes-pair">
                  <label><span>MIN</span>
                    <input className="input" value={recipeForm.minValue} disabled={locked}
                           onChange={e => setRecipeForm(f => ({ ...f, minValue: e.target.value }))} /></label>
                  <label><span>MAX</span>
                    <input className="input" value={recipeForm.maxValue} disabled={locked}
                           onChange={e => setRecipeForm(f => ({ ...f, maxValue: e.target.value }))} /></label>
                </div>
                <div className="mes-pair">
                  <label className="mes-check">
                    <input type="checkbox" checked={recipeForm.isActive} disabled={locked}
                           onChange={e => setRecipeForm(f => ({ ...f, isActive: e.target.checked }))} /> 사용
                  </label>
                  <label className="mes-check">
                    <input type="checkbox" checked={recipeForm.isMain} disabled={locked}
                           onChange={e => setRecipeForm(f => ({ ...f, isMain: e.target.checked }))} /> MAIN RCP
                  </label>
                </div>
              </div>
              <div className="mes-form-actions">
                <button className="btn btn-ghost" onClick={() => { setSelRecipe(null); setRecipeForm(emptyRecipe); }}>새로 작성</button>
                <button className="btn btn-ghost" onClick={() => void deleteRecipe()} disabled={locked || !selRecipe}>삭제</button>
                <button className="btn btn-ghost" onClick={() => void saveRecipe(true)} disabled={locked || !selRecipe}>수정</button>
                <button className="btn btn-primary" onClick={() => void saveRecipe(false)}
                        disabled={locked || !recipeForm.recipeDefinitionId}>신규저장</button>
              </div>
              <div className="mes-copy">
                <span className="mes-dim">다른 세정코드에서 가져오기 (이미 있는 것은 건너뜁니다)</span>
                <div className="mes-copy-row">
                  <input className="input" placeholder="원본 세정코드" value={recipeCopy} disabled={locked}
                         onChange={e => setRecipeCopy(e.target.value)} />
                  <button className="btn btn-ghost" onClick={() => void copyRecipes()}
                          disabled={locked || !recipeCopy.trim()}>가져오기</button>
                </div>
              </div>
            </div>
          </>
        ) : (
          <>
            <div className="mes-scroll" style={{ maxHeight: 260 }}>
              <table className="mes-table">
                <thead>
                  <tr><th>PARAMETER</th><th>DESC</th><th className="num">OPER</th><th className="num">Val.C</th>
                      <th className="num">MIN</th><th className="num">MAX</th><th>UNIT</th><th>성적서표기</th><th>USE</th></tr>
                </thead>
                <tbody>
                  {(detail?.parameters ?? []).map(p => (
                    <tr key={p.parameterId} className={p.parameterId === selParam?.parameterId ? 'picked' : ''}
                        onClick={() => pickParam(p)} style={{ cursor: 'pointer' }}>
                      <td>{p.code}</td>
                      <td>{p.description}</td>
                      <td className="num">{p.oper ?? '-'}</td>
                      <td className="num">{p.valueCount}</td>
                      <td className="num">{show(p.minValue)}</td>
                      <td className="num">{show(p.maxValue)}</td>
                      <td>{p.unit ?? ''}</td>
                      <td>{p.certificateLabel ?? ''}</td>
                      <td><span className={`mes-badge ${p.isActive ? 'done' : 'off'}`}>{p.isActive ? '사용' : '중지'}</span></td>
                    </tr>
                  ))}
                  {(detail?.parameters.length ?? 0) === 0 && (
                    <tr><td colSpan={9} className="mes-empty">
                      {selected ? '등록된 검사 항목이 없습니다.' : '왼쪽에서 세정코드를 고르세요.'}
                    </td></tr>
                  )}
                </tbody>
              </table>
            </div>

            <div className="mes-info" style={{ marginTop: 10 }}>
              <div className="mes-form">
                <label><span>PARAMETER</span>
                  <input className="input" list="mesParamCatalog" value={paramForm.code} disabled={locked}
                         onChange={e => {
                           const code = e.target.value;
                           const hit = ref?.parameterCatalog.find(c => c.code === code);
                           setParamForm(f => ({ ...f, code, description: hit?.description ?? f.description }));
                         }} />
                  <datalist id="mesParamCatalog">
                    {ref?.parameterCatalog.map(c => <option key={c.code} value={c.code}>{c.description}</option>)}
                  </datalist></label>
                <label><span>DESC</span>
                  <input className="input" value={paramForm.description} disabled={locked}
                         onChange={e => setParamForm(f => ({ ...f, description: e.target.value }))} /></label>
                <div className="mes-pair">
                  <label><span>OPER</span>
                    <select value={paramForm.oper} disabled={locked}
                            onChange={e => setParamForm(f => ({ ...f, oper: e.target.value }))}>
                      <option value="">-</option>
                      {ref?.opers.map(o => (
                        <option key={o.operCode} value={String(o.operCode)}>{o.operCode} {o.processName}</option>
                      ))}
                    </select></label>
                  <label><span>TYPE</span>
                    <select value={paramForm.parameterType} disabled={locked}
                            onChange={e => setParamForm(f => ({ ...f, parameterType: Number(e.target.value) }))}>
                      {PARAM_TYPES.map(t => <option key={t.value} value={t.value}>{t.label}</option>)}
                    </select></label>
                </div>
                <div className="mes-pair">
                  <label><span>VALUE COUNT (측정 점 수)</span>
                    <input className="input" type="number" min={1} max={5} value={paramForm.valueCount} disabled={locked}
                           onChange={e => setParamForm(f => ({ ...f, valueCount: Math.max(1, Number(e.target.value) || 1) }))} /></label>
                  <label><span>UNIT</span>
                    <input className="input" value={paramForm.unit} disabled={locked}
                           onChange={e => setParamForm(f => ({ ...f, unit: e.target.value }))} /></label>
                </div>
                <div className="mes-pair">
                  <label><span>MIN</span>
                    <input className="input" value={paramForm.minValue} disabled={locked}
                           onChange={e => setParamForm(f => ({ ...f, minValue: e.target.value }))} /></label>
                  <label><span>MAX</span>
                    <input className="input" value={paramForm.maxValue} disabled={locked}
                           onChange={e => setParamForm(f => ({ ...f, maxValue: e.target.value }))} /></label>
                </div>
                <label><span>성적서 표기명</span>
                  <input className="input" value={paramForm.certificateLabel} disabled={locked}
                         title="성적서 항목명과 맞출 이름. 비우면 PARAMETER 코드를 씁니다."
                         onChange={e => setParamForm(f => ({ ...f, certificateLabel: e.target.value }))} /></label>
                <label className="mes-check">
                  <input type="checkbox" checked={paramForm.isActive} disabled={locked}
                         onChange={e => setParamForm(f => ({ ...f, isActive: e.target.checked }))} /> 사용
                </label>
              </div>
              <div className="mes-form-actions">
                <button className="btn btn-ghost" onClick={() => { setSelParam(null); setParamForm(emptyParam); }}>새로 작성</button>
                <button className="btn btn-ghost" onClick={() => void deleteParam()} disabled={locked || !selParam}>삭제</button>
                <button className="btn btn-ghost" onClick={() => void saveParam(true)} disabled={locked || !selParam}>수정</button>
                <button className="btn btn-primary" onClick={() => void saveParam(false)}
                        disabled={locked || !paramForm.code.trim()}>신규저장</button>
              </div>
              <div className="mes-copy">
                <span className="mes-dim">다른 세정코드에서 가져오기 (코드 · OPER 가 겹치면 건너뜁니다)</span>
                <div className="mes-copy-row">
                  <input className="input" placeholder="원본 세정코드" value={paramCopy} disabled={locked}
                         onChange={e => setParamCopy(e.target.value)} />
                  <button className="btn btn-ghost" onClick={() => void copyParams()}
                          disabled={locked || !paramCopy.trim()}>가져오기</button>
                </div>
              </div>
            </div>
          </>
        )}
      </section>
    </div>
  );
}
