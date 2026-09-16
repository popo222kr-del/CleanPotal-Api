import { useRef, useState } from 'react';
import { upload as postFile } from '../../api/client';
import './Mes.css';

// LOT 바코드·QR 입력 패널. LOT 스캔 화면과 OPER 화면의 스캔 창이 같이 쓴다.
//
// 사진은 서버가 읽는다 — 브라우저 실시간 카메라(getUserMedia)는 HTTPS 에서만 허용되는데
// 사내 Wi-Fi 는 HTTP 로 붙는다. 그래서 휴대폰으로 "사진"을 찍어 올린다.
//
// 읽은 값으로 무엇을 할지는 부모가 정한다(화면마다 다르다). 여기서는 값만 넘긴다.

const MAX_PHOTO_BYTES = 20 * 1024 * 1024;

type Props = {
  onScanned: (text: string) => void | Promise<void>;
  /** 부모가 조회 결과 오류(LOT 없음 등)를 이 패널에 보여줄 때 */
  errorMessage?: string | null;
  autoFocus?: boolean;
};

export default function ScanPanel({ onScanned, errorMessage, autoFocus }: Props) {
  const fileRef = useRef<HTMLInputElement>(null);
  const [code, setCode] = useState('');
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  async function submit(text?: string) {
    const value = (text ?? code).trim();
    if (!value) { setErr('LOT 번호를 입력하거나 바코드를 찍어 주세요.'); return; }
    setErr(null);
    await onScanned(value);
    // 다음 스캔을 바로 받을 수 있게 비운다(연달아 찍는 공정이 있다).
    setCode('');
  }

  async function onPhoto(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    e.target.value = '';
    if (!file) return;
    if (file.size > MAX_PHOTO_BYTES) { setErr('사진이 너무 큽니다(최대 20MB).'); return; }

    setBusy(true); setErr(null);
    try {
      const body = new FormData();
      body.append('file', file);
      const r = await postFile<{ text: string | null; message: string | null }>('/api/mes/lot/decode', body);
      if (!r.text) { setErr(r.message ?? '사진에서 바코드·QR 을 읽지 못했습니다.'); return; }
      await submit(r.text);
    } catch {
      setErr('사진을 읽는 중 문제가 발생했습니다.');
    } finally { setBusy(false); }
  }

  return (
    <>
      <button className="btn btn-primary mes-shoot" onClick={() => fileRef.current?.click()} disabled={busy}>
        카메라로 바코드 · QR 찍기
      </button>
      <input ref={fileRef} type="file" accept="image/*" capture="environment"
             style={{ display: 'none' }} onChange={onPhoto} />
      <p className="mes-dim">바코드가 화면 가운데에 크고 선명하게 나오도록 찍어 주세요.</p>

      <div className="mes-scan-input">
        <input
          className="input"
          placeholder="LOT 번호 입력 / 바코드 스캐너"
          autoComplete="off"
          enterKeyHint="go"
          autoFocus={autoFocus}
          value={code}
          onChange={e => setCode(e.target.value)}
          onKeyDown={e => { if (e.key === 'Enter') void submit(); }}
        />
        <button className="btn btn-ghost" onClick={() => void submit()} disabled={busy}>확인</button>
      </div>

      {busy && <p className="mes-dim">읽는 중…</p>}
      {(err ?? errorMessage) && <p className="mes-alert warn">{err ?? errorMessage}</p>}
    </>
  );
}
