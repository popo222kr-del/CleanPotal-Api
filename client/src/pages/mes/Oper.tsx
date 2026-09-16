import { useCallback, useEffect, useRef, useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { api } from '../../api/client';
import { useAccess } from '../../auth/useAccess';
import Mes from '../Mes';
import { dateTime, hours, statusLabel, statusTone, type OperLot } from './lot';
import './Mes.css';

// MES OPER(공정) 화면 — 작업자가 LOT 을 처리하는 곳.
//
// 실행을 막는 조건(사유코드 · 레시피/설비 · SPEC OUT · 출고검사 NG · READ TIME)은 안전 장치라
// 여기에 옮겨 적지 않는다. 서버가 판정해 outcome 으로 알려 주고, 이 화면은 그대로 보여 준다.

// 출력 관리(성적서 · 런시트 출력)를 아직 안 옮겼다. 그 창을 먼저 띄워야 하는 검사 공정 둘은
// 옮길 때까지 기존 MES 화면을 그대로 쓴다 — 반쪽짜리로 열어 두면 출력 없이 공정이 넘어간다.
const NOT_YET_PORTED = [2100, 7000];

type Screen = {
  operCode: number; processDefinitionId: number; operName: string; screenName: string;
  isRecipeOper: boolean; requiresRecipeAndEquipment: boolean; supportsMultiSelect: boolean;
};
type InspPoint = { label: string; value: string | null };
type InspRow = {
  parameterDefinitionId: number; code: string; description: string;
  minValue: number | null; maxValue: number | null;
  referenceInputValue: string | null; inputValue: string | null; comment: string | null;
  isYesNo: boolean; isOkNgCc: boolean; isMultiPoint: boolean; points: InspPoint[];
};
type Panel = {
  lotId: number; lotNumber: string; matId: string; matDesc: string; sn: string; clnCount: number;
  recipeDefinitionId: number | null; pmResId: string | null; resId: string | null;
  cmtAets: string | null; commentHistory: string | null;
  showsInInsp: boolean; inInspEditable: boolean; showsFiInsp: boolean;
  inInspRows: InspRow[]; fiInspRows: InspRow[];
  recipes: { recipeDefinitionId: number; recipeDescription: string; readTimeMinutes: number | null }[];
  transitions: { transitionId: number; description: string; requiresReasonCode: boolean }[];
  batchMembers: { lotNumber: string; serialNumber: string }[];
};
type Reason = { code: string; description: string };
type SpecOut = { parameterDefinitionId: number; reason: string };
type ExecResult = { outcome: 'blocked' | 'needsConfirm' | 'openOutput' | 'done'; message: string; outputLotId: number | null; outputLotNumber: string | null };

/** 서버로 보낼 검사값 — 다측정은 '|' 로 이어 붙인 값이 곧 저장값이다. */
const toInputs = (rows: InspRow[]) => rows.map(r => ({
  parameterDefinitionId: r.parameterDefinitionId,
  inputValue: r.isMultiPoint ? joinPoints(r.points) : r.inputValue,
  comment: r.comment,
}));
function joinPoints(points: InspPoint[]): string | null {
  if (points.length === 0) return null;
  if (points.every(p => !p.value?.trim())) return null;
  return points.map(p => (p.value ?? '').trim()).join('|');
}

/** 남은 시간(분). MES 의 Over T 와 같은 계산이다. */
function overT(endTime: string | null | undefined) {
  if (!endTime) return '';
  return String(Math.floor((new Date(endTime).getTime() - Date.now()) / 60000));
}

export default function MesOper() {
  const { operCode: operParam } = useParams();
  const operCode = Number(operParam);

  // 아직 안 옮긴 공정은 기존 화면으로. (훅 순서가 바뀌지 않도록 컴포넌트를 나눠 둔다)
  if (!Number.isFinite(operCode) || NOT_YET_PORTED.includes(operCode)) return <Mes />;
  return <OperScreen operCode={operCode} />;
}

function OperScreen({ operCode }: { operCode: number }) {
  const nav = useNavigate();
  const acc = useAccess();
  const canEdit = acc.canEditMes;
  const [params] = useSearchParams();
  const lotQuery = params.get('lot') ?? '';

  const [screen, setScreen] = useState<Screen | null>(null);
  const [lots, setLots] = useState<OperLot[]>([]);
  const [keyword, setKeyword] = useState('');
  const [selected, setSelected] = useState<OperLot | null>(null);
  const [multi, setMulti] = useState<Set<number>>(new Set());

  const [panel, setPanel] = useState<Panel | null>(null);
  const [inRows, setInRows] = useState<InspRow[]>([]);
  const [fiRows, setFiRows] = useState<InspRow[]>([]);
  const [recipeId, setRecipeId] = useState<number | null>(null);
  const [resId, setResId] = useState('');
  const [cmt, setCmt] = useState('');
  const [tranId, setTranId] = useState<number | null>(null);
  const [reasons, setReasons] = useState<Reason[]>([]);
  const [reasonCode, setReasonCode] = useState('');
  const [specOut, setSpecOut] = useState<SpecOut[]>([]);

  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [isError, setIsError] = useState(false);
  const [confirmMsg, setConfirmMsg] = useState<string | null>(null);
  const [pendingOutput, setPendingOutput] = useState<ExecResult | null>(null);

  const appliedLotQuery = useRef<string | null>(null);

  const editableRows = panel?.showsFiInsp ? fiRows : (panel?.inInspEditable ? inRows : []);

  const loadLots = useCallback(async (q: string) => {
    const search = q.trim() ? `?keyword=${encodeURIComponent(q.trim())}` : '';
    return api.get<OperLot[]>(`/api/mes/oper/${operCode}/lots${search}`);
  }, [operCode]);

  const clearSelection = useCallback(() => {
    setSelected(null); setPanel(null); setInRows([]); setFiRows([]);
    setRecipeId(null); setResId(''); setCmt('');
    setTranId(null); setReasons([]); setReasonCode('');
    setSpecOut([]); setConfirmMsg(null); setPendingOutput(null);
  }, []);

  const select = useCallback(async (lot: OperLot) => {
    setSelected(lot); setStatus(null); setConfirmMsg(null); setPendingOutput(null);
    const p = await api.get<Panel>(`/api/mes/oper/${operCode}/lots/${lot.lotId}/panel`);
    setPanel(p);
    setInRows(p.inInspRows); setFiRows(p.fiInspRows);
    setRecipeId(p.recipeDefinitionId); setResId(p.resId ?? ''); setCmt(p.cmtAets ?? '');
    setTranId(null); setReasons([]); setReasonCode(''); setSpecOut([]);
  }, [operCode]);

  const reload = useCallback(async (q: string) => {
    setBusy(true);
    try {
      const list = await loadLots(q);
      setLots(list);
      setMulti(prev => new Set([...prev].filter(id => list.some(l => l.lotId === id))));
      setSelected(prev => {
        if (!prev) return null;
        const again = list.find(l => l.lotId === prev.lotId);
        if (!again) { clearSelection(); return null; }
        return again;
      });
      return list;
    } finally { setBusy(false); }
  }, [loadLots, clearSelection]);

  // 화면 진입 / 공정 변경
  useEffect(() => {
    let alive = true;
    void (async () => {
      try {
        const s = await api.get<Screen>(`/api/mes/oper/${operCode}`);
        if (!alive) return;
        setScreen(s);
        clearSelection();
        setKeyword('');
        appliedLotQuery.current = null;
        await reload('');
      } catch {
        if (alive) { setIsError(true); setStatus('공정 화면을 불러오지 못했습니다.'); }
      }
    })();
    return () => { alive = false; };
  }, [operCode, reload, clearSelection]);

  // LOT 스캔에서 ?lot= 로 넘어오면 그 LOT 을 목록에서 골라 준다.
  useEffect(() => {
    if (!lotQuery || lots.length === 0 || appliedLotQuery.current === lotQuery) return;
    const code = lotQuery.trim().toUpperCase();
    const hit = lots.find(l =>
      l.lotNumber.toUpperCase() === code ||
      l.serialNumber.toUpperCase() === code ||
      (l.exportNumber ?? '').toUpperCase() === code);
    appliedLotQuery.current = lotQuery;
    if (hit) {
      if (screen?.supportsMultiSelect) setMulti(prev => new Set(prev).add(hit.lotId));
      void select(hit);
      setIsError(false);
      setStatus(`스캔: ${hit.lotNumber} (S/N ${hit.serialNumber}) 선택됨`);
    } else {
      setIsError(true);
      setStatus(`LOT ${lotQuery} 은(는) 이 공정의 처리 대상 목록에 없습니다.`);
    }
  }, [lotQuery, lots, screen, select]);

  async function onTranChange(value: string) {
    const id = value ? Number(value) : null;
    setTranId(id);
    setReasonCode(''); setReasons([]);
    if (id === null || !panel) return;
    const t = panel.transitions.find(x => x.transitionId === id);
    if (!t?.requiresReasonCode) return;
    try {
      setReasons(await api.get<Reason[]>(`/api/mes/oper/lots/${panel.lotId}/reasons?transitionId=${id}`));
    } catch { /* 사유 목록만 비어 있게 둔다 — 실행할 때 서버가 다시 막는다 */ }
  }

  // SPEC 판정은 서버에 묻는다. 입력할 때마다 부르지 않게 잠깐 모았다가 한 번 보낸다.
  const specTimer = useRef<number | null>(null);
  const requestSpecCheck = useCallback((rows: InspRow[]) => {
    if (!panel) return;
    if (specTimer.current) window.clearTimeout(specTimer.current);
    specTimer.current = window.setTimeout(() => {
      void api.post<SpecOut[]>('/api/mes/oper/spec-check', {
        lotId: panel.lotId, recipeDefinitionId: recipeId, resId, cmtAets: cmt, inputs: toInputs(rows),
      }).then(setSpecOut).catch(() => { /* 판정만 못 받는다 */ });
    }, 400);
  }, [panel, recipeId, resId, cmt]);

  useEffect(() => () => { if (specTimer.current) window.clearTimeout(specTimer.current); }, []);

  function patchRow(isFi: boolean, parameterDefinitionId: number, change: Partial<InspRow>) {
    const apply = (rows: InspRow[]) =>
      rows.map(r => (r.parameterDefinitionId === parameterDefinitionId ? { ...r, ...change } : r));
    if (isFi) setFiRows(rows => { const next = apply(rows); requestSpecCheck(next); return next; });
    else setInRows(rows => { const next = apply(rows); requestSpecCheck(next); return next; });
  }

  function patchPoint(isFi: boolean, parameterDefinitionId: number, label: string, value: string) {
    const rows = isFi ? fiRows : inRows;
    const row = rows.find(r => r.parameterDefinitionId === parameterDefinitionId);
    if (!row) return;
    patchRow(isFi, parameterDefinitionId, {
      points: row.points.map(p => (p.label === label ? { ...p, value } : p)),
    });
  }

  function body(confirmed: boolean) {
    const targets = screen?.supportsMultiSelect && multi.size > 1
      ? lots.filter(l => multi.has(l.lotId)).map(l => l.lotId)
      : [panel!.lotId];
    // 패널의 LOT 이 항상 먼저다 — 검사값·코멘트는 그 LOT 에 저장된다.
    const ordered = [panel!.lotId, ...targets.filter(id => id !== panel!.lotId)];
    return {
      lotId: panel!.lotId, recipeDefinitionId: recipeId, resId, cmtAets: cmt,
      inputs: toInputs(editableRows),
      lotIds: ordered, transitionId: tranId, reasonCode: reasonCode || null, confirmed,
    };
  }

  function show(result: ExecResult) {
    setStatus(result.message);
    setIsError(result.outcome === 'blocked');
    setConfirmMsg(result.outcome === 'needsConfirm' ? result.message : null);
    setPendingOutput(result.outcome === 'openOutput' ? result : null);
  }

  async function save() {
    if (busy || !panel) return;
    setBusy(true); setStatus(null); setConfirmMsg(null);
    try {
      show(await api.post<ExecResult>('/api/mes/oper/panel', {
        lotId: panel.lotId, recipeDefinitionId: recipeId, resId, cmtAets: cmt, inputs: toInputs(editableRows),
      }));
      await reload(keyword);
    } catch {
      setIsError(true); setStatus('저장 중 문제가 발생했습니다.');
    } finally { setBusy(false); }
  }

  async function execute(confirmed: boolean) {
    if (busy || !panel || tranId === null) return;
    setBusy(true); setStatus(null); setConfirmMsg(null);
    try {
      const result = await api.post<ExecResult>(`/api/mes/oper/${operCode}/execute`, body(confirmed));
      show(result);
      if (result.outcome === 'done') { setMulti(new Set()); clearSelection(); }
      if (result.outcome === 'done' || result.outcome === 'blocked') await reload(keyword);
    } catch {
      setIsError(true); setStatus('실행 중 문제가 발생했습니다.');
    } finally { setBusy(false); }
  }

  const selectedTran = panel?.transitions.find(t => t.transitionId === tranId);
  const specReason = (id: number) => specOut.find(s => s.parameterDefinitionId === id)?.reason;

  function inspTable(rows: InspRow[], editable: boolean, isFi: boolean) {
    return (
      <div className="mes-scroll">
        <table className="mes-table mes-grid">
          <thead>
            <tr>
              <th>PARAMETER ID</th><th>DESC</th><th className="num">MIN</th><th className="num">MAX</th>
              {isFi && <th className="num">IN VALUE</th>}
              <th>{isFi ? 'FI INSP' : 'IN INSP'}</th>
              <th>COMMENT</th>
            </tr>
          </thead>
          <tbody>
            {rows.map(r => {
              const bad = specReason(r.parameterDefinitionId);
              return (
                <tr key={r.parameterDefinitionId} className={bad ? 'specout' : ''}>
                  <td>{r.code}</td>
                  <td>{r.description}</td>
                  <td className="num">{r.minValue ?? '-'}</td>
                  <td className="num">{r.maxValue ?? '-'}</td>
                  {isFi && <td className="num dim">{r.referenceInputValue ?? ''}</td>}
                  <td>
                    {!editable ? <span>{r.inputValue}</span>
                      : r.isMultiPoint ? (
                        <span className="mes-points">
                          {r.points.map(p => (
                            <label key={p.label}>
                              <span>{p.label}</span>
                              <input value={p.value ?? ''} disabled={!canEdit}
                                     onChange={e => patchPoint(isFi, r.parameterDefinitionId, p.label, e.target.value)} />
                            </label>
                          ))}
                        </span>
                      ) : (
                        <input
                          list={r.isYesNo ? 'mesYn' : r.isOkNgCc ? 'mesResult' : undefined}
                          value={r.inputValue ?? ''}
                          disabled={!canEdit}
                          onChange={e => patchRow(isFi, r.parameterDefinitionId, { inputValue: e.target.value })}
                        />
                      )}
                  </td>
                  <td>
                    <input value={r.comment ?? ''} disabled={!editable || !canEdit}
                           onChange={e => patchRow(isFi, r.parameterDefinitionId, { comment: e.target.value })} />
                  </td>
                </tr>
              );
            })}
            {rows.length === 0 && (
              <tr><td colSpan={isFi ? 7 : 6} className="mes-empty">파라미터 없음</td></tr>
            )}
          </tbody>
        </table>
        {specOut.map(s => <p key={s.parameterDefinitionId} className="mes-specout-note">⚠ {s.reason}</p>)}
      </div>
    );
  }

  return (
    <div className="mes-page">
      {/* Y/N · OK/NG/CC 는 고르기도 하고 직접 치기도 한다(MES 와 같다) */}
      <datalist id="mesYn"><option value="Y" /><option value="N" /></datalist>
      <datalist id="mesResult"><option value="OK" /><option value="NG" /><option value="CC" /></datalist>

      <header className="pg-header">
        <div><h2>{screen?.operName ?? `OPER ${operCode}`}</h2></div>
        <input className="input mes-search" placeholder="LOT번호 / S/N / 제품명 / 반출번호"
               value={keyword} onChange={e => setKeyword(e.target.value)}
               onKeyDown={e => { if (e.key === 'Enter') void reload(keyword); }} />
        <button className="btn btn-ghost" onClick={() => void reload(keyword)} disabled={busy}>조회</button>
        <button className="btn btn-ghost" onClick={() => nav('/mes/scan')}>스캔</button>
        {screen?.supportsMultiSelect && multi.size > 1 && (
          <span className="mes-badge run">다중선택 {multi.size}건</span>
        )}
      </header>

      <div className="pg-body">
        {!canEdit && <p className="mes-alert warn">공정 처리는 MES 편집 권한이 필요합니다. 조회만 가능합니다.</p>}

        <div className="mes-scroll">
          <table className="mes-table">
            <thead>
              <tr>
                {screen?.supportsMultiSelect && <th></th>}
                <th>LINE</th><th>업체명</th><th>반출번호</th><th>세정코드</th><th>제품명</th><th>S/N</th>
                <th>분임조</th><th>PROCESS</th><th>BATCH</th><th>STATUS</th><th>RECIPE</th><th>설비명</th>
                <th>AETS 입고</th><th>공정 입고</th><th className="num">TAT</th><th>작업자</th>
                <th>Start T</th><th>End T</th><th className="num">Over T</th><th></th>
              </tr>
            </thead>
            <tbody>
              {lots.map(l => (
                <tr key={l.lotId} className={l.lotId === selected?.lotId ? 'picked' : ''}
                    onClick={() => void select(l)} style={{ cursor: 'pointer' }}>
                  {screen?.supportsMultiSelect && (
                    <td onClick={e => e.stopPropagation()}>
                      <input type="checkbox" checked={multi.has(l.lotId)}
                             onChange={e => setMulti(prev => {
                               const next = new Set(prev);
                               if (e.target.checked) next.add(l.lotId); else next.delete(l.lotId);
                               return next;
                             })} />
                    </td>
                  )}
                  <td>{l.line ?? ''}</td>
                  <td>{l.pmEquipmentName ?? ''}</td>
                  <td>{l.exportNumber ?? ''}</td>
                  <td>{l.cleaningCode ?? ''}</td>
                  <td>{l.productName}</td>
                  <td>{l.serialNumber}</td>
                  <td>{l.teamName ?? ''}</td>
                  <td>{l.processLabel ?? ''}</td>
                  <td>{l.isBatch ? 'Y' : ''}</td>
                  <td><span className={`mes-badge ${statusTone(l.currentStatus)}`}>{statusLabel(l.currentStatus)}</span></td>
                  <td>{l.recipeName ?? ''}</td>
                  <td>{l.equipmentId ?? ''}</td>
                  <td>{dateTime(l.receivedDate)}</td>
                  <td>{dateTime(l.stageArrivedAt)}</td>
                  <td className="num">{hours(l.tatHours)}</td>
                  <td>{l.worker ?? ''}</td>
                  <td>{dateTime(l.recipeStartTime)}</td>
                  <td>{dateTime(l.recipeEndTime)}</td>
                  <td className="num">{overT(l.recipeEndTime)}</td>
                  <td onClick={e => e.stopPropagation()}>
                    <button className="mes-sm" onClick={() => nav(`/mes/history?lot=${encodeURIComponent(l.lotNumber)}`)}>현황</button>
                  </td>
                </tr>
              ))}
              {lots.length === 0 && (
                <tr><td colSpan={21} className="mes-empty">이 공정에 해당하는 LOT 이 없습니다.</td></tr>
              )}
            </tbody>
          </table>
        </div>

        {panel && panel.batchMembers.length > 0 && (
          <p className="mes-dim">
            배치 묶음 {panel.batchMembers.length}건: {panel.batchMembers.map(b => `${b.lotNumber}/${b.serialNumber}`).join(', ')}
          </p>
        )}

        <div className="mes-oper-detail">
          <section className="mes-info">
            {!panel ? <p className="mes-dim">목록에서 LOT 을 선택하세요.</p> : (
              <>
                <dl>
                  <div className="wide"><dt>LOT</dt><dd className="strong">{panel.lotNumber}</dd></div>
                  <div><dt>MAT ID</dt><dd>{panel.matId}</dd></div>
                  <div><dt>COUNT</dt><dd>{panel.clnCount}</dd></div>
                  <div className="wide"><dt>MAT DESC</dt><dd>{panel.matDesc}</dd></div>
                  <div><dt>S/N</dt><dd>{panel.sn}</dd></div>
                  <div><dt>LINE</dt><dd>{selected?.line ?? '-'}</dd></div>
                  <div><dt>업체명</dt><dd>{panel.pmResId ?? '-'}</dd></div>
                  <div>
                    <dt>RECIPE ID</dt>
                    <dd>
                      <select value={recipeId ?? ''} disabled={!screen?.isRecipeOper || !canEdit}
                              onChange={e => setRecipeId(e.target.value ? Number(e.target.value) : null)}>
                        <option value="">-- 선택 --</option>
                        {panel.recipes.map(r => (
                          <option key={r.recipeDefinitionId} value={r.recipeDefinitionId}>{r.recipeDescription}</option>
                        ))}
                      </select>
                    </dd>
                  </div>
                  <div>
                    <dt>설비호기</dt>
                    <dd><input value={resId} disabled={!screen?.isRecipeOper || !canEdit}
                               onChange={e => setResId(e.target.value)} /></dd>
                  </div>
                </dl>

                <textarea className="mes-comment" placeholder="CMT_AETS (코멘트)" value={cmt}
                          disabled={!canEdit} onChange={e => setCmt(e.target.value)} />
                {panel.commentHistory && <p className="mes-dim">이전 코멘트: {panel.commentHistory}</p>}

                <div className="mes-tran">
                  <select value={tranId ?? ''} disabled={!canEdit} onChange={e => void onTranChange(e.target.value)}>
                    <option value="">TRAN 코드 선택</option>
                    {panel.transitions.map(t => (
                      <option key={t.transitionId} value={t.transitionId}>{t.description}</option>
                    ))}
                  </select>
                  <select value={reasonCode} disabled={!selectedTran?.requiresReasonCode || !canEdit}
                          onChange={e => setReasonCode(e.target.value)}>
                    <option value="">사유 코드 선택</option>
                    {reasons.map(r => <option key={r.code} value={r.code}>{r.description}</option>)}
                  </select>
                </div>

                {status && !confirmMsg && !pendingOutput && (
                  <p className={`mes-alert ${isError ? 'error' : 'ok'}`}>{status}</p>
                )}
                {confirmMsg && (
                  <div className="mes-alert warn">
                    <div className="mes-pre">{confirmMsg}</div>
                    <div className="mes-confirm">
                      <button className="btn btn-primary" onClick={() => void execute(true)} disabled={busy}>그대로 진행</button>
                      <button className="btn btn-ghost" onClick={() => setConfirmMsg(null)}>취소</button>
                    </div>
                  </div>
                )}
                {pendingOutput && (
                  <div className="mes-alert warn">
                    <div className="mes-pre">
                      이 공정은 출력 관리(성적서 · 런시트)를 먼저 처리해야 전산이 넘어갑니다.
                      출력 관리는 아직 포털로 옮기는 중이라 기존 MES 화면에서 처리해 주세요.
                    </div>
                  </div>
                )}
              </>
            )}
          </section>

          <section className="mes-info">
            {!panel ? <p className="mes-dim">목록에서 LOT 을 선택하세요.</p>
              : !panel.showsInInsp && !panel.showsFiInsp
                ? <p className="mes-dim">이 공정은 검사값 입력이 없습니다. 왼쪽에서 TRAN 을 골라 실행하세요.</p>
                : (
                  <>
                    {panel.showsInInsp && (
                      <>
                        <div className="mes-title">IN INSP {panel.inInspEditable ? '' : '(참고 · 읽기전용)'}</div>
                        {inspTable(inRows, panel.inInspEditable, false)}
                      </>
                    )}
                    {panel.showsFiInsp && (
                      <>
                        <div className="mes-title" style={{ marginTop: 12 }}>FI INSP</div>
                        {inspTable(fiRows, true, true)}
                      </>
                    )}
                  </>
                )}
          </section>
        </div>

        <div className="mes-actions">
          <button className="btn btn-ghost" onClick={() => void save()} disabled={busy || !panel || !canEdit}>저장</button>
          <button className="btn btn-ghost" onClick={() => void reload(keyword)} disabled={busy}>조회</button>
          <button className="btn btn-primary" onClick={() => void execute(false)}
                  disabled={busy || !panel || tranId === null || !canEdit}>실행</button>
        </div>
      </div>
    </div>
  );
}
