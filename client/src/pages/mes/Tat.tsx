import { useCallback, useEffect, useState } from 'react';
import { api } from '../../api/client';
import { dateOnly, hours } from './lot';
import { useOpenLotHistory } from './shell/useOpenLot';
import './Mes.css';

// MES "TAT 조회" — 기간 안에 출하까지 끝난 LOT 의 입고→출하 소요 시간.

type TatItem = {
  lotId: number; lotNumber: string; customerName: string; productName: string;
  serialNumber: string; receivedDate: string; shippedAt: string; tatHours: number;
};
type TatReport = {
  avgHours: number | null; maxHours: number | null; minHours: number | null;
  count: number; items: TatItem[];
};

/** input[type=date] 가 쓰는 'YYYY-MM-DD' */
function isoDay(d: Date) {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

export default function MesTat() {
  const openLot = useOpenLotHistory();
  // 처음 보여줄 기간(최근 30일)은 화면에 들어온 시점에 한 번만 정한다.
  // 모듈 상수로 두면 탭을 며칠 켜 둔 경우 "오늘"이 옛날 날짜로 굳는다.
  const [initial] = useState(() => {
    const t = new Date();
    return { from: isoDay(new Date(t.getTime() - 30 * 86400_000)), to: isoDay(t) };
  });
  const [from, setFrom] = useState(initial.from);
  const [to, setTo] = useState(initial.to);
  const [report, setReport] = useState<TatReport | null>(null);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const load = useCallback(async (f: string, t: string) => {
    setBusy(true);
    try {
      setReport(await api.get<TatReport>(`/api/mes/lot/tat?from=${f}&to=${t}`));
      setErr(null);
    } catch {
      setReport(null);
      setErr('TAT 를 불러오는 중 문제가 발생했습니다.');
    } finally {
      setBusy(false);
    }
  }, []);

  // 처음 들어오면 최근 30일을 바로 보여준다 — 매번 기간부터 고르게 하지 않는다.
  // 이후 조회는 [조회] 버튼이 하므로, 기간을 고치는 동안 자동으로 다시 부르지 않는다.
  useEffect(() => { void load(initial.from, initial.to); }, [load, initial]);

  return (
    <div className="mes-page">
      <header className="pg-header">
        <div><h2>TAT 조회</h2></div>
        <input className="input" type="date" value={from} onChange={e => setFrom(e.target.value)} />
        <span className="mes-dim">~</span>
        <input className="input" type="date" value={to} onChange={e => setTo(e.target.value)} />
        <button className="btn btn-primary" onClick={() => { void load(from, to); }} disabled={busy}>조회</button>
      </header>

      <div className="pg-body">
        {err && <p className="mes-alert error">{err}</p>}
        {!report && busy && <p className="mes-dim">조회 중…</p>}

        {report && (
          <>
            <div className="mes-cards">
              <div className="mes-card flat"><span className="mes-card-label">건수</span><span className="mes-card-value blue">{report.count}</span></div>
              <div className="mes-card flat"><span className="mes-card-label">평균 TAT</span><span className="mes-card-value">{hours(report.avgHours)}</span></div>
              <div className="mes-card flat"><span className="mes-card-label">최소</span><span className="mes-card-value">{hours(report.minHours)}</span></div>
              <div className="mes-card flat"><span className="mes-card-label">최대</span><span className="mes-card-value red">{hours(report.maxHours)}</span></div>
            </div>

            <div className="mes-title">출하 완료 LOT<span className="mes-count">{report.items.length}건</span></div>
            <div className="mes-scroll tall">
              <table className="mes-table">
                <thead>
                  <tr>
                    <th>LOT 번호</th><th>업체</th><th>제품명</th><th>S/N</th>
                    <th>입고일</th><th>출하일</th><th className="num">TAT</th>
                  </tr>
                </thead>
                <tbody>
                  {report.items.map(i => (
                    <tr key={i.lotId}>
                      {/* LOT 번호를 누르면 그 LOT 의 현황으로 간다(원본과 같다) — TAT 가 길게 나온 LOT 이
                          어디서 멈춰 있었는지 보려면 이력을 봐야 한다. */}
                      <td>
                        <button className="mes-link"
                                onClick={() => openLot(i.lotNumber)}>
                          {i.lotNumber}
                        </button>
                      </td>
                      <td>{i.customerName}</td>
                      <td>{i.productName}</td>
                      <td>{i.serialNumber}</td>
                      <td>{dateOnly(i.receivedDate)}</td>
                      <td>{dateOnly(i.shippedAt)}</td>
                      <td className="num">{hours(i.tatHours)}</td>
                    </tr>
                  ))}
                  {report.items.length === 0 && (
                    <tr><td colSpan={7} className="mes-empty">이 기간에 출하 완료된 LOT 이 없습니다.</td></tr>
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
