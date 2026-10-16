import { useEffect, useMemo, useState } from 'react';
import { api } from '../../api/client';
import NotConnected from './NotConnected';
import { downloadXlsx, useLegacyStatus, useWideTable, won } from './mesReport';
import './MesReport.css';

// MES 조회 › 제품 단가 — 기존 MES DB 의 품목(MWIPMATDEF)과 단가(MAT_CMF_4). 읽기 전용.
// 적용일자별 단가 이력·제품 사진은 기존 DB 의 위치를 확인한 뒤 붙인다.

const v = (s: string | null | undefined) => (s ?? '').trim() || '—';

interface Mat {
  matId: string; matDesc: string; matType: string; customer: string; price: number | null;
  /** 기존 MES 제품 사용자 정의 필드 */
  proc: string; certFile: string; reworkExclude: string; salesCustomer: string; spec: string; custPartNo: string; safetyStock: string; maxClean: string;
}

export default function MesMaterials() {
  const [wideRef, wide] = useWideTable();
  const status = useLegacyStatus();
  const [rows, setRows] = useState<Mat[] | null>(null);
  const [q, setQ] = useState('');
  const [sel, setSel] = useState<Mat | null>(null);
  const [err, setErr] = useState('');

  useEffect(() => {
    if (!status?.configured) return;
    api.get<Mat[]>('/api/mes-report/materials').then(setRows).catch(e => setErr(e instanceof Error ? e.message : '불러오지 못했습니다.'));
  }, [status?.configured]);

  const shown = useMemo(() => {
    const t = q.trim().toLowerCase();
    return (rows ?? []).filter(m => !t || `${m.matId} ${m.matDesc} ${m.matType} ${m.custPartNo} ${m.certFile}`.toLowerCase().includes(t));
  }, [rows, q]);

  if (status === undefined) return <div className="page-loading">불러오는 중…</div>;
  if (!status?.configured) return <NotConnected title="제품 단가" />;

  return (
    <div className="mr-page">
      <header className="pg-header">
        <div><h2>제품 단가</h2><p>기존 MES 기준 품목과 현재 단가 · 읽기 전용</p></div>
      </header>
      <div className="pg-body">
        {err && <div className="mr-err">{err}</div>}
        <div className="mr-mat">
          <div className={`mr-card mr-mat-list mr-pscroll${wide ? ' wide' : ''}`} ref={wideRef}>
            <div className="mr-filters in">
              <input className="input" placeholder="제품 코드·설명·TYPE·고객제품코드·성적서 검색" value={q} onChange={e => setQ(e.target.value)} />
              <span className="mr-dim">{shown.length.toLocaleString()}개</span>
              {shown.length > 0 && (
                <button type="button" className="btn btn-ghost" onClick={() => void downloadXlsx('제품단가.xlsx', '제품 단가',
                  ['제품', '제품 설명', 'TYPE', '고객사', '단가'], shown.map(m => [m.matId, m.matDesc, m.matType, m.customer, m.price]), [12, 44, 14, 10, 12])}>엑셀</button>
              )}
            </div>
            <div className="mr-scroll mr-mat-scroll">
              <table className="mr-table">
                <thead><tr><th>제품</th><th>제품 설명</th><th>TYPE</th><th>고객사</th><th className="n">단가</th></tr></thead>
                <tbody>
                  {shown.map(m => (
                    <tr key={m.matId} className={`click${sel?.matId === m.matId ? ' on' : ''}`} onClick={() => setSel(m)}>
                      <td>{m.matId}</td><td className="w">{m.matDesc}</td><td>{m.matType}</td><td>{m.customer}</td><td className="n">{won(m.price)}</td>
                    </tr>
                  ))}
                  {rows && shown.length === 0 && <tr><td colSpan={5} className="mr-empty">해당하는 제품이 없습니다.</td></tr>}
                  {!rows && !err && <tr><td colSpan={5} className="mr-empty">불러오는 중…</td></tr>}
                </tbody>
              </table>
            </div>
          </div>
          <div className="mr-card mr-mat-detail">
            {!sel ? <div className="mr-empty">왼쪽에서 제품을 고르세요.</div> : (
              <>
                <h3>{sel.matDesc}</h3>
                <dl>
                  <dt>제품 코드</dt><dd>{sel.matId}</dd>
                  <dt>TYPE</dt><dd>{sel.matType || '—'}</dd>
                  <dt>고객사</dt><dd>{sel.customer}</dd>
                  <dt>현재 단가</dt><dd className="price">{sel.price == null ? '—' : `${won(sel.price)}원`}</dd>
                </dl>
                <h4 className="mr-mat-sub">기준 정보</h4>
                <dl>
                  <dt>프로세스</dt><dd>{v(sel.proc)}</dd>
                  <dt>성적서</dt><dd>{v(sel.certFile)}</dd>
                  <dt>제품규격</dt><dd>{v(sel.spec)}</dd>
                  <dt>고객제품코드</dt><dd>{v(sel.custPartNo)}</dd>
                  <dt>매출고객사</dt><dd>{v(sel.salesCustomer)}</dd>
                  <dt>재세정 제외</dt><dd>{v(sel.reworkExclude)}</dd>
                  <dt>안전재고</dt><dd>{v(sel.safetyStock)}</dd>
                  <dt>최대 세정횟수</dt><dd>{v(sel.maxClean)}</dd>
                </dl>
              </>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
