import { download, upload } from '../api/client';

// 첨부를 문자열 하나로 담는 방식. 화면 여러 곳이 같은 칸(JSON 문자열 배열)을 쓰므로
// 넣고 읽는 규칙을 여기 한 곳에 둔다.
//
// 지금 담는 것
// - att:<id>|<원래 이름>|<image|file>   파일은 서버 디스크에, DB 에는 이 줄만
//
// 예전에 담긴 것도 그대로 읽는다
// - data:image/...;base64,...            사진을 통째로 넣던 시절
// - data:<형식>;name=<이름>;base64,...   파일을 통째로 넣던 시절
// - C:\\... 같은 경로 문자열             WPF 시절. 담긴 파일이 없어 받을 수 없다
//
// base64 로 담으면 1.33배, nvarchar 가 글자당 2바이트라 다시 2배 — 1MB 파일이
// DB 에서 2.7MB 를 먹었다. 그래서 파일은 디스크로 뺀다.

const MAX_DIM = 1400;

export function resizeImg(dataUrl: string): Promise<string> {
  return new Promise(res => {
    const img = new Image();
    img.onload = () => {
      const scale = Math.min(1, MAX_DIM / Math.max(img.width, img.height));
      const cv = document.createElement('canvas');
      cv.width = Math.round(img.width * scale);
      cv.height = Math.round(img.height * scale);
      const ctx = cv.getContext('2d');
      if (!ctx) { res(dataUrl); return; }
      ctx.drawImage(img, 0, 0, cv.width, cv.height);
      try { res(cv.toDataURL('image/jpeg', 0.72)); } catch { res(dataUrl); }
    };
    img.onerror = () => res(dataUrl);
    img.src = dataUrl;
  });
}

export function fileToUrl(f: File): Promise<string> {
  return new Promise((res, rej) => {
    const fr = new FileReader();
    fr.onload = () => res(fr.result as string);
    fr.onerror = rej;
    fr.readAsDataURL(f);
  });
}

// 파일은 따로 올라가므로 기록 하나에 몇 개가 붙든 상관없다. 한 개 상한만 둔다.
// 서버는 30MB 까지 받는다(AttachmentsController.MaxBytes).
export const MAX_FILE_BYTES = 25 * 1024 * 1024;
export const MAX_IMG_BYTES = 25 * 1024 * 1024;

export function attName(v: string) {
  const ref = parseRef(v);
  if (ref) return ref.name || `첨부 ${ref.id}`;
  const m = /;name=([^;,]*)/.exec(v);
  if (m) { try { return decodeURIComponent(m[1]); } catch { return m[1]; } }
  return v.split(/[\\/]/).pop() || v;
}

export const isImgAtt = (v: string) => v.startsWith('data:image/') || parseRef(v)?.kind === 'image';
/** 받을 수 있는 파일인지. 옛 WPF 경로 문자열은 여기에 들지 않는다 — 파일이 없다. */
export const isFileAtt = (v: string) =>
  (v.startsWith('data:') && !v.startsWith('data:image/')) || parseRef(v)?.kind === 'file';


/** 못 넣은 파일을 한 번에 알려 준다. 조용히 사라지지 않게. */
export function warnSkipped(skipped: string[]) {
  if (skipped.length) alert(`넣지 못한 파일\n\n${skipped.join('\n')}`);
}


// ── 서버 보관소 ────────────────────────────────────────────────────────────
type AttachmentDto = { id: number; fileName: string; contentType: string; size: number; kind: string; ref: string };

export const isRefAtt = (v: string) => v.startsWith('att:');

/** att:<id>|<이름>|<종류> 를 뜯는다. 모양이 어긋나면 null. */
export function parseRef(v: string): { id: number; name: string; kind: string } | null {
  if (!isRefAtt(v)) return null;
  const [idPart, namePart = '', kind = 'file'] = v.slice(4).split('|');
  const id = Number(idPart);
  if (!Number.isFinite(id) || id <= 0) return null;
  let name = namePart;
  try { name = decodeURIComponent(namePart); } catch { /* 그대로 쓴다 */ }
  return { id, name, kind };
}

/** 사진을 1400px JPEG 로 줄인 File. 못 줄이면 원본을 그대로 돌려준다. */
async function shrink(f: File): Promise<File> {
  if (!f.type.startsWith('image/')) return f;
  try {
    const small = await resizeImg(await fileToUrl(f));
    const res = await fetch(small);
    const blob = await res.blob();
    if (blob.size >= f.size) return f;   // 줄여서 더 커지면 의미가 없다
    const name = f.name.replace(/\.[^.]+$/, '') + '.jpg';
    return new File([blob], name, { type: 'image/jpeg' });
  } catch { return f; }
}

/**
 * 파일들을 서버에 올리고 기록 칸에 담을 문자열을 돌려준다.
 * 못 올린 것은 이유와 함께 알려 준다 — 조용히 사라지지 않게.
 */
export async function filesToAtts(files: File[], opts?: { imagesOnly?: boolean }): Promise<string[]> {
  const send: File[] = [];
  const skipped: string[] = [];
  for (const f of files) {
    if (opts?.imagesOnly && !f.type.startsWith('image/')) { skipped.push(`${f.name} — 사진만 넣는 칸입니다`); continue; }
    if (f.size > (f.type.startsWith('image/') ? MAX_IMG_BYTES : MAX_FILE_BYTES)) {
      skipped.push(`${f.name} — 용량이 너무 큽니다`);
      continue;
    }
    send.push(await shrink(f));
  }
  warnSkipped(skipped);
  if (send.length === 0) return [];

  const form = new FormData();
  for (const f of send) form.append('files', f, f.name);
  try {
    const rows = await upload<AttachmentDto[]>('/api/attachments', form);
    return rows.map(r => r.ref);
  } catch (e) {
    alert(e instanceof Error ? e.message : '첨부를 올리지 못했습니다.');
    return [];
  }
}

/** 첨부를 브라우저가 쓸 수 있는 주소로. 예전에 담긴 data: 값은 그대로 쓴다. */
export async function attUrl(v: string): Promise<string | null> {
  const ref = parseRef(v);
  if (!ref) return v.startsWith('data:') ? v : null;
  // <img src> 로는 Authorization 헤더를 실을 수 없어 받아서 넘긴다
  const res = await download(`/api/attachments/${ref.id}`);
  return URL.createObjectURL(await res.blob());
}

/** 첨부를 원래 이름으로 내려받는다. */
export async function saveAtt(v: string) {
  const ref = parseRef(v);
  try {
    const href = ref ? URL.createObjectURL(await (await download(`/api/attachments/${ref.id}`)).blob()) : v;
    const a = document.createElement('a');
    a.href = href;
    a.download = attName(v);
    document.body.appendChild(a);
    a.click();
    a.remove();
    if (ref) setTimeout(() => URL.revokeObjectURL(href), 10_000);
  } catch (e) {
    alert(e instanceof Error ? e.message : '첨부를 받지 못했습니다.');
  }
}
