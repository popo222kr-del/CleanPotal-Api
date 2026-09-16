import { useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, getToken } from '../../api/client';
import './Mes.css';

// MES "LOT 스캔". LOT 라벨을 찍으면 그 LOT 이 있는 공정(OPER) 화면으로 넘어간다.
// 공정 처리(TRAN 실행)는 여기서 하지 않는다 — OPER 화면의 기존 게이트를 그대로 거친다.

type ScanResult = {
  found: boolean; scannedText: string | null; lotId: number;
  lotNumber: string | null; serialNumber: string | null;
  operCode: number; operName: string | null;
  hasOperScreen: boolean; message: string | null;
};
type DecodeResult = { text: string | null; message: string | null };

const MAX_PHOTO_BYTES = 20 * 1024 * 1024;

export default function MesScan() {
  const nav = useNavigate();
  const fileRef = useRef<HTMLInputElement>(null);
  const [code, setCode] = useState('');
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  const [stuck, setStuck] = useState<ScanResult | null>(null);   // 찾았지만 OPER 화면이 없는 LOT

  async function resolve(text: string) {
    setBusy(true); setMsg(null); setStuck(null);
    try {
      const r = await api.get<ScanResult>(`/api/mes/lot/scan?code=${encodeURIComponent(text)}`);
      if (r.found && r.hasOperScreen) {
        nav(`/mes/oper/${r.operCode}?lot=${encodeURIComponent(r.lotNumber ?? '')}`);
        return;
      }
      if (r.found) setStuck(r);
      setMsg(r.message);
    } catch {
      setMsg('LOT 을 찾는 중 문제가 발생했습니다.');
    } finally {
      setBusy(false);
    }
  }

  function submit() {
    const t = code.trim();
    if (!t) { setMsg('LOT 번호를 입력하거나 바코드를 찍어 주세요.'); return; }
    void resolve(t);
  }

  // 사진은 서버가 읽는다. 브라우저 실시간 카메라는 HTTPS 에서만 쓸 수 있는데
  // 사내 Wi-Fi 는 HTTP 로 접속하기 때문이다.
  async function onPhoto(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    e.target.value = '';           // 같은 사진을 다시 골라도 동작하게
    if (!file) return;
    if (file.size > MAX_PHOTO_BYTES) { setMsg('사진이 너무 큽니다(최대 20MB).'); return; }

    setBusy(true); setMsg(null); setStuck(null);
    try {
      // 파일 업로드라 JSON 클라이언트를 쓰지 않는다 — 토큰만 같은 것을 싣는다.
      const body = new FormData();
      body.append('file', file);
      const res = await fetch('/api/mes/lot/decode', {
        method: 'POST',
        headers: { Authorization: `Bearer ${getToken() ?? ''}` },
        body,
      });
      if (!res.ok) { setMsg('사진을 보내지 못했습니다.'); return; }
      const payload = await res.json();
      const r: DecodeResult = payload?.data ?? payload;
      if (!r.text) { setMsg(r.message ?? '사진에서 바코드·QR 을 읽지 못했습니다.'); return; }
      setCode(r.text);
      await resolve(r.text);
    } catch {
      setMsg('사진을 읽는 중 문제가 발생했습니다.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="mes-page">
      <header className="pg-header"><div><h2>LOT 스캔</h2></div></header>

      <div className="pg-body">
        <div className="mes-scan">
          <section className="mes-info">
            <button className="btn btn-primary mes-shoot" onClick={() => fileRef.current?.click()} disabled={busy}>
              카메라로 바코드 · QR 찍기
            </button>
            <input
              ref={fileRef} type="file" accept="image/*" capture="environment"
              style={{ display: 'none' }} onChange={onPhoto}
            />
            <p className="mes-dim">바코드가 화면 가운데에 크고 선명하게 나오도록 찍어 주세요.</p>

            <div className="mes-scan-input">
              <input
                className="input"
                placeholder="LOT 번호 입력 / 바코드 스캐너"
                autoComplete="off"
                enterKeyHint="go"
                value={code}
                onChange={e => setCode(e.target.value)}
                onKeyDown={e => { if (e.key === 'Enter') submit(); }}
              />
              <button className="btn btn-ghost" onClick={submit} disabled={busy}>확인</button>
            </div>

            {busy && <p className="mes-dim">읽는 중…</p>}
            {msg && <p className="mes-alert warn">{msg}</p>}
            {stuck?.lotNumber && (
              <button className="mes-sm" onClick={() => nav(`/mes/history?lot=${encodeURIComponent(stuck.lotNumber!)}`)}>
                LOT 현황 조회로 보기
              </button>
            )}
          </section>

          <section className="mes-info">
            <div className="mes-title">사용 방법</div>
            <ol className="mes-howto">
              <li>휴대폰: [카메라로 바코드 · QR 찍기] 를 누르고 LOT 라벨이 화면 가운데에 크게 나오게 찍습니다.</li>
              <li>PC: USB·블루투스 스캐너로 입력칸에 찍거나, LOT 번호를 입력하고 Enter 를 누릅니다.</li>
              <li>LOT 이 있는 공정(OPER) 화면으로 이동해 그 LOT 이 선택됩니다. TRAN 을 골라 [실행] 하세요.</li>
            </ol>
            <p className="mes-dim">
              인식하는 값: LOT 번호 · S/N · 반출번호. LOT QR 은 LOT 현황 조회 화면에서 확인·인쇄할 수 있습니다.
            </p>
          </section>
        </div>
      </div>
    </div>
  );
}
