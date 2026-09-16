import { useCallback, useEffect, useMemo, useState } from 'react';
import { getToken } from '../api/client';
import './Mes.css';

function getMesUrl() {
  const configured = import.meta.env.VITE_MES_URL?.trim();
  const fallback = `${window.location.origin}/mes-runtime`;

  if (!configured) return fallback;

  try {
    const url = new URL(configured, window.location.origin);
    return url.protocol === 'http:' || url.protocol === 'https:'
      ? url.toString().replace(/\/$/, '')
      : fallback;
  } catch {
    return fallback;
  }
}

// MES 세션 교환이 실패했을 때, 사람이 보고 바로 조치할 수 있는 한 줄로 바꾼다.
// (원인별로 손댈 곳이 달라서 "실행 중인지 확인하세요" 한 문장으로는 진단이 안 됐다.)
function describeFailure(reason: string) {
  switch (reason) {
    case 'no-token':
      return '포털 로그인 정보가 없습니다. 로그아웃 후 다시 로그인해 주세요.';
    case 'not-proxied':
      return 'MES 프록시(/mes-runtime)가 연결되지 않았습니다. MES 서버(기본 localhost:5206)가 실행 중인지, 포털이 최신 버전으로 배포됐는지 확인해 주세요.';
    case 'unauthorized':
      return '포털 로그인이 만료되었거나 MES 사용 권한이 없습니다. 다시 로그인한 뒤 시도해 주세요.';
    case 'offline':
      return 'MES 서버에 연결할 수 없습니다. MES 서버(기본 localhost:5206)가 실행 중인지 확인해 주세요.';
    default:
      return `MES 세션을 만들지 못했습니다 (${reason}).`;
  }
}

export default function Mes() {
  const mesUrl = useMemo(getMesUrl, []);
  const [frameKey, setFrameKey] = useState(0);
  const [sessionState, setSessionState] = useState<'connecting' | 'ready' | 'error'>('connecting');
  const [failReason, setFailReason] = useState('');
  const isMixedContent = window.location.protocol === 'https:' && mesUrl.startsWith('http:');

  const connectMes = useCallback(async () => {
    if (isMixedContent) return;
    const token = getToken();
    if (!token) {
      setFailReason('no-token');
      setSessionState('error');
      return;
    }

    setSessionState('connecting');
    try {
      const response = await fetch(`${mesUrl}/auth/portal-session`, {
        method: 'GET',
        headers: { Authorization: `Bearer ${token}` },
        credentials: 'include',
      });

      // 프록시가 없으면 이 경로가 SPA fallback(index.html)으로 떨어져 200 + HTML 이 온다.
      // 그대로 '연결됨' 으로 처리하면 iframe 안에 포털이 다시 열려 원인을 알 수 없게 된다.
      const contentType = response.headers.get('content-type') ?? '';
      if (response.ok && contentType.includes('text/html')) {
        setFailReason('not-proxied');
        setSessionState('error');
        return;
      }
      if (response.status === 401 || response.status === 403) {
        setFailReason('unauthorized');
        setSessionState('error');
        return;
      }
      if (!response.ok) {
        setFailReason(`HTTP ${response.status}`);
        setSessionState('error');
        return;
      }
      setSessionState('ready');
    } catch {
      setFailReason('offline');
      setSessionState('error');
    }
  }, [isMixedContent, mesUrl]);

  useEffect(() => {
    void connectMes();
  }, [connectMes]);

  // MES 세션 쿠키는 12시간짜리다. 화면을 계속 열어 둔 채 만료되면 iframe 안에서
  // "CleanPotal에서 접속해 주세요" 안내로 떨어지므로, 탭으로 돌아올 때 조용히 갱신해 둔다.
  useEffect(() => {
    const refresh = () => {
      if (document.visibilityState === 'visible' && sessionState === 'ready') void connectMes();
    };
    document.addEventListener('visibilitychange', refresh);
    return () => document.removeEventListener('visibilitychange', refresh);
  }, [connectMes, sessionState]);

  return (
    <section className="mes-page">
      <header className="mes-header">
        <div className="mes-heading">
          <span className="mes-mark" aria-hidden="true">🏭</span>
          <div>
            <h2>MES</h2>
            <p>LOT 전산등록부터 입고·공정·검사·출하까지 통합 관리</p>
          </div>
        </div>
        <div className="mes-actions">
          <button className="btn btn-ghost" type="button" onClick={() => {
            void connectMes().then(() => setFrameKey(key => key + 1));
          }}>
            새로고침
          </button>
          <a className="btn btn-primary" href={mesUrl} target="_blank" rel="noopener noreferrer">
            새 창으로 열기
          </a>
        </div>
      </header>

      {isMixedContent ? (
        <div className="mes-notice" role="alert">
          <strong>MES 연결 주소를 확인해 주세요.</strong>
          <span>CleanPotal은 HTTPS인데 MES가 HTTP라 브라우저가 내부 표시를 차단합니다. 두 서비스의 HTTPS 설정을 맞추거나 새 창으로 열어 주세요.</span>
        </div>
      ) : sessionState === 'connecting' ? (
        <div className="mes-connecting" role="status">
          <span className="mes-spinner" aria-hidden="true" />
          <strong>CleanPotal 로그인 정보를 MES에 연결하고 있습니다.</strong>
        </div>
      ) : sessionState === 'error' ? (
        <div className="mes-notice" role="alert">
          <strong>MES 자동 로그인에 실패했습니다.</strong>
          <span>{describeFailure(failReason)}</span>
          <button className="btn btn-primary mes-local-link" type="button" onClick={() => void connectMes()}>다시 연결</button>
        </div>
      ) : (
        <div className="mes-frame-shell">
          <iframe
            key={frameKey}
            className="mes-frame"
            src={mesUrl}
            title="Production Management MES"
            allow="camera"
          />
        </div>
      )}
    </section>
  );
}
