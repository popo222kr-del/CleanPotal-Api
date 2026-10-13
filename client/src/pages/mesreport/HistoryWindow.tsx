import { useEffect } from 'react';
import { useSearchParams } from 'react-router-dom';
import { HistoryTab } from './MesWip';
import NotConnected from './NotConnected';
import { useLegacyStatus } from './mesReport';
import './MesReport.css';

// 세정 이력 조회 새 창 — 입·출고 현황의 [새 창으로 보기]. 사이드바 없이 표를 화면 가득. 조건은 주소에서 받아 바로 조회.
export default function HistoryWindow() {
  const [sp] = useSearchParams();
  const scope = sp.get('scope') === 'other' ? 'other' : 'samsung';
  const title = `세정 이력 조회 · ${scope === 'other' ? '기타' : '삼성'}`;
  const status = useLegacyStatus();
  useEffect(() => { document.title = title; }, [title]);
  if (status === undefined) return <div className="page-loading">불러오는 중…</div>;
  if (!status?.configured) return <NotConnected title={title} />;
  return (
    <div className="mr-window mr-hist-window">
      <div className="mr-win-title"><b>{title}</b><button type="button" className="btn btn-ghost" onClick={() => window.close()}>닫기</button></div>
      <HistoryTab scope={scope} init={sp} win />
    </div>
  );
}
