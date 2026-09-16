import { useCallback, useEffect, useState } from 'react';
import { useLocation } from 'react-router-dom';
import { getToken } from '../api/client';
import './Mes.css';

// MES 는 포털과 같은 주소의 /mes-runtime 으로만 연다.
// 별도 주소를 설정으로 두면 환경마다 값이 갈리고, 다른 origin 이 되는 순간
// iframe·쿠키·WebSocket 이 전부 막힌다. 같은 주소로 고정하면 고칠 설정이 없다.
const MES_URL = `${window.location.origin}/mes-runtime`;

function describeFailure(reason: string) {
  switch (reason) {
    case 'no-token':
      return '로그인 정보가 없습니다. 로그아웃 후 다시 로그인해 주세요.';
    case 'unauthorized':
      return '로그인이 만료되었습니다. 다시 로그인한 뒤 시도해 주세요.';
    default:
      return '생산관리를 불러오지 못했습니다. 잠시 후 다시 시도해 주세요.';
  }
}

export default function Mes() {
  // /mes/oper/3000?lot=X → MES 의 /oper/3000?lot=X. 포털 사이드바에서 고른 화면이 그대로 열린다.
  // 물음표 뒤까지 넘겨야 한다 — LOT 스캔이 "이 LOT 을 선택한 채로 열어라"를 그렇게 전달한다.
  const { pathname, search } = useLocation();
  const sub = pathname.replace(/^\/mes\/?/, '');
  const frameSrc = (sub ? `${MES_URL}/${sub}` : MES_URL) + search;

  const [frameKey, setFrameKey] = useState(0);
  const [sessionState, setSessionState] = useState<'connecting' | 'ready' | 'error'>('connecting');
  const [failReason, setFailReason] = useState('');

  const connectMes = useCallback(async () => {
    const token = getToken();
    if (!token) {
      setFailReason('no-token');
      setSessionState('error');
      return;
    }

    setSessionState('connecting');
    try {
      const response = await fetch(`${MES_URL}/auth/portal-session`, {
        method: 'GET',
        headers: { Authorization: `Bearer ${token}` },
        credentials: 'include',
      });

      // 프록시가 없으면 이 경로가 SPA fallback(index.html)으로 떨어져 200 + HTML 이 온다.
      // 그대로 '연결됨' 으로 처리하면 iframe 안에 포털이 다시 열려 원인을 알 수 없게 된다.
      const contentType = response.headers.get('content-type') ?? '';
      if (response.ok && contentType.includes('text/html')) {
        setFailReason('not-ready');
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
  }, []);

  useEffect(() => {
    void connectMes();
  }, [connectMes]);

  // MES 세션 쿠키는 12시간짜리다. 화면을 계속 열어 둔 채 만료되면 iframe 안에서
  // 안내 화면으로 떨어지므로, 탭으로 돌아올 때 조용히 갱신해 둔다.
  useEffect(() => {
    const refresh = () => {
      if (document.visibilityState === 'visible' && sessionState === 'ready') void connectMes();
    };
    document.addEventListener('visibilitychange', refresh);
    return () => document.removeEventListener('visibilitychange', refresh);
  }, [connectMes, sessionState]);

  if (sessionState === 'ready') {
    // 연결되면 화면 전체를 MES 에 내준다 — 포털 사이드바 옆에 바로 이어 붙어
    // 다른 포털 화면과 똑같이 보인다(머리말·버튼 같은 군더더기를 두지 않는다).
    return (
      <div className="mes-shell">
        <iframe
          // 경로가 바뀌면 다시 그린다 — iframe 안에서 자체 이동하지 않고 포털 메뉴가 주도한다
          key={`${frameKey}:${sub}${search}`}
          className="mes-frame"
          src={frameSrc}
          title="생산관리 MES"
          allow="camera"
        />
      </div>
    );
  }

  return (
    <div className="mes-state">
      {sessionState === 'connecting' ? (
        <div className="mes-connecting" role="status">
          <span className="mes-spinner" aria-hidden="true" />
          <strong>생산관리를 여는 중입니다.</strong>
        </div>
      ) : (
        <div className="mes-notice" role="alert">
          <strong>생산관리를 열지 못했습니다.</strong>
          <span>{describeFailure(failReason)}</span>
          <button
            className="btn btn-primary mes-retry"
            type="button"
            onClick={() => void connectMes().then(() => setFrameKey(k => k + 1))}
          >
            다시 시도
          </button>
        </div>
      )}
    </div>
  );
}
