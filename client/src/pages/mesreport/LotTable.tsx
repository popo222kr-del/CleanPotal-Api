import { LOT_COLS, type LotCol, type LotRow } from './mesReport';

/** 머리 줄 — 묶음(c[3])이 있는 칸이 하나라도 있으면 두 줄(묶음 → 칸), 없으면 한 줄 */
function Head({ cols, numbered }: { cols: LotCol[]; numbered?: boolean }) {
  if (!cols.some(c => c[3])) return <thead><tr>{numbered && <th className="no">#</th>}{cols.map(([k, l]) => <th key={k}>{l}</th>)}</tr></thead>;
  const top: { label: string; span: number; key: string; group: boolean }[] = [];
  cols.forEach(([k, l, , g]) => {
    const last = top[top.length - 1];
    if (g && last?.group && last.label === g) last.span++;
    else top.push({ label: g ?? l, span: 1, key: k, group: !!g });
  });
  return (
    <thead className="two">
      <tr>
        {numbered && <th className="no" rowSpan={2}>#</th>}
        {top.map(t => (t.group ? <th key={t.key} colSpan={t.span} className="grp">{t.label}</th> : <th key={t.key} rowSpan={2}>{t.label}</th>))}
      </tr>
      <tr>{cols.filter(c => c[3]).map(([k, l]) => <th key={k}>{l}</th>)}</tr>
    </thead>
  );
}

/** LOT 목록 표 — 가로로 길어서 칸 안에서 옆으로 넘긴다. numbered 면 맨 앞에 줄 번호, onLot 이면 LOT ID 를 눌러 상세. */
export default function LotTable({ rows, empty, cols = LOT_COLS, numbered, onLot }: {
  rows: LotRow[]; empty?: string; cols?: LotCol[]; numbered?: boolean; onLot?: (lotId: string) => void;
}) {
  const span = cols.length + (numbered ? 1 : 0);
  // 반입라인이 반출라인과 다르면(다른 라인으로 청구되는 건) 빨간색 — 출하 관리(SCM) 화면처럼
  const cls = (r: LotRow, k: keyof LotRow) => k === 'matDesc' || k === 'comment' ? 'w'
    : (k === 'inLine' || k === 'line') && r.inLine && r.line && r.inLine !== r.line ? 'diff' : '';
  const cell = (r: LotRow, k: keyof LotRow) => {
    if (k === 'lotId' && onLot) return <button type="button" className="mr-num" onClick={() => onLot(r.lotId)} title="LOT 상세(이력·검사값) 새 창">{r.lotId}</button>;
    return r[k] as string;
  };
  return (
    <div className="mr-scroll">
      <table className="mr-table mr-lots">
        <Head cols={cols} numbered={numbered} />
        <tbody>
          {rows.map((r, i) => (
            <tr key={`${r.lotId}-${i}`}>
              {numbered && <td className="no">{i + 1}</td>}
              {cols.map(([k]) => <td key={k} className={cls(r, k)} title={k === 'inLine' ? r.shipTo : undefined}>{cell(r, k)}</td>)}
            </tr>
          ))}
          {rows.length === 0 && <tr><td colSpan={span} className="mr-empty">{empty ?? '해당하는 LOT 가 없습니다.'}</td></tr>}
        </tbody>
      </table>
    </div>
  );
}
