import { LOT_COLS, won, type LotRow } from './mesReport';

/** LOT 목록 표 — 가로로 길어서 칸 안에서 옆으로 넘긴다 */
export default function LotTable({ rows, empty }: { rows: LotRow[]; empty?: string }) {
  return (
    <div className="mr-scroll">
      <table className="mr-table mr-lots">
        <thead><tr>{LOT_COLS.map(([k, l]) => <th key={k}>{l}</th>)}</tr></thead>
        <tbody>
          {rows.map((r, i) => (
            <tr key={`${r.lotId}-${i}`}>
              {LOT_COLS.map(([k]) => <td key={k} className={k === 'price' ? 'n' : k === 'matDesc' ? 'w' : ''}>{k === 'price' ? won(r.price) : (r[k] as string)}</td>)}
            </tr>
          ))}
          {rows.length === 0 && <tr><td colSpan={LOT_COLS.length} className="mr-empty">{empty ?? '해당하는 LOT 가 없습니다.'}</td></tr>}
        </tbody>
      </table>
    </div>
  );
}
