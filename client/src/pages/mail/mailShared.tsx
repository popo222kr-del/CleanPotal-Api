import { useMemo, useRef } from 'react';
import { download } from '../../api/client';

// 메일 확인 — 목록 화면과 새 창 보기가 같이 쓰는 것(형식·본문 칸·읽기 칸).

export interface Att { index: number; name: string; contentType: string; size: number }
export interface Detail {
  id: number; folder: string; subject: string; from: string; fromAddr: string; to: string; cc: string; date: string | null;
  html: string | null; text: string | null; attachments: Att[]; remoteImagesBlocked: boolean; starred: boolean; size: number;
}

export const fmtSize = (n: number) => (n >= 1024 * 1024 * 1024 ? `${(n / 1024 / 1024 / 1024).toFixed(1)}GB`
  : n >= 1024 * 1024 ? `${(n / 1024 / 1024).toFixed(1)}MB` : n >= 1024 ? `${Math.round(n / 1024)}KB` : `${n}B`);

/** 서버 파일을 원래 이름으로 저장 */
export async function saveFile(path: string, fallbackName: string) {
  const res = await download(path);
  const cd = res.headers.get('content-disposition') ?? '';
  const m = /filename\*=UTF-8''([^;]+)/i.exec(cd) ?? /filename="?([^";]+)"?/i.exec(cd);
  const name = m ? decodeURIComponent(m[1]) : fallbackName;
  const url = URL.createObjectURL(await res.blob());
  const a = document.createElement('a');
  a.href = url; a.download = name;
  document.body.appendChild(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}

/** 메일을 새 창으로 — 창 하나를 다시 쓰지 않고 메일마다 새로 연다(여러 통을 나란히 볼 수 있게). */
export function openMailWindow(id: number) {
  const w = Math.min(1100, screen.availWidth - 80), h = Math.min(900, screen.availHeight - 80);
  window.open(`/mail/view/${id}`, `mail-${id}`, `width=${w},height=${h},left=${(screen.availWidth - w) / 2},top=40,resizable=yes,scrollbars=yes`);
}

/** 메일 본문을 격리된 칸에 — 스크립트는 못 돌고, 바깥 그림·글꼴은 불러오지 않는다(열람 추적 방지). 링크는 새 창. */
export function MailBody({ html }: { html: string }) {
  const ref = useRef<HTMLIFrameElement>(null);
  const doc = useMemo(() => `<!doctype html><html><head><meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data:; style-src 'unsafe-inline'; font-src data:">
<base target="_blank"><style>body{margin:0;padding:16px 18px;font:14px/1.6 -apple-system,'Malgun Gothic',sans-serif;color:#1f2937;word-break:break-word}
img{max-width:100%;height:auto}table{max-width:100%}pre{white-space:pre-wrap}</style></head><body>${html}</body></html>`, [html]);
  const fit = () => {
    const f = ref.current;
    const h = f?.contentDocument?.documentElement?.scrollHeight;
    if (f && h) f.style.height = `${h + 8}px`;
  };
  return <iframe ref={ref} className="ml-frame" title="메일 본문" sandbox="allow-same-origin allow-popups allow-popups-to-escape-sandbox" srcDoc={doc} onLoad={fit} />;
}

/** 읽기 칸 — 머리(제목·보낸/받는 사람·날짜)·첨부·본문. */
export function MailReader({ d, onStar, onNewWindow, onBack, onSender, onError }: {
  d: Detail;
  onStar?: () => void;
  onNewWindow?: () => void;
  onBack?: () => void;
  /** 이 사람의 메일만 보기 */
  onSender?: (addr: string) => void;
  onError?: (msg: string) => void;
}) {
  const save = (path: string, name: string) => saveFile(path, name).catch(e => onError?.(e instanceof Error ? e.message : '받지 못했습니다.'));
  return (
    <>
      <div className="ml-rhead">
        <div className="ml-rtools">
          {onBack && <button type="button" className="ml-back" onClick={onBack}>‹ 목록</button>}
          <span className="ml-rtools-r">
            {onStar && (
              <button type="button" className={`ml-tbtn star${d.starred ? ' on' : ''}`} onClick={onStar} aria-pressed={d.starred} title="중요 표시">
                {d.starred ? '★' : '☆'} 중요
              </button>
            )}
            {onNewWindow && <button type="button" className="ml-tbtn" onClick={onNewWindow} title="새 창으로 보기">⧉ 새 창</button>}
            <button type="button" className="ml-tbtn" onClick={() => void save(`/api/mail/${d.id}/eml`, 'mail.eml')} title="Outlook 등에서 열 수 있는 원본">원본(.eml)</button>
          </span>
        </div>
        <h3>{d.subject || '(제목 없음)'}</h3>
        <dl>
          <dt>보낸 사람</dt>
          <dd>
            {d.from}
            {onSender && d.fromAddr && <button type="button" className="ml-link" onClick={() => onSender(d.fromAddr)}>이 사람 메일 모두</button>}
          </dd>
          <dt>받는 사람</dt><dd>{d.to || '—'}</dd>
          {d.cc && <><dt>참조</dt><dd>{d.cc}</dd></>}
          <dt>날짜</dt><dd>{d.date ?? '—'} <span className="ml-dim">· {fmtSize(d.size)}</span></dd>
        </dl>
        {d.attachments.length > 0 && (
          <div className="ml-acts">
            {d.attachments.map(a => (
              <button key={a.index} type="button" className="ml-att" title={`${a.name} 받기`}
                onClick={() => void save(`/api/mail/${d.id}/attachments/${a.index}`, a.name)}>
                📎 {a.name} <em>{fmtSize(a.size)}</em>
              </button>
            ))}
          </div>
        )}
        {d.remoteImagesBlocked && <div className="ml-note">보안을 위해 바깥 그림은 표시하지 않았습니다.</div>}
      </div>
      <div className="ml-content">
        {d.html !== null ? <MailBody html={d.html} /> : <pre className="ml-text">{d.text ?? '(본문 없음)'}</pre>}
      </div>
    </>
  );
}
