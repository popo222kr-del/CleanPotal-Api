import { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../../api/client';
import { useAccess } from '../../auth/useAccess';
import './Mes.css';

// MES 전산등록(CREATE). 여러 행을 표에 채운 뒤 한 번에 등록한다.
// 행마다 독립 등록이라 한 행이 실패해도 나머지는 계속 등록된다.
// 엑셀에서 여러 행을 복사해 표에 붙여넣을 수 있다(열 순서는 표 머리글과 같다).

type RefProduct = {
  productId: number; cleaningCode: string; productName: string;
  customerId: number | null; customerName: string | null;
  processRouteId: number | null; routeName: string | null;
};

type RowResult = {
  rowIndex: number; success: boolean; message: string;
  firstLotNumber: string | null; exportNumber: string | null;
};

type Row = {
  key: number;
  shipDate: string;      // 'YYYY-MM-DD'
  exportNumber: string;
  serialNumber: string;
  cleaningCode: string;
  line: string;
  processLabel: string;
  pmEquipmentName: string;
  teamName: string;
  quantity: number;
  orderNumber: string;
  // 등록 결과
  done: boolean;
  message: string | null;
  lotNumber: string | null;
};

let nextKey = 1;
const blankRow = (): Row => ({
  key: nextKey++, shipDate: '', exportNumber: '', serialNumber: '', cleaningCode: '',
  line: '', processLabel: '', pmEquipmentName: '', teamName: '', quantity: 1, orderNumber: '',
  done: false, message: null, lotNumber: null,
});
const isBlank = (r: Row) =>
  !r.shipDate && !r.exportNumber && !r.serialNumber && !r.cleaningCode &&
  !r.line && !r.processLabel && !r.pmEquipmentName && !r.teamName && !r.orderNumber;

/** 엑셀에서 붙여넣은 날짜 표기를 'YYYY-MM-DD' 로. 못 읽으면 빈 문자열. */
function parseDate(s: string): string {
  const t = s.trim();
  if (!t) return '';
  const m = t.match(/^(\d{4})[-./]?(\d{1,2})[-./]?(\d{1,2})$/);
  if (!m) return '';
  const [, y, mo, d] = m;
  return `${y}-${mo.padStart(2, '0')}-${d.padStart(2, '0')}`;
}

const PASTE_HINT =
  '엑셀에서 행을 복사해 표를 누르고 Ctrl+V 로 붙여넣을 수 있습니다 '
  + '(고객출고일 · 반출번호 · S/N · 세정코드 · LINE · PROCESS · 업체명 · 분임조 · 수량 · 발주번호).';

export default function MesRegister() {
  const nav = useNavigate();
  const acc = useAccess();
  const canEdit = acc.canEditMes;

  const [products, setProducts] = useState<RefProduct[]>([]);
  const [rows, setRows] = useState<Row[]>([blankRow()]);
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [isError, setIsError] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);

  useEffect(() => {
    void (async () => {
      try {
        setProducts(await api.get<RefProduct[]>('/api/mes/register/reference'));
      } catch {
        setLoadError('제품 목록을 불러오지 못했습니다. 새로고침해 주세요.');
      }
    })();
  }, []);

  // 세정코드 → 제품. 대소문자는 구분하지 않는다(라벨 표기가 섞여 있다).
  const byCode = useMemo(() => {
    const m = new Map<string, RefProduct>();
    for (const p of products) m.set(p.cleaningCode.toUpperCase(), p);
    return m;
  }, [products]);

  const match = useCallback((code: string) => byCode.get(code.trim().toUpperCase()) ?? null, [byCode]);

  function patch(key: number, change: Partial<Row>) {
    setRows(rs => rs.map(r => (r.key === key ? { ...r, ...change } : r)));
  }

  function addRow() { setRows(rs => [...rs, blankRow()]); }
  function removeRow(key: number) {
    setRows(rs => {
      const left = rs.filter(r => r.key !== key);
      return left.length > 0 ? left : [blankRow()];
    });
  }
  function clearRows() {
    setRows([blankRow()]);
    setStatus(null); setIsError(false);
  }

  // 엑셀 붙여넣기 — 빈 행부터 채우고 모자라면 행을 늘린다(MES 화면과 같은 규칙).
  function onPaste(e: React.ClipboardEvent<HTMLDivElement>) {
    const text = e.clipboardData.getData('text/plain');
    if (!text.includes('\t')) return;      // 한 칸에 붙여넣는 보통 입력은 그대로 둔다
    e.preventDefault();
    if (busy) return;

    const parsed: Partial<Row>[] = [];
    for (const raw of text.split('\n')) {
      const cells = raw.replace(/\r$/, '').split('\t');
      if (cells.length < 4) continue;
      const cell = (i: number) => (i < cells.length ? cells[i].trim() : '');
      if (cell(3) === '세정코드') continue;   // 머리글까지 복사한 경우
      const code = cell(3);
      const product = match(code);
      parsed.push({
        shipDate: parseDate(cell(0)),
        exportNumber: cell(1),
        serialNumber: cell(2),
        cleaningCode: code,
        line: cell(4),
        processLabel: cell(5),
        // 업체명을 비워 두면 제품의 업체 이름을 기본값으로 쓴다
        pmEquipmentName: cell(6) || product?.customerName || '',
        teamName: cell(7),
        quantity: Number.parseInt(cell(8), 10) > 0 ? Number.parseInt(cell(8), 10) : 1,
        orderNumber: cell(9),
        done: false, message: null, lotNumber: null,
      });
    }

    if (parsed.length === 0) {
      setIsError(true);
      setStatus('붙여넣을 행이 없습니다. 고객출고일부터 세정코드까지 4개 열 이상을 복사하세요.');
      return;
    }

    setRows(rs => {
      const next = [...rs];
      for (const p of parsed) {
        const slot = next.findIndex(r => !r.done && isBlank(r));
        if (slot >= 0) next[slot] = { ...next[slot], ...p };
        else next.push({ ...blankRow(), ...p });
      }
      return next;
    });
    setIsError(false);
    setStatus(`${parsed.length}행을 붙여넣었습니다.`);
  }

  async function registerAll() {
    if (busy) return;
    const pending = rows.filter(r => !r.done && !isBlank(r));
    if (pending.length === 0) {
      setIsError(true);
      setStatus('등록할 행이 없습니다. 세정코드를 입력하거나 엑셀에서 붙여넣으세요.');
      return;
    }

    setBusy(true); setStatus(null);
    try {
      const results = await api.post<RowResult[]>('/api/mes/register', {
        rows: pending.map(r => ({
          shipDate: r.shipDate || null,
          exportNumber: r.exportNumber, serialNumber: r.serialNumber,
          cleaningCode: r.cleaningCode, line: r.line, processLabel: r.processLabel,
          pmEquipmentName: r.pmEquipmentName, teamName: r.teamName,
          quantity: r.quantity, orderNumber: r.orderNumber,
        })),
      });

      // 보낸 순서(rowIndex)로 되돌려 붙인다. 성공한 행은 잠겨서 다시 눌러도 재등록되지 않는다.
      const byKey = new Map<number, RowResult>();
      results.forEach(res => { const r = pending[res.rowIndex]; if (r) byKey.set(r.key, res); });
      setRows(rs => rs.map(r => {
        const res = byKey.get(r.key);
        if (!res) return r;
        return { ...r, done: res.success, message: res.message, lotNumber: res.firstLotNumber };
      }));

      const ok = results.filter(r => r.success).length;
      const failed = results.length - ok;
      setIsError(failed > 0);
      setStatus(failed === 0
        ? `${ok}건 모두 등록되었습니다.`
        : `${ok}건 성공, ${failed}건 실패 — 행별 메시지를 확인하세요.`);
    } catch {
      setIsError(true);
      setStatus('등록 중 문제가 발생했습니다.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="mes-page">
      <header className="pg-header">
        <div><h2>전산등록</h2></div>
        <button className="btn btn-ghost" onClick={addRow} disabled={busy}>행 추가</button>
        <button className="btn btn-ghost" onClick={clearRows} disabled={busy}>전체 초기화</button>
        <button className="btn btn-primary" onClick={registerAll} disabled={busy || !canEdit}>
          {busy ? '등록 중…' : '일괄 전산등록'}
        </button>
      </header>

      <div className="pg-body">
        {loadError && <p className="mes-alert error">{loadError}</p>}
        {!canEdit && <p className="mes-alert warn">전산등록은 MES 편집 권한이 필요합니다. 관리자에게 요청하세요.</p>}

        <div className="mes-scroll tall" onPaste={onPaste}>
          <table className="mes-table mes-grid">
            <thead>
              <tr>
                <th>고객출고일</th><th>반출번호</th><th>S/N</th><th>세정코드</th><th>LINE</th>
                <th>PROCESS</th><th>업체명</th><th>분임조</th><th className="num">수량</th><th>발주번호</th>
                <th>품목명 확인</th><th className="wide">결과</th><th></th>
              </tr>
            </thead>
            <tbody>
              {rows.map(r => {
                const product = r.cleaningCode.trim() ? match(r.cleaningCode) : null;
                const locked = r.done || busy || !canEdit;
                return (
                  <tr key={r.key} className={r.done ? 'done' : ''}>
                    <td><input type="date" value={r.shipDate} disabled={locked} onChange={e => patch(r.key, { shipDate: e.target.value })} /></td>
                    <td><input placeholder="자동" value={r.exportNumber} disabled={locked} onChange={e => patch(r.key, { exportNumber: e.target.value })} /></td>
                    <td><input placeholder="자동" value={r.serialNumber} disabled={locked} onChange={e => patch(r.key, { serialNumber: e.target.value })} /></td>
                    <td>
                      <input
                        value={r.cleaningCode}
                        disabled={locked}
                        onChange={e => {
                          const code = e.target.value;
                          const p = match(code);
                          // 업체가 바뀌면 업체명 칸 기본값을 그 업체 이름으로 바꾼다(직접 고친 값은 둔다).
                          const keepTyped = r.pmEquipmentName && r.pmEquipmentName !== match(r.cleaningCode)?.customerName;
                          patch(r.key, {
                            cleaningCode: code,
                            pmEquipmentName: keepTyped ? r.pmEquipmentName : (p?.customerName ?? ''),
                          });
                        }}
                      />
                    </td>
                    <td><input value={r.line} disabled={locked} onChange={e => patch(r.key, { line: e.target.value })} /></td>
                    <td><input value={r.processLabel} disabled={locked} onChange={e => patch(r.key, { processLabel: e.target.value })} /></td>
                    <td><input value={r.pmEquipmentName} disabled={locked} onChange={e => patch(r.key, { pmEquipmentName: e.target.value })} /></td>
                    <td><input value={r.teamName} disabled={locked} onChange={e => patch(r.key, { teamName: e.target.value })} /></td>
                    <td className="num">
                      <input type="number" min={1} className="num" value={r.quantity} disabled={locked}
                             onChange={e => patch(r.key, { quantity: Math.max(1, Number.parseInt(e.target.value, 10) || 1) })} />
                    </td>
                    <td><input value={r.orderNumber} disabled={locked} onChange={e => patch(r.key, { orderNumber: e.target.value })} /></td>
                    <td className={!product && r.cleaningCode.trim() ? 'bad' : ''}>
                      {r.cleaningCode.trim() ? (product?.productName ?? '세정코드를 찾을 수 없습니다') : ''}
                    </td>
                    <td className={r.done ? 'good' : 'bad'}>
                      {r.message}
                      {r.lotNumber && (
                        <button className="mes-sm" style={{ marginLeft: 6 }}
                                onClick={() => nav(`/mes/history?lot=${encodeURIComponent(r.lotNumber!)}`)}>현황</button>
                      )}
                    </td>
                    <td><button className="mes-sm" onClick={() => removeRow(r.key)} disabled={busy}>삭제</button></td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>

        <p className={`mes-status ${status ? (isError ? 'bad' : 'good') : ''}`}>{status ?? PASTE_HINT}</p>
      </div>
    </div>
  );
}
