import { useCallback, useEffect, useRef, useState } from 'react';
import { api } from '../../api/client';
import { dateTime, elapsed, hours, statusLabel, statusTone, type LotCategory, type OperLot } from './lot';
import { useOpenLotHistory } from './shell/useOpenLot';
import './Mes.css';

// MES Dash Board. MES 를 누르면 처음 보이는 화면이라 가장 먼저 옮겼다.
// 카드를 누르면 아래에 그 분류의 제품 목록이 펼쳐지는 구조는 MES 와 같다.

type Summary = {
  todayReceived: number; inProgress: number; hold: number; rework: number;
  shippingWaiting: number; longWait: number; avgTatHours: number | null;
  completedCount: number; todayShipped: number;
};
type Stage = {
  processDefinitionId: number; processName: string; operCode: number;
  count: number; avgWaitHours: number | null; isBottleneck: boolean;
};
type DashboardData = { summary: Summary; stages: Stage[]; shippedDoneOperCode: number };

type Kpi = { label: string; value: string; tone: string; category: LotCategory | null };

const REFRESH_MS = 60_000;

export default function MesDashboard() {
  const openLot = useOpenLotHistory();
  const [data, setData] = useState<DashboardData | null>(null);
  const [lots, setLots] = useState<OperLot[]>([]);
  const [pickedTitle, setPickedTitle] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const [loadedAt, setLoadedAt] = useState<Date | null>(null);

  // 자동 갱신이 돌 때도 "지금 펼쳐 둔 목록"을 유지해야 해서 선택 상태를 ref 로도 들고 있는다.
  const picked = useRef<{ category: LotCategory; operCode: number | null } | null>(null);

  const loadLots = useCallback(async (category: LotCategory, operCode: number | null) => {
    const q = new URLSearchParams({ category });
    if (operCode !== null) q.set('operCode', String(operCode));
    return api.get<OperLot[]>(`/api/mes/dashboard/lots?${q}`);
  }, []);

  const load = useCallback(async () => {
    setBusy(true);
    try {
      const d = await api.get<DashboardData>('/api/mes/dashboard');
      setData(d);
      if (picked.current) setLots(await loadLots(picked.current.category, picked.current.operCode));
      setLoadedAt(new Date());
      setErr(null);
    } catch {
      setErr('대시보드를 불러오는 중 문제가 발생했습니다.');
    } finally {
      setBusy(false);
    }
  }, [loadLots]);

  useEffect(() => {
    void load();
    const t = setInterval(() => { void load(); }, REFRESH_MS);
    return () => clearInterval(t);
  }, [load]);

  async function pick(category: LotCategory, operCode: number | null, title: string) {
    picked.current = { category, operCode };
    setPickedTitle(title);
    setBusy(true);
    try {
      setLots(await loadLots(category, operCode));
      setErr(null);
    } catch {
      setLots([]);
      setErr('제품 목록을 불러오는 중 문제가 발생했습니다.');
    } finally {
      setBusy(false);
    }
  }

  const s = data?.summary;
  const kpis: Kpi[] = s ? [
    { label: '오늘 입고', value: String(s.todayReceived), tone: 'blue', category: 'TodayReceived' },
    { label: '공정 진행', value: String(s.inProgress), tone: 'blue', category: 'InProgress' },
    { label: 'HOLD', value: String(s.hold), tone: 'red', category: 'Hold' },
    { label: '재작업', value: String(s.rework), tone: 'amber', category: 'Rework' },
    { label: '출하대기', value: String(s.shippingWaiting), tone: 'cyan', category: 'ShippingWaiting' },
    { label: '장기대기', value: String(s.longWait), tone: 'red', category: 'LongWait' },
    { label: '평균 TAT (최근 30일)', value: hours(s.avgTatHours), tone: '', category: null },
  ] : [];

  const isPicked = (category: LotCategory, operCode: number | null) =>
    picked.current?.category === category && (picked.current?.operCode ?? null) === operCode;

  return (
    <div className="mes-page">
      <header className="pg-header">
        <div><h2>MES Dash Board</h2></div>
        <span className="mes-stamp">
          {loadedAt ? `기준 ${loadedAt.toLocaleTimeString('ko-KR', { hour12: false })} · 60초마다 자동 갱신` : ''}
        </span>
        <button className="btn btn-ghost" onClick={() => { void load(); }} disabled={busy}>새로고침</button>
      </header>

      <div className="pg-body">
        {err && <p className="mes-alert error">{err}</p>}
        {!data && !err && <p className="mes-dim">불러오는 중…</p>}

        {data && (
          <>
            <div className="mes-title">오늘의 KPI</div>
            <div className="mes-cards">
              {kpis.map(k => (
                <button
                  key={k.label}
                  type="button"
                  className={`mes-card ${k.category ? 'clickable' : 'flat'} ${k.category && isPicked(k.category, null) ? 'picked' : ''}`}
                  disabled={!k.category}
                  onClick={() => k.category && pick(k.category, null, k.label)}
                >
                  <span className="mes-card-label">{k.label}</span>
                  <span className={`mes-card-value ${k.tone}`}>{k.value}</span>
                </button>
              ))}
            </div>

            <div className="mes-title">공정별 WIP</div>
            <div className="mes-cards">
              {data.stages.map(st => {
                const shipped = st.operCode === data.shippedDoneOperCode;
                const category: LotCategory = shipped ? 'TodayShipped' : 'WipStage';
                const operCode = shipped ? null : st.operCode;
                const title = shipped ? '출하 완료 (당일)' : `${st.operCode} ${st.processName}`;
                return (
                  <button
                    key={`${st.operCode}-${st.processDefinitionId}`}
                    type="button"
                    className={`mes-card clickable ${st.isBottleneck ? 'bottleneck' : ''} ${isPicked(category, operCode) ? 'picked' : ''}`}
                    onClick={() => pick(category, operCode, title)}
                  >
                    <span className="mes-card-label" title={st.processName}>{st.processName}</span>
                    <span className="mes-card-value blue">{st.count}</span>
                    <span className="mes-card-sub">평균 체류 {hours(st.avgWaitHours)}</span>
                    {st.isBottleneck && <span className="mes-badge hold">병목</span>}
                  </button>
                );
              })}
            </div>

            <div className="mes-title">
              제품 목록
              <span className="mes-title-sub">
                {pickedTitle ? ` — ${pickedTitle}` : '  (위 KPI · 공정 카드를 클릭하세요)'}
              </span>
              {pickedTitle && <span className="mes-count">{lots.length}건</span>}
            </div>
            <div className="mes-scroll tall">
              <table className="mes-table">
                <thead>
                  <tr>
                    <th>LINE</th><th>업체명</th><th>반출번호</th><th>세정코드</th><th>제품명</th><th>S/N</th>
                    <th>분임조</th><th>PROCESS</th><th>STATUS</th><th>OPER</th>
                    <th>AETS 입고</th><th>공정 입고</th><th className="num">경과</th><th>비고</th><th></th>
                  </tr>
                </thead>
                <tbody>
                  {lots.map(l => (
                    <tr key={l.lotId} onDoubleClick={() => openLot(l.lotNumber)}>
                      <td>{l.line ?? ''}</td>
                      <td>{l.pmEquipmentName ?? ''}</td>
                      <td>{l.exportNumber ?? ''}</td>
                      <td>{l.cleaningCode ?? ''}</td>
                      <td>{l.productName}</td>
                      <td>{l.serialNumber}</td>
                      <td>{l.teamName ?? ''}</td>
                      <td>{l.processLabel ?? ''}</td>
                      <td><span className={`mes-badge ${statusTone(l.currentStatus)}`}>{statusLabel(l.currentStatus)}</span></td>
                      <td>{l.currentProcessName ?? ''}</td>
                      <td>{dateTime(l.receivedDate)}</td>
                      <td>{dateTime(l.stageArrivedAt)}</td>
                      <td className="num">{elapsed(l.stageArrivedAt)}</td>
                      <td className="mes-clip" title={l.comment ?? ''}>{l.comment ?? ''}</td>
                      <td>
                        <button className="mes-sm" onClick={() => openLot(l.lotNumber)}>현황</button>
                      </td>
                    </tr>
                  ))}
                  {lots.length === 0 && (
                    <tr><td colSpan={15} className="mes-empty">
                      {pickedTitle ? '해당하는 제품이 없습니다.' : '위 카드를 클릭하면 목록이 나옵니다.'}
                    </td></tr>
                  )}
                </tbody>
              </table>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
