import { useCallback, useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { api, ApiError } from '../../api/client';
import { dateOnly } from './lot';
import './Mes.css';

// MES "LOT 현황 조회". MES(Blazor) 화면을 포털 안으로 옮긴 첫 화면이다.
// 조회 규칙(무엇을 S/N 기준으로 묶는지 등)은 서버의 MES 업무 계층이 그대로 갖고 있고,
// 여기서는 그 결과를 그리기만 한다.

type Header = {
  lotId: number;
  lotNumber: string;
  serialNumber: string;
  exportNumber: string | null;
  matId: string;
  matDesc: string;
  customerName: string;
  line: string | null;
  currentOperCode: number;
  currentOperName: string;
  recipeId: string | null;
  resId: string | null;
  comment: string | null;
  pmEquipmentName: string | null;
};

type Transition = {
  operCode: number; operDesc: string; tranCode: string; tranTime: string;
  quantity: number; recipeId: string | null; recipeDesc: string | null;
  resId: string | null; tranCause: string | null; comment: string | null;
  userId: string; userDesc: string;
};

type Cycle = { no: number; inboundDate: string; outboundDate: string | null; remarks: string | null };

type ParamRecord = {
  operCode: number; operDesc: string; parameterCode: string; parameterDescription: string;
  inputValue: string | null; comment: string | null; recordedAt: string;
};

type LotHistoryResult = {
  header: Header;
  transitions: Transition[];
  cycles: Cycle[];
  parameters: ParamRecord[];
};

const pad = (n: number) => String(n).padStart(2, '0');
/** 'MM-DD HH:mm' — 이력 표는 열이 많아 연도까지 쓰면 한 줄이 넘친다 */
function short(iso: string) {
  const d = new Date(iso);
  return `${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
}
const day = (iso: string | null) => dateOnly(iso) || '-';

export default function LotHistory() {
  // OPER 목록에서 '현황' 으로 넘어오는 경로(?lot=LOT번호)를 MES 화면과 똑같이 받는다.
  const [params, setParams] = useSearchParams();
  const lotParam = params.get('lot') ?? '';

  const [keyword, setKeyword] = useState(lotParam);
  const [result, setResult] = useState<LotHistoryResult | null>(null);
  // LOT QR — 라벨로 인쇄해 붙이면 LOT 스캔·OPER 화면에서 찍어 바로 찾을 수 있다(값 = LOT 번호).
  const [qr, setQr] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [notFound, setNotFound] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  const search = useCallback(async (q: string) => {
    const term = q.trim();
    if (!term) return;
    setBusy(true); setErr(null); setNotFound(false);
    try {
      const r = await api.get<LotHistoryResult | null>(`/api/mes/lot/history?keyword=${encodeURIComponent(term)}`);
      setResult(r);
      setNotFound(r === null);
      // QR 은 없어도 나머지는 보여줄 수 있으니 따로 받고, 실패해도 조회를 깨지 않는다.
      setQr(null);
      if (r) {
        try {
          const img = await api.get<{ pngBase64: string }>(`/api/mes/lot/qr?value=${encodeURIComponent(r.header.lotNumber)}`);
          setQr(img.pngBase64);
        } catch { /* QR 만 빠진다 */ }
      }
    } catch (e) {
      setResult(null);
      setQr(null);
      setErr(e instanceof ApiError ? e.message : '조회에 실패했습니다.');
    } finally {
      setBusy(false);
    }
  }, []);

  // 주소로 들어온 LOT 번호는 한 번 자동 조회한다.
  useEffect(() => { if (lotParam) { setKeyword(lotParam); void search(lotParam); } }, [lotParam, search]);

  function submit() {
    // 주소에도 남겨 둬야 새로고침·뒤로가기에서 같은 결과가 나온다.
    const term = keyword.trim();
    if (!term) return;
    if (term !== lotParam) setParams({ lot: term }, { replace: true });
    else void search(term);
  }

  const h = result?.header;

  return (
    <div className="mes-page">
      <header className="pg-header">
        <div><h2>LOT 현황 조회</h2></div>
        <input
          className="input mes-search"
          placeholder="LOT번호 / S/N / 반출번호"
          value={keyword}
          onChange={e => setKeyword(e.target.value)}
          onKeyDown={e => { if (e.key === 'Enter') submit(); }}
        />
        <button className="btn btn-primary" onClick={submit} disabled={busy || !keyword.trim()}>조회</button>
      </header>

      <div className="pg-body">
        {err && <p className="mes-alert error">{err}</p>}
        {notFound && <p className="mes-alert warn">해당하는 LOT을 찾을 수 없습니다.</p>}
        {busy && <p className="mes-dim">조회 중…</p>}

        {h && (
          <>
            <section className="mes-info with-qr">
              <dl>
                <div><dt>LOT 번호</dt><dd className="strong">{h.lotNumber}</dd></div>
                <div><dt>S/N</dt><dd>{h.serialNumber}</dd></div>
                <div><dt>반출번호</dt><dd>{h.exportNumber ?? '-'}</dd></div>
                <div><dt>MAT ID</dt><dd>{h.matId} · {h.matDesc}</dd></div>
                <div><dt>고객사</dt><dd>{h.customerName}</dd></div>
                <div><dt>PM RES ID</dt><dd>{h.pmEquipmentName ?? '-'}</dd></div>
                <div><dt>LINE</dt><dd>{h.line ?? '-'}</dd></div>
                <div><dt>현재 공정</dt><dd>{h.currentOperCode} · {h.currentOperName}</dd></div>
                <div className="wide"><dt>코멘트</dt><dd>{h.comment ?? '-'}</dd></div>
              </dl>
              {qr && (
                <figure className="mes-qr">
                  <img src={`data:image/png;base64,${qr}`} alt={`LOT ${h.lotNumber} QR`} />
                  <figcaption>LOT QR (라벨 · 스캔용)</figcaption>
                </figure>
              )}
            </section>

            <h3 className="mes-title">TRAN 이력</h3>
            <div className="mes-scroll">
              <table className="mes-table">
                <thead>
                  <tr>
                    <th>OPER</th><th>공정</th><th>TRAN</th><th>시각</th><th className="num">수량</th>
                    <th>레시피</th><th>설비</th><th>사유</th><th>작업자</th>
                  </tr>
                </thead>
                <tbody>
                  {result.transitions.map((t, i) => (
                    <tr key={`${t.operCode}-${t.tranCode}-${t.tranTime}-${i}`}>
                      <td>{t.operCode}</td>
                      <td>{t.operDesc}</td>
                      <td>{t.tranCode}</td>
                      <td>{short(t.tranTime)}</td>
                      <td className="num">{t.quantity}</td>
                      <td>{t.recipeId ?? ''}</td>
                      <td>{t.resId ?? ''}</td>
                      <td>{t.tranCause ?? ''}</td>
                      <td>{t.userId}</td>
                    </tr>
                  ))}
                  {result.transitions.length === 0 && (
                    <tr><td colSpan={9} className="mes-empty">이력이 없습니다.</td></tr>
                  )}
                </tbody>
              </table>
            </div>

            <div className="mes-split">
              <div>
                <h3 className="mes-title">입고/출고 사이클 (S/N 기준 누적)</h3>
                <div className="mes-scroll">
                  <table className="mes-table">
                    <thead><tr><th>NO</th><th>입고일자</th><th>출고일자</th><th>비고</th></tr></thead>
                    <tbody>
                      {result.cycles.map(c => (
                        <tr key={c.no}>
                          <td>{c.no}</td>
                          <td>{day(c.inboundDate)}</td>
                          <td>{day(c.outboundDate)}</td>
                          <td>{c.remarks ?? ''}</td>
                        </tr>
                      ))}
                      {result.cycles.length === 0 && (
                        <tr><td colSpan={4} className="mes-empty">기록이 없습니다.</td></tr>
                      )}
                    </tbody>
                  </table>
                </div>
              </div>

              <div>
                <h3 className="mes-title">파라미터 적용 값</h3>
                <div className="mes-scroll">
                  <table className="mes-table">
                    <thead>
                      <tr><th>OPER</th><th>공정</th><th>PARAMETER</th><th>DESC</th><th>값</th><th>코멘트</th><th>기록시각</th></tr>
                    </thead>
                    <tbody>
                      {result.parameters.map((p, i) => (
                        <tr key={`${p.operCode}-${p.parameterCode}-${p.recordedAt}-${i}`}>
                          <td>{p.operCode}</td>
                          <td>{p.operDesc}</td>
                          <td>{p.parameterCode}</td>
                          <td>{p.parameterDescription}</td>
                          <td>{p.inputValue ?? ''}</td>
                          <td>{p.comment ?? ''}</td>
                          <td>{short(p.recordedAt)}</td>
                        </tr>
                      ))}
                      {result.parameters.length === 0 && (
                        <tr><td colSpan={7} className="mes-empty">기록이 없습니다.</td></tr>
                      )}
                    </tbody>
                  </table>
                </div>
              </div>
            </div>
          </>
        )}

        {!h && !busy && !notFound && !err && (
          <p className="mes-dim">LOT번호 · S/N · 반출번호 중 아는 것을 넣고 조회하세요.</p>
        )}
      </div>
    </div>
  );
}
