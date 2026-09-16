import { Fragment, useCallback, useEffect, useState } from 'react';
import { api } from '../../api/client';
import { dateTime, hours, statusLabel, statusTone } from './lot';
import './Mes.css';

// MES 입·출고 현황 조회.
// 위는 재공현황 매트릭스(제품 × 공정단계), 아래는 LOT 목록. 매트릭스 칸을 누르면 아래가 그 조건으로 좁혀진다.
//
// 어떤 칸이 어떤 조건인지는 화면이 해석하지 않는다. 서버가 준 칸 객체를 그대로 되돌려 보내고
// 서버가 다시 걸러 준다 — 집계 규칙이 두 곳에 생기면 매트릭스 숫자와 목록 건수가 어긋난다.

type DrillTarget = { allProducts: boolean; cleaningCode: string | null; stageOperCode: number | null; label: string };
type StageCell = { count: number; target: DrillTarget | null; toolTip: string | null; emphasize: boolean; isSummary: boolean };
type InOutCell = { isInbound: boolean; allProducts: boolean; cleaningCode: string | null; customerName: string | null; count: number; label: string };
type MatrixRow = {
  isTotal: boolean; rowNo: number; cleaningCode: string | null;
  matGroup: string; matId: string; matDesc: string; rowTarget: DrillTarget;
  stageCells: StageCell[]; inboundCells: InOutCell[]; outboundCells: InOutCell[];
};
type Lot = {
  lotId: number; lotNumber: string; line: string | null; pmEquipmentName: string | null;
  exportNumber: string | null; cleaningCode: string | null; productName: string; serialNumber: string;
  itemCode: string; isBatch: boolean; currentStatus: number; recipeCode: string | null;
  equipmentId: string | null; receivedDate: string; stageArrivedAt: string; tatHours: number | null;
  processLabel: string | null; worker: string | null; comment: string | null;
};
type SearchResult = {
  customerOptions: string[]; stageTitles: string[]; customers: string[];
  rows: MatrixRow[]; lots: Lot[]; inOutVisible: boolean;
};

const isoDay = (d: Date) => {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
};
const monthAgo = () => { const d = new Date(); d.setMonth(d.getMonth() - 1); return d; };

const HINT = '조회하면 현 공정의 전체 재공현황이 표시됩니다. 업체명 / 세정코드 / S/N 을 입력하면 해당 조건으로 검색됩니다.';

