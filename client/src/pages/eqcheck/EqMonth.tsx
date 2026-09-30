import { useEffect, useMemo, useState } from 'react';
import { api } from '../../api/client';
import type { EqCheckItem, EqCheckMonth, EqCheckUnit } from '../../api/types';
import { timeLabel } from '../checklist/common';
import { CYCLES, md } from './eqCommon';

// 월간 점검표 — 종이 양식(AQ-C-13 Rev.7) 모양으로 한 설비·한 달. 인쇄해서 결재·보관할 수 있다.

const thisMonth = () => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`; };

export default function EqMonth({ initialUnit }: { initialUnit: string }) {
  const [units, setUnits] = useState<EqCheckUnit[]>([]);
  const [unit, setUnit] = useState(initialUnit);
  const [ym, setYm] = useState(thisMonth());
  const [data, setData] = useState<EqCheckMonth | null>(null);
  const [err, setErr] = useState('');

  useEffect(() => {
    api.get<EqCheckUnit[]>('/api/eqcheck/units').then(u => {
      const act = u.filter(x => x.isActive);
      setUnits(act);
      setUnit(cur => cur || act[0]?.code || '');
    }).catch(() => {});
  }, []);

  useEffect(() => {
    if (!unit) return;
    let alive = true;
    const [y, m] = ym.split('-').map(Number);
    setData(null);
    api.get<EqCheckMonth>(`/api/eqcheck/month/${encodeURIComponent(unit)}?year=${y}&month=${m}`)
      .then(d => { if (alive) { setData(d); setErr(''); } })
      .catch(e => { if (alive) setErr(e instanceof Error ? e.message : '불러오지 못했습니다.'); });
    return () => { alive = false; };
  }, [unit, ym]);

  const cell = useMemo(() => {
    const m = new Map<string, { t: string; ng: boolean; by: string }>();
    for (const c of data?.cells ?? []) m.set(`${c.itemId}|${c.periodKey}`, { t: c.valueText, ng: c.judge === 'NG', by: c.by });
    return m;
  }, [data]);
  const days = useMemo(() => {
    if (!data) return [];
    const n = new Date(data.year, data.month, 0).getDate();
    return Array.from({ length: n }, (_, i) => `${data.monthKey}-${String(i + 1).padStart(2, '0')}`);
  }, [data]);
  const who = (keys: string[], items: EqCheckItem[]) => keys.map(k => {
    const names = new Set(items.map(i => cell.get(`${i.id}|${k}`)?.by).filter(Boolean));
    return [...names].join(', ');
  });

  const byCycle = (c: string) => (data?.items ?? []).filter(i => i.cycle === c);
  const label = (i: EqCheckItem) => <><b>{i.name}</b>{i.point && <em> {i.point}</em>}</>;
  const V = ({ id, k }: { id: number; k: string }) => {
    const v = cell.get(`${id}|${k}`);
    return <td className={`ec-mv ${v?.ng ? 'ng' : ''}`}>{v?.t ?? ''}</td>;
  };
  const note = (c: string, k: string) => data?.notes.find(n => n.cycle === c && n.periodKey === k);

  return (
    <>
      <div className="ck-bar-row ck-noprint">
        <select className="input ck-sel" value={unit} onChange={e => setUnit(e.target.value)}>
          {units.map(u => <option key={u.code} value={u.code}>{u.code} · {u.templateName}</option>)}
        </select>
        <input className="input ck-date" type="month" value={ym} onChange={e => e.target.value && setYm(e.target.value)} />
        <button className="btn btn-ghost ck-sm" onClick={() => window.print()}>인쇄</button>
      </div>
      {err && <div className="ck-error">{err}</div>}
      {!data ? <div className="ck-empty">불러오는 중…</div> : (
        <div className="ec-sheet">
          <div className="ec-sheet-head">
            <div><span>설비 호기</span><b>{data.unit.code}</b></div>
            <div className="t"><b>{data.unit.templateName}</b> ({data.year}년 {data.month}월 설비 점검표)
              <small>점검주기 주간 : 금요일 09시, 월간 : 매월 첫주 금요일 09시 · AQ-C-13(Rev.7)</small></div>
            <div className="sign"><span>설비 담당자<br />(관리책임자)</span><span>생산팀<br />(점검자)</span><span>승 인</span></div>
          </div>

          <h4>일상 점검 <small>양호 O / 점검 △ / 불량 X</small></h4>
          <div className="ec-tw">
            <table className="ec-mtable daily">
              <thead><tr><th>점검항목</th><th>판정 SPEC</th>{days.map(d => <th key={d}>{Number(d.slice(8))}</th>)}</tr></thead>
              <tbody>
                {byCycle('일상').map(i => (
                  <tr key={i.id}><td>{label(i)}<div className="ck-sub">{i.category}</div></td><td className="spec">{i.spec}</td>
                    {days.map(d => <V key={d} id={i.id} k={d} />)}</tr>
                ))}
                <tr className="who"><td colSpan={2}>점검자</td>{who(days, byCycle('일상')).map((w, n) => <td key={n} title={w}>{w ? '✓' : ''}</td>)}</tr>
              </tbody>
            </table>
          </div>

          {CYCLES.slice(1).map(c => {
            const keys = c === '주간' ? data.weekKeys : [data.monthKey];
            const items = byCycle(c);
            if (items.length === 0) return null;
            return (
              <div key={c}>
                <h4>{c} 점검 <small>{c === '주간' ? '금요일 09시' : `기한 ${md(data.monthDue)} (첫째 주 금요일)`}</small></h4>
                <div className="ec-tw">
                  <table className="ec-mtable">
                    <thead><tr><th>구분</th><th>점검항목</th><th>판정 SPEC</th>
                      {keys.map(k => <th key={k}>{c === '주간' ? `${md(k)} (금)` : `${data.month}월`}</th>)}</tr></thead>
                    <tbody>
                      {items.map(i => (
                        <tr key={i.id}><td className="ck-dim">{i.category}</td><td>{label(i)}</td><td className="spec">{i.spec}</td>
                          {keys.map(k => <V key={k} id={i.id} k={k} />)}</tr>
                      ))}
                      <tr className="who"><td colSpan={3}>점검자</td>{who(keys, items).map((w, n) => <td key={n}>{w}</td>)}</tr>
                      {keys.some(k => note(c, k)) && (
                        <tr className="who"><td colSpan={3}>특이사항</td>{keys.map(k => <td key={k} className="ec-note-cell">{note(c, k)?.note ?? ''}</td>)}</tr>
                      )}
                    </tbody>
                  </table>
                </div>
              </div>
            );
          })}

          <h4>점검 부적합 및 고장 조치 내용</h4>
          <table className="ec-mtable faults">
            <thead><tr><th>발생 일자</th><th>내용</th><th>조치 내용 및 결과</th><th>완료 일자</th><th>확인자</th></tr></thead>
            <tbody>
              {data.faults.length === 0 && <tr><td colSpan={5} className="ck-dim">없음</td></tr>}
              {data.faults.map(f => (
                <tr key={f.id}>
                  <td className="nowrap">{timeLabel(f.checkedAt)}</td>
                  <td>[{f.cycle}] {f.name}{f.point ? ` ${f.point}` : ''}{f.valueText ? ` — ${f.valueText}` : ''}{f.memo ? ` (${f.memo})` : ''}</td>
                  <td>{f.ngStatus === 'DONE' ? f.ngCloseNote : <span className="ck-pill bad">미조치</span>}</td>
                  <td className="nowrap">{f.ngClosedAt ? timeLabel(f.ngClosedAt) : ''}</td>
                  <td>{f.ngClosedBy}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}
