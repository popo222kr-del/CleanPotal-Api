import { useEffect, useMemo, useState } from 'react';
import { api } from '../../api/client';
import type { EqCheckItem, EqCheckMonth, EqCheckUnit } from '../../api/types';
import { timeLabel } from '../checklist/common';
import { cl, md } from './eqCommon';

// 월간 점검표 — 종이 양식(AQ-C-13 Rev.7) 한 설비·한 달. 체크시트(현장) 월간 리포트와 같은 표 모양·글자 크기.
// 날짜 머리에 요일을 적고 토요일은 파랑, 일요일·공휴일은 빨강(주말·공휴일 칸은 옅게 칠한다).

const DOW = ['일', '월', '화', '수', '목', '금', '토'];
const thisMonth = () => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`; };

/** 결과 글자 → 표 칸 모양(체크시트 현장 리포트와 같게 ○ / △ / ✕). */
function cellText(v: string): string {
  if (v === 'O') return '○';
  if (v === 'X') return '✕';
  return v;
}

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
  const holidays = useMemo(() => new Map((data?.holidays ?? []).map(h => [h.date, h.name])), [data]);
  const days = useMemo(() => {
    if (!data) return [];
    const n = new Date(data.year, data.month, 0).getDate();
    return Array.from({ length: n }, (_, i) => {
      const key = `${data.monthKey}-${String(i + 1).padStart(2, '0')}`;
      const dow = new Date(data.year, data.month - 1, i + 1).getDay();
      const hol = holidays.get(key);
      return { key, d: i + 1, dow, hol, cls: hol || dow === 0 ? 'sun' : dow === 6 ? 'sat' : '' };
    });
  }, [data, holidays]);

  const byCycle = (c: string) => (data?.items ?? []).filter(i => i.cycle === c);
  const who = (keys: string[], items: EqCheckItem[]) => keys.map(k =>
    [...new Set(items.map(i => cell.get(`${i.id}|${k}`)?.by).filter(Boolean))].join(', '));
  const note = (c: string, k: string) => data?.notes.find(n => n.cycle === c && n.periodKey === k)?.note ?? '';
  const V = ({ id, k, cls = '' }: { id: number; k: string; cls?: string }) => {
    const v = cell.get(`${id}|${k}`);
    return <td className={`d ${cls}`}><div className={v?.ng ? 'ng' : v?.t === 'O' ? 'ok' : v ? 'val' : ''}>{v ? cellText(v.t) : ''}</div></td>;
  };
  const itemCell = (i: EqCheckItem) => (
    <td className="t"><b>{i.name}</b>{i.point && <span className="ec-pt"> {i.point}</span>}<div className="ck-dim">{i.spec}</div></td>
  );

  return (
    <div>
      <div className="ck-toolbar ck-noprint">
        <select className="ck-input" value={unit} onChange={e => setUnit(e.target.value)}>
          {units.map(u => <option key={u.code} value={u.code}>{u.code} · {u.templateName}</option>)}
        </select>
        <input className="ck-input" type="month" value={ym} onChange={e => e.target.value && setYm(e.target.value)} />
        {data && (
          <div className="ck-toolbar-right">
            <button className="ck-btn-sm" onClick={() => window.print()}>인쇄 (A3 가로)</button>
          </div>
        )}
      </div>
      {err && <div className="ck-error">{err}</div>}
      {!data ? <div className="ck-empty">불러오는 중…</div> : (
        <div className="ck-report ec-report">
          <div className="ck-rtitle">
            <div>
              <h2>{data.year}년 {data.month}월 {data.unit.code} {data.unit.templateName} 설비 점검표</h2>
              <div className="ck-rmeta">
                AQ-C-13(Rev.7) · {data.unit.line}{data.unit.process ? ` · ${data.unit.process}` : ''} · 점검주기 매일 · 주간 금요일 09시 · 월간 매월 첫째 주 금요일 09시
              </div>
            </div>
            <table className="ck-sign"><tbody>
              <tr><th>설비 담당자<br />(관리책임자)</th><th>생산팀<br />(점검자)</th><th>승인</th></tr>
              <tr><td /><td /><td /></tr>
            </tbody></table>
          </div>
          <div className="ck-legend">
            ○ 양호 · △ 점검 · <span className="ng">✕ 불량</span> · 숫자 측정값(<span className="ng">빨강</span>=기준 벗어남)
            · <span className="sat">토</span> · <span className="sun">일·공휴일</span>
            {data.holidays.length > 0 && <> · 공휴일 {data.holidays.map(h => `${Number(h.date.slice(8))}일 ${h.name}`).join(', ')}</>}
          </div>

          <h4 className="ec-rh">매일 점검 <small>생산팀 · 하루 1회</small></h4>
          <div className="ck-rscroll">
            <table className="ck-grid ec-grid">
              <thead>
                <tr>
                  <th className="z">구분</th>
                  <th className="t">점검 항목 · 판정 SPEC</th>
                  {days.map(d => <th key={d.key} className={`d ${d.cls}`} title={d.hol}>{d.d}<br />{DOW[d.dow]}</th>)}
                </tr>
              </thead>
              <tbody>
                {byCycle('일상').map(i => (
                  <tr key={i.id}>
                    <td className="z">{i.category}</td>
                    {itemCell(i)}
                    {days.map(d => <V key={d.key} id={i.id} k={d.key} cls={d.cls} />)}
                  </tr>
                ))}
                <tr className="who">
                  <td className="z" colSpan={2}>점검자</td>
                  {who(days.map(d => d.key), byCycle('일상')).map((w, n) => <td key={n} className={`d ${days[n].cls}`} title={w}>{w ? '✓' : ''}</td>)}
                </tr>
              </tbody>
            </table>
          </div>

          {(['주간', '월간'] as const).map(c => {
            const keys = c === '주간' ? data.weekKeys : [data.monthKey];
            const items = byCycle(c);
            if (items.length === 0) return null;
            return (
              <div key={c}>
                <h4 className="ec-rh">{cl(c)} 점검 <small>{c === '주간' ? '생산팀 · 금요일 09시' : `설비팀 · 기한 ${md(data.monthDue)} (첫째 주 금요일)`}</small></h4>
                <div className="ck-rscroll">
                  <table className="ck-grid ec-grid">
                    <thead>
                      <tr>
                        <th className="z">구분</th>
                        <th className="t">점검 항목 · 판정 SPEC</th>
                        {keys.map(k => <th key={k} className="p">{c === '주간' ? `${md(k)} (금)` : `${data.month}월`}</th>)}
                      </tr>
                    </thead>
                    <tbody>
                      {items.map(i => (
                        <tr key={i.id}>
                          <td className="z">{i.category}</td>
                          {itemCell(i)}
                          {keys.map(k => <V key={k} id={i.id} k={k} cls="p" />)}
                        </tr>
                      ))}
                      <tr className="who"><td className="z" colSpan={2}>점검자</td>{who(keys, items).map((w, n) => <td key={n} className="p">{w}</td>)}</tr>
                      {keys.some(k => note(c, k)) && (
                        <tr className="who"><td className="z" colSpan={2}>특이사항</td>{keys.map(k => <td key={k} className="p note">{note(c, k)}</td>)}</tr>
                      )}
                    </tbody>
                  </table>
                </div>
              </div>
            );
          })}

          <h4 className="ec-rh">점검 부적합 및 고장 조치 내용</h4>
          <table className="ck-rng">
            <thead><tr><th>발생 일자</th><th>내용</th><th>조치 내용 및 결과</th><th>완료 일자</th><th>확인자</th></tr></thead>
            <tbody>
              {data.faults.length === 0 && <tr><td colSpan={5} className="ck-dim">없음</td></tr>}
              {data.faults.map(f => (
                <tr key={f.id}>
                  <td className="nowrap">{timeLabel(f.checkedAt)}</td>
                  <td>[{cl(f.cycle)}] {f.name}{f.point ? ` ${f.point}` : ''}{f.valueText ? ` — ${f.valueText}` : ''}{f.memo ? ` (${f.memo})` : ''}</td>
                  <td>{f.ngStatus === 'DONE' ? f.ngCloseNote : <span className="ng">미조치</span>}</td>
                  <td className="nowrap">{f.ngClosedAt ? timeLabel(f.ngClosedAt) : ''}</td>
                  <td>{f.ngClosedBy}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
