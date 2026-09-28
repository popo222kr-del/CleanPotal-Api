import { useRef, useState, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';

// 웹앱 안에서 체크시트 QR 을 찍는다.
// 휴대폰 기본 카메라로 QR 을 찍으면 홈 화면 웹앱이 아니라 사파리·크롬으로 열린다(아이폰은 막을 방법이 없다).
// 웹앱 안의 이 버튼으로 찍으면 웹앱 그대로 구역 화면으로 간다.
// 실시간 카메라 스캔(getUserMedia)은 https 에서만 되므로, http 주소에서도 되는 '사진 찍기 → 사진 속 QR 읽기' 방식이다.

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

/** 누르면 카메라가 열리고, 찍은 사진의 QR 이 가리키는 구역 체크시트로 간다. */
export default function QrScanButton({ className, title, children }: { className?: string; title?: string; children: ReactNode }) {
  const input = useRef<HTMLInputElement>(null);
  const nav = useNavigate();
  const [busy, setBusy] = useState(false);

  async function onFile(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    e.target.value = '';   // 같은 사진을 다시 골라도 onChange 가 오게
    if (!file) return;
    setBusy(true);
    try {
      const text = await decodeQr(file);
      if (!text) { alert('사진에서 QR 을 찾지 못했습니다.\nQR 이 화면 가운데에 크게, 흔들리지 않게 다시 찍어 주세요.'); return; }
      const code = zoneCodeFromQr(text);
      if (!code) { alert(`체크시트 구역 QR 이 아닙니다.\n(읽은 내용: ${text.slice(0, 80)})`); return; }
      nav(`/c/${encodeURIComponent(code)}`);
    } catch {
      alert('사진을 읽지 못했습니다. 다시 찍어 주세요.');
    } finally { setBusy(false); }
  }

  return (
    <>
      <button type="button" className={className} title={title ?? 'QR 찍기'} disabled={busy}
        onClick={() => input.current?.click()}>{children}</button>
      <input ref={input} type="file" accept="image/*" capture="environment" hidden onChange={onFile} />
      {busy && <div className="qr-busy">QR 읽는 중…</div>}
    </>
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
