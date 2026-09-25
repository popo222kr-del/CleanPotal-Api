import { useEffect, useState } from 'react';
import { api } from '../../api/client';
import type { CheckReport, CheckZoneDef } from '../../api/types';
import { dayLabel, timeLabel } from './common';
import { exportCheckReport } from './checkReportExcel';

// 월간 리포트 — 종이 양식을 그대로 흉내 내지 않고 "언제 누가 무엇을 점검했는지" 가 한눈에 보이는 표.
// 인쇄(A3 가로)와 엑셀 저장을 지원한다.

const DOW = ['일', '월', '화', '수', '목', '금', '토'];

function monthNow() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
}

function cellClass(v: string): string {
  if (v === 'O') return 'ok';
  if (v === 'X' || v.startsWith('!')) return 'ng';
  if (v === '미') return 'miss';
  if (v === 'N/A') return 'na';
  return v ? 'val' : '';
}
function cellText(v: string): string {
  if (v === 'O') return '○';
  if (v === 'X') return '✕';
  if (v.startsWith('!')) return v.slice(1);
  return v;
}

export default function ReportTab() {
  const [lines, setLines] = useState<string[]>([]);
  const [line, setLine] = useState('');
  const [month, setMonth] = useState(monthNow());
  const [report, setReport] = useState<CheckReport | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    api.get<CheckZoneDef[]>('/api/checklist/zones').then(zs => {
      const ls = [...new Set(zs.filter(z => z.isActive && !z.isCommon).map(z => z.line))];
      setLines(ls);
      setLine(l => l || ls[0] || '');
    }).catch(() => {});
  }, []);

  async function load() {
    if (!line || !month) return;
    const [y, m] = month.split('-').map(Number);
    setLoading(true);
    try { setReport(await api.get<CheckReport>(`/api/checklist/report?line=${encodeURIComponent(line)}&year=${y}&month=${m}`)); }
    catch (e) { alert(e instanceof Error ? e.message : '리포트를 만들지 못했습니다.'); }
    finally { setLoading(false); }
  }
  useEffect(() => { if (line) load(); /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, [line]);

  const days = report ? Array.from({ length: report.days }, (_, i) => i + 1) : [];
  const dow = (d: number) => report ? new Date(report.year, report.month - 1, d).getDay() : 0;
  // 구역 이름 칸을 합치기 위해 구역별 줄 수를 센다.
  const span: Record<string, number> = {};
  report?.rows.forEach(r => { span[r.zoneCode] = (span[r.zoneCode] ?? 0) + 1; });
  const seen = new Set<string>();

  return (
    <div>
      <div className="ck-toolbar ck-noprint">
        {lines.length > 1 && (
          <div className="ck-seg">
            {lines.map(l => <button key={l} className={line === l ? 'on' : ''} onClick={() => setLine(l)}>{l}</button>)}
          </div>
        )}
        <input className="ck-input" type="month" value={month} onChange={e => setMonth(e.target.value)} />
        <button className="ck-btn-sm primary" onClick={load} disabled={loading}>{loading ? '만드는 중…' : '조회'}</button>
        {report && (
          <div className="ck-toolbar-right">
            <button className="ck-btn-sm" onClick={() => window.print()}>인쇄 (A3 가로)</button>
            <button className="ck-btn-sm" onClick={() => exportCheckReport(report)}>엑셀 저장</button>
          </div>
        )}
      </div>

      {report && (
        <div className="ck-report">
          <div className="ck-rtitle">
            <h2>{report.year}년 {report.month}월 {report.line} {report.formName}</h2>
            <div className="ck-rmeta">
              {report.revision} {report.effectiveDate && `· 시행일 ${report.effectiveDate}`} · 출력 {timeLabel(report.generatedAt)}
            </div>
            <table className="ck-sign"><tbody>
              <tr><th>작성</th><th>검토</th><th>승인</th></tr>
              <tr><td /><td /><td /></tr>
            </tbody></table>
          </div>
          <div className="ck-legend">○ 양호 · <span className="ng">✕ NG</span> · 숫자 측정값(<span className="ng">빨강</span>=기준 벗어남) · N/A 해당 없음 · <span className="miss">미</span> 점검 안 함 · 칸 위=주간, 아래=야간</div>

          <div className="ck-rscroll">
            <table className="ck-grid">
              <thead>
                <tr>
                  <th className="z">구역</th>
                  <th className="t">점검 항목</th>
                  <th className="w">시점</th>
                  {days.map(d => <th key={d} className={`d ${dow(d) === 0 ? 'sun' : dow(d) === 6 ? 'sat' : ''}`}>{d}<br />{DOW[dow(d)]}</th>)}
                </tr>
              </thead>
              <tbody>
                {report.rows.map(r => {
                  const first = !seen.has(r.zoneCode);
                  seen.add(r.zoneCode);
                  return (
                    <tr key={r.zoneCode + r.itemCode}>
                      {first && <td rowSpan={span[r.zoneCode]} className="z">{r.zoneName}</td>}
                      <td className="t"><span className="ck-code">{r.itemCode}</span> {r.text}{r.detail && <div className="ck-dim">{r.detail}</div>}</td>
                      <td className="w">{r.timing}</td>
                      {days.map(d => {
                        const a = r.cells[(d - 1) * 2], b = r.cells[(d - 1) * 2 + 1];
                        return (
                          <td key={d} className="d2">
                            <div className={cellClass(a)}>{cellText(a)}</div>
                            <div className={cellClass(b)}>{cellText(b)}</div>
                          </td>
                        );
                      })}
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          <div className="ck-rbottom">
            <table className="ck-rsum">
              <thead><tr><th>구역</th><th>대상</th><th>완료</th><th>미점검</th><th>NG</th><th>이행률</th></tr></thead>
              <tbody>
                {report.zones.map(z => (
                  <tr key={z.zoneCode}>
                    <td>{z.zoneName}</td><td>{z.due}</td><td>{z.done}</td>
                    <td className={z.missing ? 'ng' : ''}>{z.missing}</td><td className={z.ng ? 'ng' : ''}>{z.ng}</td>
                    <td>{z.due ? `${Math.round((z.done / z.due) * 1000) / 10}%` : '-'}</td>
                  </tr>
                ))}
              </tbody>
            </table>

            <table className="ck-rng">
              <thead><tr><th>일자</th><th>구역</th><th>항목</th><th>사유 / 값</th><th>조치</th><th>점검자</th></tr></thead>
              <tbody>
                {report.ngs.length === 0 && <tr><td colSpan={6} className="ck-dim">이 달 NG 없음</td></tr>}
                {report.ngs.map(n => (
                  <tr key={n.resultId}>
                    <td>{dayLabel(n.workDate)} {n.shift}</td><td>{n.zoneName}</td><td>{n.itemCode} {n.itemText}</td>
                    <td>{n.memo}{n.numValue !== null && ` (${n.numValue}, 기준 ${n.specText})`}</td>
                    <td>{n.ngStatus === 'DONE' ? `${n.ngCloseNote} — ${n.ngClosedBy}` : <span className="ng">미조치</span>}</td>
                    <td>{n.checkedByName}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}