export default function MesLotInOut() {
  const [defaults] = useState(() => ({ from: isoDay(monthAgo()), to: isoDay(new Date()) }));
  const [dateFrom, setDateFrom] = useState(defaults.from);
  const [dateTo, setDateTo] = useState(defaults.to);
  const [customer, setCustomer] = useState('');
  const [cleaningCode, setCleaningCode] = useState('');
  const [serialNumber, setSerialNumber] = useState('');

  const [options, setOptions] = useState<{ customerOptions: string[]; stageTitles: string[] }>(
    { customerOptions: [], stageTitles: [] });
  const [result, setResult] = useState<SearchResult | null>(null);
  const [lots, setLots] = useState<Lot[]>([]);
  const [picked, setPicked] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(HINT);
  const [isError, setIsError] = useState(false);

  // 들어오면 MES 와 같이 자료는 띄우지 않고 업체 목록만 채운다.
  useEffect(() => {
    void (async () => {
      try { setOptions(await api.get<typeof options>('/api/mes/lot-inout/options')); }
      catch { setIsError(true); setMsg('화면을 불러오지 못했습니다.'); }
    })();
    // options 는 이 효과가 채우는 대상이라 의존성에 넣지 않는다(넣으면 서로를 다시 부른다).
  }, []);

  const query = useCallback(() => {
    const q = new URLSearchParams();
    if (dateFrom) q.set('dateFrom', dateFrom);
    if (dateTo) q.set('dateTo', dateTo);
    if (customer) q.set('customer', customer);
    if (cleaningCode.trim()) q.set('cleaningCode', cleaningCode.trim());
    if (serialNumber.trim()) q.set('serialNumber', serialNumber.trim());
    return q;
  }, [dateFrom, dateTo, customer, cleaningCode, serialNumber]);

  const filterBody = useCallback(() => ({
    customer: customer || null,
    cleaningCode: cleaningCode.trim() || null,
    serialNumber: serialNumber.trim() || null,
    dateFrom: dateFrom || null,
    dateTo: dateTo || null,
  }), [customer, cleaningCode, serialNumber, dateFrom, dateTo]);

  async function search() {
    if (busy) return;
    setBusy(true); setMsg(null); setPicked(null);
    try {
      const r = await api.get<SearchResult>(`/api/mes/lot-inout?${query()}`);
      setResult(r);
      setLots(r.lots);
      setOptions({ customerOptions: r.customerOptions, stageTitles: r.stageTitles });
      setIsError(false);
    } catch {
      setIsError(true); setMsg('조회 중 문제가 발생했습니다.');
    } finally { setBusy(false); }
  }

  function reset() {
    setCustomer(''); setCleaningCode(''); setSerialNumber('');
    setDateFrom(isoDay(monthAgo())); setDateTo(isoDay(new Date()));
    setResult(null); setLots([]); setPicked(null);
    setIsError(false); setMsg(HINT);
  }

  async function drill(path: string, extra: Record<string, unknown>) {
    if (busy) return;
    setBusy(true);
    try {
      const r = await api.post<{ lots: Lot[]; message: string }>(path, { ...filterBody(), ...extra });
      setLots(r.lots); setMsg(r.message); setIsError(false); setPicked(null);
    } catch {
      setIsError(true); setMsg('목록을 좁히는 중 문제가 발생했습니다.');
    } finally { setBusy(false); }
  }

  const stageTitles = result?.stageTitles ?? options.stageTitles;
  const customers = result?.customers ?? [];
  const showInOut = (result?.inOutVisible ?? false) && customers.length > 0;

  return (
    <div className="mes-page">
      <header className="pg-header"><div><h2>입 · 출고 현황 조회</h2></div></header>

      <div className="pg-body">
        <div className="mes-filters">
          <label><span>날짜</span>
            <span className="mes-daterange">
              <input type="date" className="input" value={dateFrom} onChange={e => setDateFrom(e.target.value)} />
              <span className="mes-dim">~</span>
              <input type="date" className="input" value={dateTo} onChange={e => setDateTo(e.target.value)} />
            </span>
          </label>
          <label><span>업체명</span>
            <select value={customer} onChange={e => setCustomer(e.target.value)}
                    title="업체명을 고르면 입고·출고 현황이 함께 표시됩니다">
              <option value="">(전체)</option>
              {options.customerOptions.map(c => <option key={c} value={c}>{c}</option>)}
            </select>
          </label>
          <label><span>세정코드</span>
            <input className="input" value={cleaningCode} onChange={e => setCleaningCode(e.target.value)} />
          </label>
          <label><span>S/N</span>
            <input className="input" value={serialNumber} onChange={e => setSerialNumber(e.target.value)} />
          </label>
          <span className="mes-filters-actions">
            <button className="btn btn-ghost" onClick={reset} disabled={busy}>초기화</button>
            <button className="btn btn-primary" onClick={() => void search()} disabled={busy}>조회</button>
          </span>
        </div>

        <div className="mes-scroll mes-matrix-scroll">
          {!result ? <p className="mes-empty mes-pre">{HINT}</p> : (
            <table className="mes-table mes-matrix">
              <thead>
                <tr>
                  <th rowSpan={2}>MAT GROUP</th>
                  <th rowSpan={2}>MAT DESC</th>
                  <th rowSpan={2}>MAT ID</th>
                  <th colSpan={stageTitles.length}>재공현황</th>
                  {showInOut && <th colSpan={customers.length + 1}>입고현황(업체별)</th>}
                  {showInOut && <th colSpan={customers.length + 1}>출고현황(업체별)</th>}
                </tr>
                <tr>
                  {stageTitles.map(t => <th key={t}>{t}</th>)}
                  {showInOut && [0, 1].map(band => (
                    <Fragment key={band}>
                      {customers.map(c => <th key={c} title={c}>{c}</th>)}
                      <th>합계</th>
                    </Fragment>
                  ))}
                </tr>
              </thead>
              <tbody>
                {result.rows.map(row => (
                  <tr key={row.rowNo} className={row.isTotal ? 'total' : ''}>
                    <td>{row.matGroup}</td>
                    <td className="click" title="이 제품의 전체 LOT 보기"
                        onClick={() => void drill('/api/mes/lot-inout/drill', { target: row.rowTarget })}>{row.matDesc}</td>
                    <td className="click"
                        onClick={() => void drill('/api/mes/lot-inout/drill', { target: row.rowTarget })}>{row.matId}</td>
                    {row.stageCells.map((cell, i) => (
                      <td key={`s${i}`}
                          className={[cell.target ? 'click' : '', cell.emphasize ? 'warn' : '', cell.isSummary ? 'sum' : ''].join(' ').trim()}
                          title={cell.toolTip ?? undefined}
                          onClick={() => cell.target && void drill('/api/mes/lot-inout/drill', { target: cell.target })}>
                        {cell.count === 0 ? '' : cell.count}
                      </td>
                    ))}
                    {showInOut && [...row.inboundCells, ...row.outboundCells].map((cell, i) => (
                      <td key={`io${i}`} className="click"
                          onClick={() => void drill('/api/mes/lot-inout/drill-inout', { cell })}>
                        {cell.count === 0 ? '' : cell.count}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>

        <div className="mes-scroll" style={{ marginTop: 10 }}>
          <table className="mes-table">
            <thead>
              <tr>
                <th>LINE</th><th>업체명</th><th>반출번호</th><th>세정코드</th><th>제품명</th><th>S/N</th>
                <th>품목코드</th><th>BATCH</th><th>STATUS</th><th>RECIPE</th><th>설비명</th>
                <th>AETS 입고</th><th>공정 입고</th><th className="num">TAT</th><th>PROCESS</th><th>작업자</th><th>Comment</th>
              </tr>
            </thead>
            <tbody>
              {lots.map(l => (
                <tr key={l.lotId} className={l.lotId === picked ? 'picked' : ''}
                    onClick={() => setPicked(l.lotId)} style={{ cursor: 'pointer' }}>
                  <td>{l.line ?? ''}</td>
                  <td>{l.pmEquipmentName ?? ''}</td>
                  <td>{l.exportNumber ?? ''}</td>
                  <td>{l.cleaningCode ?? ''}</td>
                  <td>{l.productName}</td>
                  <td>{l.serialNumber}</td>
                  <td>{l.itemCode}</td>
                  <td>{l.isBatch ? 'Y' : ''}</td>
                  <td><span className={`mes-badge ${statusTone(l.currentStatus)}`}>{statusLabel(l.currentStatus)}</span></td>
                  <td>{l.recipeCode ?? ''}</td>
                  <td>{l.equipmentId ?? ''}</td>
                  <td>{dateTime(l.receivedDate)}</td>
                  <td>{dateTime(l.stageArrivedAt)}</td>
                  <td className="num">{hours(l.tatHours)}</td>
                  <td>{l.processLabel ?? ''}</td>
                  <td>{l.worker ?? ''}</td>
                  <td className="mes-clip" title={l.comment ?? ''}>{l.comment ?? ''}</td>
                </tr>
              ))}
              {lots.length === 0 && (
                <tr><td colSpan={17} className="mes-empty">{busy ? '조회 중…' : '조회하면 목록이 표시됩니다.'}</td></tr>
              )}
            </tbody>
          </table>
        </div>

        <p className={`mes-status ${msg ? (isError ? 'bad' : 'good') : ''}`}>총 {lots.length}건 {msg ? `· ${msg}` : ''}</p>
      </div>
    </div>
  );
}
