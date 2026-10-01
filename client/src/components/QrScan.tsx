import { useEffect, useRef, useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { useNavigate } from 'react-router-dom';

// 웹앱 안에서 체크시트 QR 을 찍는다.
// 휴대폰 기본 카메라로 QR 을 찍으면 홈 화면 웹앱이 아니라 사파리·크롬으로 열린다(아이폰은 막을 방법이 없다).
// 웹앱 안의 이 버튼으로 찍으면 웹앱 그대로 구역 화면으로 간다.
// https 주소(보안 연결)에서는 카메라 화면을 바로 띄워 QR 이 비치는 순간 읽는다(사진을 찍을 필요 없음).
// 카메라를 못 여는 때(http 주소·권한 거부·카메라 없음)는 예전처럼 '사진 찍기 → 사진 속 QR 읽기' 로 넘어간다.

/** QR 글자에서 구역코드를 꺼낸다. 서버 주소(테스트·운영)는 달라도 되고 '/c/코드' 만 본다. 구역코드만 적힌 QR 도 받는다. */
export function zoneCodeFromQr(text: string): string | null {
  const t = text.trim();
  const m = t.match(/\/c\/([^/?#\s]+)/i);
  if (m) return decodeURIComponent(m[1]).toUpperCase();
  if (/^[A-Z0-9-]{1,20}$/i.test(t)) return t.toUpperCase();
  return null;
}

async function loadImage(file: File): Promise<{ src: CanvasImageSource; w: number; h: number }> {
  try {
    const b = await createImageBitmap(file);
    return { src: b, w: b.width, h: b.height };
  } catch {
    const url = URL.createObjectURL(file);
    try {
      const img = new Image();
      img.src = url;
      await img.decode();
      return { src: img, w: img.naturalWidth, h: img.naturalHeight };
    } finally { URL.revokeObjectURL(url); }
  }
}

/** 사진 속 QR 글자. 폰 사진은 크고 QR 크기도 제각각이라 몇 가지 크기로 줄여 가며 읽어 본다. */
async function decodeQr(file: File): Promise<string | null> {
  const { default: jsQR } = await import('jsqr');   // 스캔할 때만 받는다
  const { src, w: w0, h: h0 } = await loadImage(file);
  const tried = new Set<number>();
  for (const max of [1200, 1800, 800, 2600]) {
    const s = Math.min(1, max / Math.max(w0, h0));
    const w = Math.round(w0 * s), h = Math.round(h0 * s);
    if (tried.has(w)) continue;
    tried.add(w);
    const c = document.createElement('canvas');
    c.width = w; c.height = h;
    const ctx = c.getContext('2d', { willReadFrequently: true });
    if (!ctx) break;
    ctx.drawImage(src, 0, 0, w, h);
    const r = jsQR(ctx.getImageData(0, 0, w, h).data, w, h);
    if (r?.data) return r.data;
  }
  return null;
}

/** QR 글자 → 갈 화면. 설비 호기 QR(/e/호기)은 체크시트(설비), 나머지는 구역 체크시트(/c/구역). 체크시트 QR 이 아니면 null. */
export function qrTarget(text: string): string | null {
  const eq = text.trim().match(/\/e\/([^/?#\s]+)/i);
  if (eq) return `/e/${encodeURIComponent(decodeURIComponent(eq[1]).toUpperCase())}`;
  const code = zoneCodeFromQr(text);
  return code ? `/c/${encodeURIComponent(code)}` : null;
}

/** 실시간 카메라 스캔을 쓸 수 있는가 — 보안 연결(https)이고 브라우저가 카메라를 열 수 있어야 한다. */
const canLiveScan = () => typeof window !== 'undefined' && window.isSecureContext && !!navigator.mediaDevices?.getUserMedia;

/** 누르면 카메라 화면이 열리고, QR 이 비치면 그 체크시트로 간다(카메라를 못 열면 사진 찍기로). */
export default function QrScanButton({ className, title, children }: { className?: string; title?: string; children: ReactNode }) {
  const input = useRef<HTMLInputElement>(null);
  const nav = useNavigate();
  const [busy, setBusy] = useState(false);
  const [live, setLive] = useState(false);

  async function onFile(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    e.target.value = '';   // 같은 사진을 다시 골라도 onChange 가 오게
    if (!file) return;
    setBusy(true);
    try {
      const text = await decodeQr(file);
      if (!text) { alert('사진에서 QR 을 찾지 못했습니다.\nQR 이 화면 가운데에 크게, 흔들리지 않게 다시 찍어 주세요.'); return; }
      const to = qrTarget(text);
      if (!to) { alert(`체크시트 QR 이 아닙니다.\n(읽은 내용: ${text.slice(0, 80)})`); return; }
      nav(to);
    } catch {
      alert('사진을 읽지 못했습니다. 다시 찍어 주세요.');
    } finally { setBusy(false); }
  }

  const photo = () => input.current?.click();

  return (
    <>
      <button type="button" className={className} title={title ?? 'QR 찍기'} disabled={busy}
        onClick={() => (canLiveScan() ? setLive(true) : photo())}>{children}</button>
      <input ref={input} type="file" accept="image/*" capture="environment" hidden onChange={onFile} />
      {busy && createPortal(<div className="qr-busy">QR 읽는 중…</div>, document.body)}
      {live && (
        <LiveScanner
          onClose={() => setLive(false)}
          onFound={to => { setLive(false); nav(to); }}
          onFallback={() => { setLive(false); photo(); }}
        />
      )}
    </>
  );
}

// 일부 브라우저(안드로이드 크롬 등)는 QR 읽기를 기본으로 갖고 있다 — 있으면 그걸 쓰고(빠르다), 없으면 jsQR.
type Detector = { detect(src: CanvasImageSource): Promise<{ rawValue: string }[]> };
type DetectorCtor = new (o: { formats: string[] }) => Detector;

/** 카메라 화면 — 뒤쪽 카메라를 켜고 화면에 비친 QR 을 계속 읽는다. */
function LiveScanner({ onClose, onFound, onFallback }: {
  onClose: () => void; onFound: (to: string) => void; onFallback: () => void;
}) {
  const video = useRef<HTMLVideoElement>(null);
  const [msg, setMsg] = useState('카메라를 켜는 중…');
  const [wrong, setWrong] = useState('');

  useEffect(() => {
    let stream: MediaStream | null = null;
    let timer = 0;
    let stopped = false;
    const canvas = document.createElement('canvas');
    const ctx = canvas.getContext('2d', { willReadFrequently: true });

    (async () => {
      try {
        stream = await navigator.mediaDevices.getUserMedia({
          video: { facingMode: { ideal: 'environment' }, width: { ideal: 1280 }, height: { ideal: 720 } }, audio: false,
        });
      } catch {
        if (stopped) return;
        // 권한 거부·카메라 없음 — 사진 찍기로 넘어간다
        alert('카메라를 열 수 없습니다(카메라 권한을 허용했는지 확인하세요).\n사진 찍기로 QR 을 읽습니다.');
        onFallback();
        return;
      }
      if (stopped) { stream.getTracks().forEach(t => t.stop()); return; }
      const v = video.current!;
      v.srcObject = stream;
      try { await v.play(); } catch { /* 자동 재생이 막혀도 프레임은 읽힌다 */ }
      setMsg('QR 을 네모 안에 비춰 주세요');

      const Ctor = (window as unknown as { BarcodeDetector?: DetectorCtor }).BarcodeDetector;
      let detector: Detector | null = null;
      try { if (Ctor) detector = new Ctor({ formats: ['qr_code'] }); } catch { detector = null; }
      const jsQR = detector ? null : (await import('jsqr')).default;
      let lastWrong = '';

      const tick = async () => {
        if (stopped) return;
        let text: string | null = null;
        try {
          if (v.readyState >= 2 && v.videoWidth) {
            if (detector) {
              text = (await detector.detect(v))[0]?.rawValue ?? null;
            } else if (jsQR && ctx) {
              // 가운데 정사각형만 잘라 작게 줄여 읽는다 — 빠르고, 네모 안의 QR 만 본다
              const side = Math.min(v.videoWidth, v.videoHeight);
              const size = Math.min(640, side);
              canvas.width = size; canvas.height = size;
              ctx.drawImage(v, (v.videoWidth - side) / 2, (v.videoHeight - side) / 2, side, side, 0, 0, size, size);
              text = jsQR(ctx.getImageData(0, 0, size, size).data, size, size, { inversionAttempts: 'dontInvert' })?.data ?? null;
            }
          }
        } catch { /* 한 프레임 실패는 넘긴다 */ }
        if (stopped) return;
        if (text) {
          const to = qrTarget(text);
          if (to) { navigator.vibrate?.(60); onFound(to); return; }
          if (text !== lastWrong) { lastWrong = text; setWrong(`체크시트 QR 이 아닙니다 (${text.slice(0, 40)})`); }
        }
        timer = window.setTimeout(tick, 120);
      };
      tick();
    })();

    return () => {
      stopped = true;
      window.clearTimeout(timer);
      stream?.getTracks().forEach(t => t.stop());
    };
    // 한 번 열 때만 — 콜백이 바뀌어도 카메라를 다시 켜지 않는다
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // 문서 맨 바깥에 그린다 — 하단 탭 막대 안의 버튼에서 열면 막대의 효과(blur 등) 때문에 전체 화면을 덮지 못했다
  return createPortal(
    <div className="qr-live" role="dialog" aria-label="QR 스캔">
      <video ref={video} playsInline muted autoPlay />
      <div className="qr-live-frame" aria-hidden="true"><i /></div>
      <div className="qr-live-top">
        <span>{msg}</span>
        {wrong && <em>{wrong}</em>}
      </div>
      <div className="qr-live-bar">
        <button type="button" onClick={onFallback}>사진으로 찍기</button>
        <button type="button" className="close" onClick={onClose}>닫기</button>
      </div>
    </div>,
    document.body,
  );
}

/** QR 모양 아이콘 */
export const QrIcon = (
  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
    <path d="M4 8V5a1 1 0 0 1 1-1h3M16 4h3a1 1 0 0 1 1 1v3M20 16v3a1 1 0 0 1-1 1h-3M8 20H5a1 1 0 0 1-1-1v-3" />
    <rect x="7.5" y="7.5" width="3.5" height="3.5" rx=".5" /><rect x="13" y="7.5" width="3.5" height="3.5" rx=".5" />
    <rect x="7.5" y="13" width="3.5" height="3.5" rx=".5" /><path d="M13 13h1.5v1.5H13zM15.5 15.5h1v1h-1z" />
  </svg>
);
