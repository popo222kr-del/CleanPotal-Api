import { useCallback, useEffect, useState } from 'react';
import { api } from '../../../api/client';
import { useAccess } from '../../../auth/useAccess';
import '../Mes.css';

// 셋업 > 공정 관리.
// 왼쪽은 공정 하나하나(입고·세정·출고검사…), 오른쪽은 그것을 순서대로 엮은 플로우.
// 같은 공정이 한 플로우에 여러 번 들어갈 수 있어(LASER 경로의 세정·건조 반복) 순서는 집합이 아니라 목록이다.
// 새 공정은 기존 플로우에 저절로 들어가지 않는다 — 플로우에서 직접 추가해야 한다.

type Process = {
  processDefinitionId: number; processCode: string; processName: string; operCode: number; isActive: boolean;
};
type Step = { stepOrder: number; processDefinitionId: number; processCode: string; processName: string };
type Route = {
  processRouteId: number; routeCode: string; routeName: string; isActive: boolean; steps: Step[];
};
type SetupData = { processes: Process[]; routes: Route[] };
type Result = { success: boolean; message: string };

export default function ProcessTab() {
  const canEdit = useAccess().canEditMes;
  const [data, setData] = useState<SetupData>({ processes: [], routes: [] });
  const [busy, setBusy] = useState(false);

  // 공정
  const [selProcess, setSelProcess] = useState<Process | null>(null);
  const [processCode, setProcessCode] = useState('');
  const [processName, setProcessName] = useState('');
  const [msg, setMsg] = useState<string | null>(null);
  const [isError, setIsError] = useState(false);

  // 플로우
  const [selRoute, setSelRoute] = useState<Route | null>(null);
  const [routeCode, setRouteCode] = useState('');
  const [routeName, setRouteName] = useState('');
  const [steps, setSteps] = useState<Process[]>([]);
  const [selStep, setSelStep] = useState<number | null>(null);
  const [addId, setAddId] = useState('');
  const [routeMsg, setRouteMsg] = useState<string | null>(null);
  const [routeError, setRouteError] = useState(false);

  const load = useCallback(async () => {
    setData(await api.get<SetupData>('/api/mes/setup/processes'));
  }, []);

  useEffect(() => {
    void (async () => {
      try { await load(); }
      catch { setIsError(true); setMsg('공정 목록을 불러오는 중 문제가 발생했습니다.'); }
    })();
  }, [load]);

  async function run(work: () => Promise<Result>, ok: (m: string) => void, fail: (m: string) => void, after?: () => void) {
    if (busy) return;
    setBusy(true);
    try {
      const r = await work();
      (r.success ? ok : fail)(r.message);
      if (r.success) { await load(); after?.(); }
    } catch {
      fail('처리 중 문제가 발생했습니다.');
    } finally { setBusy(false); }
  }

  const setOk = (m: string) => { setIsError(false); setMsg(m); };
  const setFail = (m: string) => { setIsError(true); setMsg(m); };
  const setRouteOk = (m: string) => { setRouteError(false); setRouteMsg(m); };
  const setRouteFail = (m: string) => { setRouteError(true); setRouteMsg(m); };

  // ── 공정 ──
  function newProcess() { setSelProcess(null); setProcessCode(''); setProcessName(''); setMsg(null); }
  function pickProcess(p: Process) {
    setSelProcess(p); setProcessCode(p.processCode); setProcessName(p.processName); setMsg(null);
  }
  const saveProcess = () => run(() => {
    const body = { processCode: processCode.trim(), processName: processName.trim() };
    return selProcess
      ? api.put<Result>(`/api/mes/setup/processes/${selProcess.processDefinitionId}`, body)
      : api.post<Result>('/api/mes/setup/processes', body);
  }, setOk, setFail, newProcess);

  const toggleProcess = (p: Process) => run(
    () => api.post<Result>(`/api/mes/setup/processes/${p.processDefinitionId}/active`, { isActive: !p.isActive }),
    setOk, setFail);

  // ── 플로우 ──
  function newRoute() {
    setSelRoute(null); setRouteCode(''); setRouteName('');
    setSteps([]); setSelStep(null); setRouteMsg(null);
  }
  function pickRoute(r: Route) {
    setSelRoute(r); setRouteCode(r.routeCode); setRouteName(r.routeName);
    // 순서 목록은 공정 정의를 참조한다. 목록에 없는(지워진) 공정은 이름만 들고 온다.
    setSteps(r.steps.map(s =>
      data.processes.find(p => p.processDefinitionId === s.processDefinitionId)
      ?? { processDefinitionId: s.processDefinitionId, processCode: s.processCode, processName: s.processName, operCode: 0, isActive: false }));
    setSelStep(null); setRouteMsg(null);
  }
  function addStep() {
    const p = data.processes.find(x => x.processDefinitionId === Number(addId));
    if (!p) return;
    setSteps(s => [...s, p]);   // 같은 공정을 여러 번 넣을 수 있다
    setSelStep(steps.length);   // 방금 넣은 줄을 골라 둔다 — 바로 ▲▼ 로 자리를 잡을 수 있게
  }
  function removeStep(i: number) {
    setSteps(s => s.filter((_, idx) => idx !== i));
    setSelStep(null);
  }
  function move(delta: number) {
    if (selStep === null) return;
    const to = selStep + delta;
    if (to < 0 || to >= steps.length) return;
    setSteps(s => { const n = [...s]; [n[selStep], n[to]] = [n[to], n[selStep]]; return n; });
    setSelStep(to);
  }
  const saveRoute = () => run(() => {
    const body = {
      routeCode: routeCode.trim(),
      routeName: routeName.trim(),
      processDefinitionIds: steps.map(s => s.processDefinitionId),
    };
    return selRoute
      ? api.put<Result>(`/api/mes/setup/routes/${selRoute.processRouteId}`, body)
      : api.post<Result>('/api/mes/setup/routes', body);
  }, setRouteOk, setRouteFail, newRoute);

  const toggleRoute = () => {
    if (!selRoute) return;
    return run(() => api.post<Result>(`/api/mes/setup/routes/${selRoute.processRouteId}/active`,
      { isActive: !selRoute.isActive }), setRouteOk, setRouteFail, () => setSelRoute(null));
  };

  return (
    <div className="mes-process-setup">
      <section>
        <div className="mes-title">공정 등록 / 수정</div>
        <div className="mes-process-cols">
          <div className="mes-info">
            <div className="mes-form">
              <label><span>공정 코드</span>
                <input className="input" value={processCode} disabled={!canEdit}
                       onChange={e => setProcessCode(e.target.value)} /></label>
              <label><span>공정명</span>
                <input className="input" value={processName} disabled={!canEdit}
                       onChange={e => setProcessName(e.target.value)} /></label>
            </div>
            {msg && <p className={`mes-alert ${isError ? 'error' : 'ok'}`}>{msg}</p>}
            <div className="mes-form-actions">
              <button className="btn btn-ghost" onClick={newProcess} disabled={busy}>새로 작성</button>
              <button className="btn btn-primary" onClick={() => void saveProcess()}
                      disabled={busy || !canEdit || !processName.trim()}>저장</button>
            </div>
          </div>

          <div className="mes-scroll tall">
            <table className="mes-table">
              <thead><tr><th className="num">OPER</th><th>공정 코드</th><th>공정명</th><th>상태</th><th></th></tr></thead>
              <tbody>
                {data.processes.map(p => (
                  <tr key={p.processDefinitionId}
                      className={p.processDefinitionId === selProcess?.processDefinitionId ? 'picked' : ''}
                      onClick={() => pickProcess(p)} style={{ cursor: 'pointer' }}>
                    <td className="num">{p.operCode}</td>
                    <td>{p.processCode}</td>
                    <td>{p.processName}</td>
                    <td><span className={`mes-badge ${p.isActive ? 'done' : 'off'}`}>{p.isActive ? '사용' : '중지'}</span></td>
                    <td onClick={e => e.stopPropagation()}>
                      {canEdit && (
                        <button className="mes-sm" onClick={() => void toggleProcess(p)} disabled={busy}>
                          {p.isActive ? '중지' : '활성화'}
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
                {data.processes.length === 0 && (
                  <tr><td colSpan={5} className="mes-empty">등록된 공정이 없습니다.</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </div>
      </section>

      <section>
        <div className="mes-title">공정 플로우 관리</div>
        <div className="mes-process-cols">
          <div className="mes-info">
            <div className="mes-form">
              <label><span>플로우 코드</span>
                <input className="input" value={routeCode} disabled={!canEdit}
                       onChange={e => setRouteCode(e.target.value)} /></label>
              <label><span>플로우 이름</span>
                <input className="input" value={routeName} disabled={!canEdit}
                       onChange={e => setRouteName(e.target.value)} /></label>
            </div>
            <div className="mes-scroll" style={{ marginTop: 10, maxHeight: 200 }}>
              <table className="mes-table">
                <thead><tr><th>코드</th><th>이름</th><th>상태</th></tr></thead>
                <tbody>
                  {data.routes.map(r => (
                    <tr key={r.processRouteId} className={r.processRouteId === selRoute?.processRouteId ? 'picked' : ''}
                        onClick={() => pickRoute(r)} style={{ cursor: 'pointer' }}>
                      <td>{r.routeCode}</td>
                      <td>{r.routeName}</td>
                      <td><span className={`mes-badge ${r.isActive ? 'done' : 'off'}`}>{r.isActive ? '사용' : '중지'}</span></td>
                    </tr>
                  ))}
                  {data.routes.length === 0 && (
                    <tr><td colSpan={3} className="mes-empty">등록된 공정 플로우가 없습니다.</td></tr>
                  )}
                </tbody>
              </table>
            </div>
            <div className="mes-form-actions">
              <button className="btn btn-ghost" onClick={newRoute} disabled={busy}>새 플로우</button>
              <button className="btn btn-ghost" onClick={() => void toggleRoute()}
                      disabled={busy || !selRoute || !canEdit}
                      title="누르면 사용 · 중지가 바로 전환됩니다">
                {selRoute?.isActive ? '중지하기' : '사용하기'}
              </button>
              <button className="btn btn-primary" onClick={() => void saveRoute()}
                      disabled={busy || !canEdit || !routeCode.trim()}>저장</button>
            </div>
          </div>

          <div>
            <div className="mes-dim">공정 순서 {selRoute ? `(${selRoute.routeCode})` : '(새 플로우)'}</div>
            <div className="mes-steps">
              <div className="mes-scroll" style={{ flex: 1, maxHeight: 240 }}>
                <table className="mes-table">
                  <thead><tr><th className="num">#</th><th>공정</th><th></th></tr></thead>
                  <tbody>
                    {steps.map((s, i) => (
                      <tr key={`${s.processDefinitionId}-${i}`} className={i === selStep ? 'picked' : ''}
                          onClick={() => setSelStep(i)} style={{ cursor: 'pointer' }}>
                        <td className="num">{i + 1}</td>
                        <td>{s.processName}</td>
                        <td onClick={e => e.stopPropagation()}>
                          <button className="mes-sm" onClick={() => removeStep(i)} disabled={!canEdit}>제거</button>
                        </td>
                      </tr>
                    ))}
                    {steps.length === 0 && (
                      <tr><td colSpan={3} className="mes-empty">아래에서 공정을 골라 추가하세요.</td></tr>
                    )}
                  </tbody>
                </table>
              </div>
              <div className="mes-steps-move">
                <button className="mes-sm" onClick={() => move(-1)} disabled={selStep === null || !canEdit} title="앞으로">▲</button>
                <button className="mes-sm" onClick={() => move(1)} disabled={selStep === null || !canEdit} title="뒤로">▼</button>
              </div>
            </div>
            <div className="mes-steps-add">
              <select value={addId} onChange={e => setAddId(e.target.value)} disabled={!canEdit}>
                <option value="">-- 추가할 공정 --</option>
                {data.processes.map(p => (
                  <option key={p.processDefinitionId} value={p.processDefinitionId}>{p.operCode} {p.processName}</option>
                ))}
              </select>
              <button className="btn btn-ghost" onClick={addStep} disabled={!addId || !canEdit}>맨 뒤에 추가</button>
            </div>
            {routeMsg && <p className={`mes-alert ${routeError ? 'error' : 'ok'}`}>{routeMsg}</p>}
          </div>
        </div>
      </section>
    </div>
  );
}
