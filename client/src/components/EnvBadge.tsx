import { useEffect } from 'react';
import { useAbout } from '../hooks/useAbout';

// 지금 보고 있는 포털이 개발·테스트·운영 중 어디인지. 세 서버가 같은 화면이라 헷갈리지 않게
// 로고 옆 배지와 브라우저 탭 제목("[테스트] …")으로 보여 준다. 서버가 /api/about 으로 알려 준다(PortalAbout).

const BASE_TITLE = '세정팀 업무 통합 관리';

/**
 * 로고 옆 배지(inline) 또는 사이드바 로고 아래 띠(strip — 빌드 정보까지).
 * 운영 서버에서는 아무것도 보이지 않는다(개발·테스트만 표시).
 */
export default function EnvBadge({ variant = 'inline' }: { variant?: 'inline' | 'strip' }) {
  const about = useAbout();
  useEffect(() => {
    if (!about) return;
    document.title = about.env === 'prod' ? BASE_TITLE : `[${about.envLabel}] ${BASE_TITLE}`;
  }, [about]);
  // 운영 서버는 표시하지 않는다 — 실제 사용 화면은 깔끔하게. 개발·테스트에서만 어디인지 알린다.
  if (!about || about.env === 'prod') return null;
  const ver = about.commit ? `빌드 ${about.commit}${about.dirty ? '+수정' : ''} · ${about.builtAt}` : '빌드 정보 없음';
  if (variant === 'strip') {
    return (
      <div className={`env-strip env-${about.env}`} title={`${ver}${about.subject ? `\n${about.subject}` : ''}`}>
        <b>{about.envLabel} 서버</b>
        <span>{about.env === 'dev' ? '실시간 코드'
          : about.commit ? `${about.commit}${about.dirty ? '+' : ''} · ${about.builtAt.slice(5)}` : '빌드 정보 없음'}</span>
      </div>
    );
  }
  return (
    <span className={`env-badge env-${about.env}`} title={`${about.envLabel} 서버 · ${ver}${about.subject ? `\n${about.subject}` : ''}`}>
      {about.envLabel}
    </span>
  );
}
