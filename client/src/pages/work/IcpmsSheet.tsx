import { useEffect, useMemo, useRef, useState } from 'react';
import { api } from '../../api/client';
import './Work.css';
import { useAccess } from '../../auth/useAccess';
import type { IcpmsEquipment, IcpmsMeasurement } from '../../api/types';
import {
  REPORT_ELS, blocksToClipboard, buildBlock, copyRich, eqCmp, f3, lineOf, shortDate,
  type Line, type ReportBlock,
} from './icpmsCopy';
import { parseIcpmsReportFile } from './icpmsImport';

// ICP-MS 보고서 — 엑셀 "AETS QA ICP-MS Data"(7번 METAL · 8번 N-METAL)의 "5. ICP-MS 측정 결과" 칸.
// 라인·날짜(기간)·설비를 골라 칸을 만들고, 엑셀 설비 시트에 그대로 붙여넣을 수 있게 복사한다
// (병합·테두리·머리글 색·소수 3자리 형식 포함, 기간이면 시트처럼 빈 줄 하나씩 띄워 이어 붙임).
// 측정 자료는 설비 ICP-MS 메뉴와 같은 자료를 쓴다.

const pad = (n: number) => String(n).padStart(2, '0');
const ymd = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
function daysBetween(from: string, to: string): string[] {
  const out: string[] = [];
  const d = new Date(from + 'T00:00:00'); const end = new Date(to + 'T00:00:00');
  while (d <= end && out.length < 93) { out.push(ymd(d)); d.setDate(d.getDate() + 1); }
  return out;
}

