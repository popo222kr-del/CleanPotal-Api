import { useEffect, useMemo, useState } from 'react';
import type { IcpmsMeasurement } from '../api/types';
import {
  REPORT_ELS, blocksToClipboard, buildBlock, copyRich, eqCmp, f3, lineOf, shortDate,
  type Line, type ReportBlock,
} from './icpmsCopy';

// 보고서 복사 — 라인(METAL / N-METAL)·설비·날짜를 골라, 엑셀 ICP-MS 보고서의 "5. ICP-MS 측정 결과" 칸을
// 그대로 만들어 복사한다. 엑셀의 설비 시트에 붙여넣으면 병합·테두리·숫자 형식까지 들어간다.
// 기간을 고르면 날짜마다 블록을 빈 줄 하나씩 띄워 이어 붙인다(엑셀 시트와 같은 간격).

const pad = (n: number) => String(n).padStart(2, '0');
const ymd = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
function daysBetween(from: string, to: string): string[] {
  const out: string[] = [];
  const d = new Date(from + 'T00:00:00'); const end = new Date(to + 'T00:00:00');
  while (d <= end && out.length < 93) { out.push(ymd(d)); d.setDate(d.getDate() + 1); }
  return out;
}

export default function IcpmsReport({ rows }: { rows: IcpmsMeasurement[] }) {
  const [line, setLine] = useState<Line>('METAL');
  const latest = useMemo(() => rows.map(r => r.analysisDate).filter(Boolean).sort().pop() ?? ymd(new Date()), [rows]);
  const [from, setFrom] = useState(latest);
  const [to, setTo] = useState(latest);
  const [sel, setSel] = useState<string[] | null>(null);   // null = 그 라인 전체
  const [withEmpty, setWithEmpty] = useState(true);
  const [msg, setMsg] = useState('');
  useEffect(() => { setFrom(latest); setTo(latest); }, [latest]);

  const eqs = useMemo(() => [...new Set(rows.map(r => r.eqId.trim()).filter(e => e && lineOf(e) === line))].sort(eqCmp), [rows, line]);
  const chosen = sel ?? eqs;
  const days = useMemo(() => (from && to && from <= to ? daysBetween(from, to) : []), [from, to]);

  // 설비 → 날짜 → 블록
  const groups = useMemo(() => {
    const idx = new Map<string, IcpmsMeasurement[]>();
    for (const r of rows) {
      const k = `${r.eqId.trim()}|${r.analysisDate}`;
      (idx.get(k) ?? idx.set(k, []).get(k)!).push(r);
    }
    return chosen.map(eq => ({
      eq,
      blocks: days.map(d => buildBlock(eq, d, idx.get(`${eq}|${d}`) ?? [])).filter(b => withEmpty || b.measured),
    }));
  }, [rows, chosen, days, withEmpty]);
  const allBlocks = groups.flatMap(g => g.blocks);

  async function copy(blocks: ReportBlock[], what: string) {
    if (blocks.length === 0) return;
    const { html, text } = blocksToClipboard(blocks);
    const ok = await copyRich(html, text);
    setMsg(ok ? `${what} 복사했습니다 — 엑셀에서 붙여넣기(Ctrl+V) 하세요.` : '복사하지 못했습니다. 브라우저가 복사를 막았습니다.');
    window.setTimeout(() => setMsg(''), 4000);
  }
  function toggleEq(eq: string) {
    const cur = sel ?? eqs;
    const next = cur.includes(eq) ? cur.filter(x => x !== eq) : [...cur, eq].sort(eqCmp);
    setSel(next.length === eqs.length ? null : next);
  }

  return (
    <div className="icp-report">
      <div className="icp-rbar">
        <div className="icp-modes">
          {(['METAL', 'N-METAL'] as Line[]).map(l => (
            <button key={l} className={`icp-mode ${line === l ? 'on' : ''}`} onClick={() => { setLine(l); setSel(null); }}>{l}</button>
          ))}
        </div>
        <span className="icp-flt-lbl">날짜</span>
        <input type="date" className="icp-sel" value={from} onChange={e => { setFrom(e.target.value); if (e.target.value > to) setTo(e.target.value); }} />
        <span className="icp-dim">~</span>
        <input type="date" className="icp-sel" value={to} min={from} onChange={e => setTo(e.target.value)} />
        {days.length > 1 && (
          <label className="icp-rchk"><input type="checkbox" checked={withEmpty} onChange={e => setWithEmpty(e.target.checked)} /> 측정 없는 날도 빈 칸(-)으로 넣기</label>
        )}
        <span style={{ flex: 1 }} />
        <button className="btn btn-primary" disabled={allBlocks.length === 0} onClick={() => copy(allBlocks, `${allBlocks.length}칸을`)}>
          보이는 {allBlocks.length}칸 모두 복사
        </button>
      </div>
      <div className="icp-elbar">
        <span className="icp-flt-lbl">설비</span>
        <button className={`icp-chip ${sel === null ? 'on' : ''}`} onClick={() => setSel(null)}>전체</button>
        {eqs.map(eq => (
          <button key={eq} className={`icp-chip ${sel !== null && sel.includes(eq) ? 'on' : ''}`}
            onClick={() => (sel === null ? setSel([eq]) : toggleEq(eq))}>{eq}</button>
        ))}
        {eqs.length === 0 && <span className="icp-dim">{line} 설비의 측정 자료가 없습니다.</span>}
      </div>
      {msg && <div className="icp-rmsg">{msg}</div>}
      {days.length >= 93 && <div className="icp-rmsg warn">기간은 93일까지만 보여 줍니다.</div>}

      {groups.map(g => g.blocks.length > 0 && (
        <section key={g.eq} className="icp-rgroup">
          <div className="icp-rghead">
            <b>{g.eq}</b>
            <span className="icp-dim">측정 {g.blocks.filter(b => b.measured).length}일{days.length > 1 ? ` / ${days.length}일` : ''}</span>
            <span style={{ flex: 1 }} />
            {g.blocks.length > 1 && <button className="btn btn-ghost icp-rbtn" onClick={() => copy(g.blocks, `${g.eq} ${g.blocks.length}칸을`)}>{g.eq} 전체 복사</button>}
          </div>
          {g.blocks.map(b => (
            <div key={b.date} className={`icp-rblock ${b.measured ? '' : 'empty'}`}>
              <div className="icp-rtitle">
                <span>5. ICP-MS 측정 결과 ({shortDate(b.date)})</span>
                <button className="icp-rcopy" onClick={() => copy([b], `${g.eq} ${shortDate(b.date)} 칸을`)}>복사</button>
              </div>
              <div className="icp-rscroll">
                <table className="icp-rtable">
                  <thead><tr><th>Chemical Name</th><th>SPEC</th>{REPORT_ELS.map(e => <th key={e}>{e}</th>)}<th>총 합</th><th>비 고</th></tr></thead>
                  <tbody>
                    {b.rows.map((r, i) => (
                      <tr key={r.chem}>
                        <td className="b">{r.chem}</td><td className="b">&lt;1ppb</td>
                        {r.values ? r.values.map((v, j) => <td key={j}>{f3(v)}</td>) : REPORT_ELS.map(e => <td key={e}>-</td>)}
                        <td className="b">{r.total === null ? '-' : f3(r.total)}</td>
                        {i === 0 && <td rowSpan={b.rows.length} className="note">각 원소별 SPEC 적용</td>}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          ))}
        </section>
      ))}
    </div>
  );
}
