// 첨부를 문자열 하나로 담는 방식. 화면 여러 곳이 같은 칸(JSON 문자열 배열)을 쓰므로
// 넣고 읽는 규칙을 여기 한 곳에 둔다.
//
// - 사진: data:image/...;base64,... (1400px JPEG 로 줄여 담는다)
// - 파일: data:<형식>;name=<원래 이름>;base64,...
// - 옛 WPF 기록: 파일 '경로' 문자열이 그대로 들어 있다. 담긴 파일이 없어 받을 수 없다.

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

// 기록 하나를 통째로 한 번에 보내므로 서버 수신 한도(30MB) 안에 들어와야 한다.
export const MAX_FILE_BYTES = 8 * 1024 * 1024;
export const MAX_IMG_BYTES = 20 * 1024 * 1024;
export const MAX_TOTAL_BYTES = 18 * 1024 * 1024;

export function withName(dataUrl: string, name: string) {
  const i = dataUrl.indexOf(';base64,');
  if (i < 0) return dataUrl;
  const mime = dataUrl.slice(5, i) || 'application/octet-stream';
  return `data:${mime};name=${encodeURIComponent(name)};base64,${dataUrl.slice(i + 8)}`;
}

export function attName(v: string) {
  const m = /;name=([^;,]*)/.exec(v);
  if (m) { try { return decodeURIComponent(m[1]); } catch { return m[1]; } }
  return v.split(/[\\/]/).pop() || v;
}

export const isImgAtt = (v: string) => v.startsWith('data:image/');
export const isFileAtt = (v: string) => v.startsWith('data:') && !v.startsWith('data:image/');

/** 넣을 수 있으면 저장할 문자열을, 너무 크면 null 을 준다. */
export async function toAtt(f: File): Promise<string | null> {
  const img = f.type.startsWith('image/');
  if (f.size > (img ? MAX_IMG_BYTES : MAX_FILE_BYTES)) return null;
  const url = await fileToUrl(f);
  return img ? await resizeImg(url) : withName(url, f.name);
}

/** 못 넣은 파일을 한 번에 알려 준다. 조용히 사라지지 않게. */
export function warnSkipped(skipped: string[]) {
  if (skipped.length) alert(`넣지 못한 파일\n\n${skipped.join('\n')}`);
}

export const attBytes = (...lists: string[][]) => lists.flat().reduce((n, v) => n + v.length, 0);
export const mb = (n: number) => `${(n / 1024 / 1024).toFixed(1)}MB`;

/** 파일들을 첨부 문자열로 바꾼다. 못 넣은 것은 이유와 함께 알려 준다. */
export async function filesToAtts(files: File[], opts?: { imagesOnly?: boolean }): Promise<string[]> {
  const added: string[] = [];
  const skipped: string[] = [];
  for (const f of files) {
    if (opts?.imagesOnly && !f.type.startsWith('image/')) { skipped.push(`${f.name} — 사진만 넣는 칸입니다`); continue; }
    const v = await toAtt(f);
    if (v) added.push(v); else skipped.push(`${f.name} — 용량이 너무 큽니다`);
  }
  warnSkipped(skipped);
  return added;
}
