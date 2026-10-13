import { useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { api } from '../../api/client';
import LotTable from './LotTable';
import { downloadLots, type LotRow } from './mesReport';
import { inScope, matches, openLotDetail, passes, readLotsQuery } from './wipShared';
import './MesReport.css';

// 입·출고 현황 숫자의 LOT 목록 새 창 — 사이드바 없이 목록만. 지금 재공을 다시 불러와 표와 같은 조건으로 거른다.
export default function LotsWindow() {
  const [sp] = useSearchParams();
  const { scope, key, f, label } = useMemo(() => readLotsQuery(sp), [sp]);
  const [all, setAll] = useState<LotRow[] | null>(null);
  const [err, setErr] = useState('');

  useEffect(() => { document.title = `${label} - 재공 LOT`; }, [label]);
  useEffect(() => {
    api.get<LotRow[]>('/api/mes-report/wip/lots').then(setAll)
      .catch(e => setErr(e instanceof Error ? e.message : '불러오지 못했습니다.'));
  }, []);
  const rows = useMemo(() => (all ?? []).filter(l => inScope(l, scope) && passes(l, f, scope) && matches(l, key, scope)), [all, scope, f, key]);

  return (
    <div className="mr-window">
      <div className="mr-card mr-window-card">
        <div className="mr-card-h">
          <b>{label}</b>
          {all && <span className="mr-dim">{rows.length.toLocaleString()} LOT</span>}
          {rows.length > 0 && <button type="button" className="btn btn-ghost" onClick={() => void downloadLots(`재공_${label.replace(/[\\/:*?"<>|]/g, '_')}.xlsx`, rows)}>엑셀</button>}
          <button type="button" className="btn btn-ghost mr-x" onClick={() => window.close()}>닫기</button>
        </div>
        {err && <div className="mr-err">{err}</div>}
        {!all && !err && <div className="mr-empty">불러오는 중…</div>}
        {all && <LotTable rows={rows} onLot={openLotDetail} />}
      </div>
    </div>
  );
}