export default function IcpmsSheet() {
  const { canEditOffice: canEdit } = useAccess();
  const today = ymd(new Date());
  const [line, setLine] = useState<Line>('METAL');
  const [from, setFrom] = useState(today);
  const [to, setTo] = useState(today);
  const [eqAll, setEqAll] = useState<string[]>([]);
  const [rows, setRows] = useState<IcpmsMeasurement[] | null>(null);
  const [sel, setSel] = useState<string[] | null>(null);   // null = 그 라인 전체
  const [withEmpty, setWithEmpty] = useState(true);
  const [msg, setMsg] = useState('');
  const [busy, setBusy] = useState(false);
  const [reload, setReload] = useState(0);
  const fileRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    api.get<IcpmsEquipment[]>('/api/worklog/icpms/equipment')
      .then(list => setEqAll(list.filter(e => e.hasData).map(e => e.eqId.trim())))
      .catch(() => setEqAll([]));
  }, [reload]);
  useEffect(() => {
    let alive = true;
    if (!from || !to || from > to) { setRows([]); return; }
    setRows(null);
    api.get<IcpmsMeasurement[]>(`/api/worklog/icpms?from=${from}&to=${to}`)
      .then(r => { if (alive) setRows(r); })
      .catch(e => { if (alive) { setRows([]); setMsg(e instanceof Error ? e.message : '불러오지 못했습니다.'); } });
    return () => { alive = false; };
  }, [from, to, reload]);

  const eqs = useMemo(() => [...new Set(eqAll.filter(e => lineOf(e) === line))].sort(eqCmp), [eqAll, line]);
  const chosen = sel ?? eqs;
  const days = useMemo(() => (from && to && from <= to ? daysBetween(from, to) : []), [from, to]);
  const groups = useMemo(() => {
    const idx = new Map<string, IcpmsMeasurement[]>();
    for (const r of rows ?? []) {
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
  function pickEq(eq: string) {
    if (sel === null) { setSel([eq]); return; }
    const next = sel.includes(eq) ? sel.filter(x => x !== eq) : [...sel, eq].sort(eqCmp);
    setSel(next.length === 0 || next.length === eqs.length ? null : next);
  }
  async function importFile(f: File) {
    setBusy(true);
    try {
      const p = await parseIcpmsReportFile(f);
      if (p.rows.length === 0) { alert('가져올 측정값을 찾지 못했습니다. "5. ICP-MS 측정 결과 (날짜)" 칸이 있는 설비별 시트인지 확인하세요.'); return; }
      if (!confirm(`설비 ${p.sheets.length}대 · ${p.from} ~ ${p.to} · ${p.rows.length}줄을 가져옵니다.`
        + (p.future ? `\n(오늘 뒤 날짜 ${p.future}줄은 미리 채워 둔 칸이라 뺍니다)` : '') + '\n이미 있는 측정값은 건너뜁니다.')) return;
      const r = await api.post<{ received: number; inserted: number; skipped: number }>('/api/worklog/icpms/import', { rows: p.rows });
      alert(`새로 ${r.inserted}줄 · 이미 있어 건너뜀 ${r.skipped}줄`);
      setReload(k => k + 1);
    } catch (e) {
      alert(e instanceof Error ? e.message : '엑셀을 가져오지 못했습니다.');
    } finally { setBusy(false); }
  }

  return (
    <div className="wf-page">
      <header className="pg-header">
        <div>
          <h2>ICP-MS 보고서</h2>
          <p>라인·날짜·설비를 골라 보고서 칸을 엑셀에 그대로 붙여넣기</p>
        </div>
        {canEdit && <button className="btn btn-ghost" disabled={busy} onClick={() => fileRef.current?.click()}>{busy ? '가져오는 중…' : '엑셀 가져오기'}</button>}
        <input ref={fileRef} type="file" accept=".xlsx" hidden onChange={e => { const f = e.target.files?.[0]; if (f) void importFile(f); e.target.value = ''; }} />
      </header>
      <div className="pg-body">
        <div className="wf-tabs">
          {(['METAL', 'N-METAL'] as Line[]).map(l => (
            <button key={l} className={line === l ? 'on' : ''} onClick={() => { setLine(l); setSel(null); }}>{l}</button>
          ))}
        </div>
        <div className="wf-toolbar">
          <input className="input wf-date" type="date" value={from} onChange={e => { const v = e.target.value; if (!v) return; setFrom(v); if (v > to) setTo(v); }} />
          <span className="wf-dim">~</span>
          <input className="input wf-date" type="date" value={to} min={from} onChange={e => e.target.value && setTo(e.target.value)} />
          {from !== today && <button className="btn btn-ghost wf-sm" onClick={() => { setFrom(today); setTo(today); }}>오늘</button>}
          {days.length > 1 && (
            <label className="wf-chk"><input type="checkbox" checked={withEmpty} onChange={e => setWithEmpty(e.target.checked)} /> 측정 없는 날도 '-' 칸으로 넣기</label>
          )}
          <span style={{ flex: 1 }} />
          <button className="btn btn-primary wf-sm" disabled={allBlocks.length === 0} onClick={() => copy(allBlocks, `${allBlocks.length}칸을`)}>
            보이는 {allBlocks.length}칸 모두 복사
          </button>
        </div>
        <div className="wf-ichips">
          <button className={`wf-seg ${sel === null ? 'on' : ''}`} onClick={() => setSel(null)}>전체</button>
          {eqs.map(eq => (
            <button key={eq} className={`wf-seg ${sel !== null && sel.includes(eq) ? 'on' : ''}`} onClick={() => pickEq(eq)}>{eq}</button>
          ))}
          {eqs.length === 0 && <span className="wf-dim">{line} 설비의 측정 자료가 없습니다.{canEdit ? ' 엑셀 가져오기로 7·8번 파일을 넣으세요.' : ''}</span>}
        </div>
        {msg && <div className="wf-imsg">{msg}</div>}

        {rows === null ? <div className="wf-empty">불러오는 중…</div> : (
          <div className="wf-blocks">
            {groups.map(g => g.blocks.length > 0 && (
              <section key={g.eq} className="wf-bblock">
                <div className="wf-bhead">
                  <b>{g.eq}</b>
                  <span className="wf-dim">측정 {g.blocks.filter(b => b.measured).length}일{days.length > 1 ? ` / ${days.length}일` : ''}</span>
                  <span style={{ flex: 1 }} />
                  {g.blocks.length > 1 && <button className="btn btn-ghost wf-sm" onClick={() => copy(g.blocks, `${g.eq} ${g.blocks.length}칸을`)}>{g.eq} 전체 복사</button>}
                </div>
                {g.blocks.map(b => (
                  <div key={b.date} className={`wf-iblock ${b.measured ? '' : 'empty'}`}>
                    <div className="wf-ititle">
                      <span>5. ICP-MS 측정 결과 ({shortDate(b.date)})</span>
                      <button className="wf-icopy" onClick={() => copy([b], `${g.eq} ${shortDate(b.date)} 칸을`)}>복사</button>
                    </div>
                    <div className="wf-iscroll">
                      <table className="wf-itable">
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
        )}
      </div>
    </div>
  );
}
