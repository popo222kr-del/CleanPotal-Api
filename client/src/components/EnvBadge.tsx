import { useEffect } from 'react';
import { useAbout } from '../hooks/useAbout';

// 지금 보고 있는 포털이 개발·테스트·운영 중 어디인지. 세 서버가 같은 화면이라 헷갈리지 않게
// 로고 옆 배지와 브라우저 탭 제목("[테스트] …")으로 보여 준다. 서버가 /api/about 으로 알려 준다(PortalAbout).

const BASE_TITLE = '세정팀 업무 통합 관리';

/** 로고 옆 배지. 운영은 작고 차분하게, 개발·테스트는 눈에 띄게. */
export default function EnvBadge() {
  const about = useAbout();
  useEffect(() => {
    if (!about) return;
    document.title = about.env === 'prod' ? BASE_TITLE : `[${about.envLabel}] ${BASE_TITLE}`;
  }, [about]);
  if (!about) return null;
  const ver = about.commit ? `빌드 ${about.commit}${about.dirty ? '+수정' : ''} · ${about.builtAt}` : '빌드 정보 없음';
  return (
    <span className={`env-badge env-${about.env}`} title={`${about.envLabel} 서버 · ${ver}${about.subject ? `\n${about.subject}` : ''}`}>
      {about.envLabel}
    </span>
  );
}
