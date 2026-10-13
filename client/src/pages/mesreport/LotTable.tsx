import { LOT_COLS, won, type LotCol, type LotRow } from './mesReport';

/** LOT 목록 표 — 가로로 길어서 칸 안에서 옆으로 넘긴다. numbered 면 맨 앞에 줄 번호. */
export default function LotTable({ rows, empty, cols = LOT_COLS, numbered }: { rows: LotRow[]; empty?: string; cols?: LotCol[]; numbered?: boolean }) {
  const span = cols.length + (numbered ? 1 : 0);
  return (
    <div className="mr-scroll">
      <table className="mr-table mr-lots">
        <thead><tr>{numbered && <th className="no">#</th>}{cols.map(([k, l]) => <th key={k}>{l}</th>)}</tr></thead>
        <tbody>
          {rows.map((r, i) => (
            <tr key={`${r.lotId}-${i}`}>
              {numbered && <td className="no">{i + 1}</td>}
              {cols.map(([k]) => <td key={k} className={k === 'price' ? 'n' : k === 'matDesc' ? 'w' : ''}>{k === 'price' ? won(r.price) : (r[k] as string)}</td>)}
            </tr>
          ))}
          {rows.length === 0 && <tr><td colSpan={span} className="mr-empty">{empty ?? '해당하는 LOT 가 없습니다.'}</td></tr>}
        </tbody>
      </table>
    </div>
  );
}
