import { useEffect, useState } from 'react';
import { attUrl } from '../pages/attach';

/**
 * 첨부 문자열을 브라우저가 쓸 수 있는 주소로 바꿔 준다.
 *
 * 보관소에 있는 파일은 Authorization 헤더가 필요해 &lt;img src&gt; 로 바로 못 건다.
 * 받아서 blob 주소로 만들고, 다 쓰면 되돌려준다 — 안 그러면 탭을 닫을 때까지 메모리에 남는다.
 */
export function useAttUrl(value: string | null) {
  const [url, setUrl] = useState<string | null>(null);

  useEffect(() => {
    if (!value) { setUrl(null); return; }
    let alive = true;
    let made: string | null = null;

    attUrl(value)
      .then(u => {
        if (!alive) {
          if (u?.startsWith('blob:')) URL.revokeObjectURL(u);
          return;
        }
        if (u?.startsWith('blob:')) made = u;
        setUrl(u);
      })
      .catch(() => { if (alive) setUrl(null); });

    return () => {
      alive = false;
      if (made) URL.revokeObjectURL(made);
    };
  }, [value]);

  return url;
}
