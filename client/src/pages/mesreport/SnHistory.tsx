import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { api } from '../../api/client';
import { openLotDetail } from './wipShared';
import './MesReport.css';

// 같은 부품(S/N)의 세정 이력 — 기존 MES LOT 현황 조회 왼쪽 아래 목록(회차·입고일·출고일·코멘트)에 판정·반출/반입 라인·분임조·입고/출고검사 특이사항·무게를 더했다.
// S/N 은 반입 때 바뀌면 바뀐 값(반입 S/N) 기준. 최근 회차가 위. LOT ID 를 누르면 그 LOT 상세를 새 창(비교용)으로.

export interface SnRow {
  lotId: string; useCount: string; inDay: string | null; outDay: string | null; days: number | null; shipped: boolean; status: string;
  outLine: string; inLine: string; circle: string; judge: string; inIssues: string[]; outIssues: string[]; inChecked: boolean; outChecked: boolean; weightIn: string | null; weightOut: string | null; comment: string;
  matId: string; matDesc: string; sn: string; outSn: string;
}

export function useSnHistory(sn: string | undefined) {
  const [rows, setRows] = useState<SnRow[] | null>(null);
  const [err, setErr] = useState('');
  useEffect(() => {
    if (!sn) { setRows([]); return; }
    setRows(null); setErr('');
    api.get<SnRow[]>(`/api/mes-report/sn-history?sn=${encodeURIComponent(sn)}`).then(setRows)
      .catch(e => setErr(e instanceof Error ? e.message : '불러오지 못했습니다.'));
  }, [sn]);
  return { rows, err };
}

/** 이 S/N 전체를 새 창으로 — 지금 LOT 과 나란히 비교 */
export function openSnHistory(sn: string, lotId: string) {
  const w = Math.min(1400, screen.availWidth - 40), h = Math.min(860, screen.availHeight - 60);
  window.open(`/mes-report/sn-history?sn=${encodeURIComponent(sn)}&lot=${encodeURIComponent(lotId)}`, `sn-${sn}`,
    `width=${w},height=${h},left=20,top=30,resizable=yes,scrollbars=yes`);
}

/** 판정 NG 이거나 입고·출고검사에 특이사항이 있던 회차 */
export const snBad = (r: SnRow) => r.judge === 'NG' || r.inIssues.length > 0 || r.outIssues.length > 0;

/** 검사 특이사항 칸 — 이상 항목은 빨갛게, 검사를 했고(측정값·판정 있음) 이상 없으면 '이상 없음', 검사 전이면 빈칸 */
function Issues({ list, done }: { list: string[]; done: boolean }) {
  if (list.length) return <span className="ng">{list.join(', ')}</span>;
  return done ? <span className="dim">이상 없음</span> : null;
}

/** 세정 이력 표 — current 줄은 파랗게, NG·검사 특이사항 회차는 빨갛게 */
export function SnTable({ rows, current, matId }: { rows: SnRow[]; current: string; matId?: string }) {
  return (
    <table className="ld-t ld-sn">
      <thead>
        <tr><th>회차</th><th>LOT ID</th><th>입고일</th><th>출고일</th><th>체류</th><th>반출라인</th><th>반입라인</th><th>분임조</th><th>판정</th><th>입고검사</th><th>출고검사</th><th>무게 입고 → 출고</th><th>COMMENT</th></tr>
      </thead>
      <tbody>
        {rows.map(r => {
          const bad = snBad(r);
          return (
            <tr key={r.lotId} className={`${r.lotId === current ? 'cur' : ''}${bad ? ' bad' : ''}`}>
              <td className="c">{r.useCount}</td>
              <td>
                {r.lotId === current ? <b>{r.lotId}</b> : <button type="button" className="mr-num" onClick={() => openLotDetail(r.lotId)} title="이 LOT 상세 새 창">{r.lotId}</button>}
                {r.lotId === current && <span className="ld-here">지금</span>}
                {matId && r.matId !== matId && <small className="mat"> {r.matDesc || r.matId}</small>}
              </td>
              <td className="c">{r.inDay ?? ''}</td>
              <td className="c">{r.shipped ? r.outDay ?? '' : <span className="dim">{r.status}</span>}</td>
              <td className="n">{r.days == null ? '' : `${r.days.toFixed(1)}일${r.shipped ? '' : '째'}`}</td>
              <td className="c">{r.outLine}</td>
              <td className="c">{r.inLine && r.inLine !== r.outLine ? <b className="ng" title="반출 라인과 다름">{r.inLine}</b> : r.inLine}</td>
              <td className="c">{r.circle}</td>
              <td className="c">{r.judge ? <span className={r.judge === 'NG' ? 'ng' : 'ok'}>{r.judge}</span> : ''}</td>
              <td className="c"><Issues list={r.inIssues} done={r.inChecked} /></td>
              <td className="c"><Issues list={r.outIssues} done={r.outChecked} /></td>
              <td className="n">{[r.weightIn, r.weightOut].some(Boolean) ? `${r.weightIn ?? '—'} → ${r.weightOut ?? '—'}` : ''}</td>
              <td className="w">{r.comment}</td>
            </tr>
          );
        })}
        {rows.length === 0 && <tr><td colSpan={14} className="mr-empty">이력이 없습니다.</td></tr>}
      </tbody>
    </table>
  );
}

/** 새 창 — 이 S/N 전체 회차 */
export default function SnHistoryWindow() {
  const [q] = useSearchParams();
  const sn = q.get('sn') ?? '';
  const lot = q.get('lot') ?? '';
  const { rows, err } = useSnHistory(sn);
  useEffect(() => { document.title = `${sn} - 세정 이력`; }, [sn]);
  const shipped = rows?.filter(r => r.shipped) ?? [];
  const avg = shipped.length ? shipped.reduce((a, r) => a + (r.days ?? 0), 0) / shipped.length : null;
  const ng = rows?.filter(snBad).length ?? 0;
  return (
    <div className="mr-window mr-detail">
      <div className="mr-card">
        <div className="mr-card-h">
          <b>S/N {sn} 세정 이력</b>
          {rows && <span className="mr-dim">{rows.length}회 · 평균 체류 {avg == null ? '—' : `${avg.toFixed(1)}일`} · NG·검사 특이 {ng}회 · 최근 회차가 위</span>}
          <button type="button" className="btn btn-ghost mr-x" onClick={() => window.close()}>닫기</button>
        </div>
        {err && <div className="mr-err">{err}</div>}
        {!rows && !err && <div className="mr-empty">불러오는 중…</div>}
        {rows && <div className="ld-wrap"><SnTable rows={rows} current={lot} matId={rows.find(r => r.lotId === lot)?.matId} /></div>}
      </div>
    </div>
  );
}
