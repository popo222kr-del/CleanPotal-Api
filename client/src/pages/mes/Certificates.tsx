import { useCallback, useEffect, useRef, useState } from 'react';
import { api, upload as postFile } from '../../api/client';
import { useAccess } from '../../auth/useAccess';
import { dateTime, downloadFile } from './lot';
import './Mes.css';

// MES 성적서 조회 — LOT 번호·파일명으로 찾고, 새 버전을 올린다.

type Doc = {
  documentId: number; lotId: number; lotNumber: string; fileName: string;
  documentVersion: number; createdBy: string; createdAt: string;
};
type Lot = { lotId: number; lotNumber: string; productName: string; serialNumber: string };
type SearchResult = { documents: Doc[]; lots: Lot[] };

export default function MesCertificates() {
  const canEdit = useAccess().canEditMes;
  const fileRef = useRef<HTMLInputElement>(null);
  const [keyword, setKeyword] = useState('');
  const [result, setResult] = useState<SearchResult>({ documents: [], lots: [] });
  const [targetLotId, setTargetLotId] = useState(0);
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  const [isError, setIsError] = useState(false);

  const search = useCallback(async (q: string) => {
    setBusy(true);
    try {
      const r = await api.get<SearchResult>(`/api/mes/certificates?keyword=${encodeURIComponent(q.trim())}`);
      setResult(r);
      // 고른 LOT 이 새 결과에 없으면 선택을 푼다 — 엉뚱한 LOT 에 올리지 않게.
      setTargetLotId(prev => (r.lots.some(l => l.lotId === prev) ? prev : 0));
      setIsError(false);
      setMsg(`성적서 ${r.documents.length}건을 조회했습니다.`);
    } catch {
      setIsError(true); setMsg('성적서를 조회하지 못했습니다.');
    } finally { setBusy(false); }
  }, []);

  useEffect(() => { void search(''); }, [search]);

  async function download(doc: Doc) {
    setBusy(true);
    try {
      // 목록이 버전별로 보이므로 고른 그 버전을 받는다(최신이 아니라).
      const name = await downloadFile(
        `/api/mes/lot/${doc.lotId}/documents/${doc.documentId}`,
        `${doc.lotNumber}_성적서_v${doc.documentVersion}.xlsx`);
      setIsError(false); setMsg(`${name} 을(를) 받았습니다.`);
    } catch (e) {
      setIsError(true); setMsg(e instanceof Error ? e.message : '성적서를 받지 못했습니다.');
    } finally { setBusy(false); }
  }

  async function upload(file: File | undefined) {
    if (!file || targetLotId === 0) return;
    setBusy(true);
    try {
      const body = new FormData();
      body.append('file', file);
      const r = await postFile<{ success: boolean; message: string }>(
        `/api/mes/lot/${targetLotId}/documents`, body);
      setIsError(!r.success); setMsg(r.message);
      if (r.success) await search(keyword);
    } catch (e) {
      setIsError(true); setMsg(e instanceof Error ? e.message : '성적서를 올리지 못했습니다.');
    } finally { setBusy(false); }
  }

  return (
    <div className="mes-page">
      <header className="pg-header">
        <div><h2>성적서 조회</h2></div>
        <input className="input mes-search" placeholder="LOT 번호 또는 파일명"
               value={keyword} onChange={e => setKeyword(e.target.value)}
               onKeyDown={e => { if (e.key === 'Enter') void search(keyword); }} />
        <button className="btn btn-primary" onClick={() => void search(keyword)} disabled={busy}>조회</button>
      </header>

      <div className="pg-body">
        <section className="mes-info">
          <div className="mes-title">성적서 새 버전 등록</div>
          <div className="mes-upload-row">
            <select value={targetLotId} onChange={e => setTargetLotId(Number(e.target.value))} disabled={!canEdit}>
              <option value={0}>
                {result.lots.length > 0 ? '등록 대상 LOT 을 고르세요' : '먼저 LOT 번호로 검색하세요'}
              </option>
              {result.lots.map(l => (
                <option key={l.lotId} value={l.lotId}>{l.lotNumber} · {l.productName} · {l.serialNumber}</option>
              ))}
            </select>
            <button className="btn btn-ghost" onClick={() => fileRef.current?.click()}
                    disabled={busy || targetLotId === 0 || !canEdit}>
              Excel 성적서 선택 및 등록
            </button>
            <input ref={fileRef} type="file" accept=".xlsx,.xlsm,.xls" style={{ display: 'none' }}
                   onChange={e => { const f = e.target.files?.[0]; e.target.value = ''; void upload(f); }} />
          </div>
          {!canEdit && <p className="mes-dim">성적서 등록은 MES 편집 권한이 필요합니다.</p>}
        </section>

        {msg && <p className={`mes-alert ${isError ? 'error' : 'ok'}`}>{msg}</p>}

        <div className="mes-scroll tall">
          <table className="mes-table">
            <thead>
              <tr><th>LOT 번호</th><th>파일명</th><th className="num">버전</th><th>등록자</th><th>등록일시</th><th></th></tr>
            </thead>
            <tbody>
              {result.documents.map(d => (
                <tr key={d.documentId}>
                  <td>{d.lotNumber}</td>
                  <td>{d.fileName}</td>
                  <td className="num">v{d.documentVersion}</td>
                  <td>{d.createdBy}</td>
                  <td>{dateTime(d.createdAt)}</td>
                  <td><button className="mes-sm" onClick={() => void download(d)} disabled={busy}>받기</button></td>
                </tr>
              ))}
              {result.documents.length === 0 && (
                <tr><td colSpan={6} className="mes-empty">{busy ? '조회 중…' : '성적서가 없습니다.'}</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
