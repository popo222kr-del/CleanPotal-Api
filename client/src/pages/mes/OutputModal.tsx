import { useEffect, useRef, useState } from 'react';
import { upload as postFile } from '../../api/client';
import { useAccess } from '../../auth/useAccess';
import { downloadFile } from './lot';

// MES 출력 관리 — 입고검사·출고검사를 완료·출하로 넘길 때 먼저 열린다.
// [닫기] 를 누르는 시점에 전산이 다음 공정으로 이동한다(advancesOnClose).

type Props = {
  lotId: number;
  lotNumber: string;
  advancesOnClose: boolean;
  onClose: () => void;
};

type UploadResult = { success: boolean; message: string };

export default function OutputModal({ lotId, lotNumber, advancesOnClose, onClose }: Props) {
  const canEdit = useAccess().canEditMes;
  const certRef = useRef<HTMLInputElement>(null);
  const imageRef = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  const [isError, setIsError] = useState(false);

  // 실수로 Esc 를 눌러 닫히면 전산이 이동해 버린다. 닫기는 버튼으로만 한다.
  useEffect(() => {
    const stop = (e: KeyboardEvent) => { if (e.key === 'Escape') e.preventDefault(); };
    window.addEventListener('keydown', stop);
    return () => window.removeEventListener('keydown', stop);
  }, []);

  function ok(m: string) { setMsg(m); setIsError(false); }
  function fail(m: string) { setMsg(m); setIsError(true); }

  async function run(work: () => Promise<void>) {
    if (busy) return;
    setBusy(true); setMsg(null);
    try { await work(); }
    catch (e) { fail(e instanceof Error ? e.message : '처리 중 문제가 발생했습니다.'); }
    finally { setBusy(false); }
  }

  const getCertificate = () => run(async () => {
    const name = await downloadFile(`/api/mes/lot/${lotId}/documents/latest`, `${lotNumber}_성적서.xlsx`);
    ok(`${name} 을(를) 받았습니다.`);
  });

  const getRunsheet = () => run(async () => {
    const name = await downloadFile(`/api/mes/lot/${lotId}/runsheet`, `${lotNumber}_runsheet.xlsx`);
    ok(`${name} 을(를) 받았습니다.`);
  });

  function upload(path: string, file: File | undefined) {
    if (!file) return;
    return run(async () => {
      const body = new FormData();
      body.append('file', file);
      const result = await postFile<UploadResult>(path, body);
      if (result.success) ok(result.message); else fail(result.message);
    });
  }

  return (
    <div className="mes-modal-bg">
      <div className="mes-modal">
        <h3>출력 관리 — {lotNumber}</h3>

        <div className="mes-out-group">
          <span className="mes-out-label">성적서 (INSPECTION)</span>
          <div className="mes-out-row">
            <button className="btn btn-primary" onClick={getCertificate} disabled={busy}>성적서 받기</button>
            <button className="btn btn-ghost" onClick={() => certRef.current?.click()} disabled={busy || !canEdit}>
              성적서 업로드
            </button>
            <input ref={certRef} type="file" accept=".xlsx,.xlsm,.xls" style={{ display: 'none' }}
                   onChange={e => { const f = e.target.files?.[0]; e.target.value = '';
                                    void upload(`/api/mes/lot/${lotId}/documents`, f); }} />
          </div>
        </div>

        <div className="mes-out-group">
          <span className="mes-out-label">특이사항 (ABNORMAL) · 런시트</span>
          <div className="mes-out-row">
            <button className="btn btn-ghost" onClick={() => imageRef.current?.click()} disabled={busy || !canEdit}
                    title="성적서 엑셀에 이미지를 넣는 일은 아직 웹에서 되지 않습니다 — 데스크톱 프로그램에서 넣어 주세요.">
              특이사항 업로드
            </button>
            <input ref={imageRef} type="file" accept="image/*" style={{ display: 'none' }}
                   onChange={e => { const f = e.target.files?.[0]; e.target.value = '';
                                    void upload(`/api/mes/lot/${lotId}/abnormal-image`, f); }} />
            <button className="btn btn-ghost" onClick={getRunsheet} disabled={busy}>RUNSHEET 받기</button>
          </div>
        </div>

        <p className="mes-out-note">
          정상 라벨 · 부적합 · 폐기 · 수리는 OPER 결과와 CMT_AETS, 재작업 전이로 처리합니다.
        </p>

        {msg && <p className={`mes-alert ${isError ? 'error' : 'ok'}`}>{msg}</p>}

        {advancesOnClose && (
          <p className="mes-alert warn"><strong>[닫기] 를 누르면 전산이 다음 공정으로 이동합니다.</strong></p>
        )}

        <div className="mes-modal-foot">
          <button className="btn btn-primary" onClick={onClose} disabled={busy}>닫기</button>
        </div>
      </div>
    </div>
  );
}
