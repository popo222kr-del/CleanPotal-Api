import { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useAccess } from '../auth/useAccess';
import StatusTab from './checklist/StatusTab';
import NgTab from './checklist/NgTab';
import ReportTab from './checklist/ReportTab';
import AdminTab from './checklist/AdminTab';
import './checklist/Checklist.css';

// QR 체크시트 — 사무실에서 보는 곳. 현장은 구역 QR 로 /c/구역코드 화면에 바로 들어온다.
type Tab = 'status' | 'ng' | 'report' | 'admin';

export default function Checklist() {
  const acc = useAccess();
  // 대시보드 알림(미조치 NG)에서 ?tab=ng 로 바로 NG 탭을 연다.
  const [params] = useSearchParams();
  const [tab, setTab] = useState<Tab>(() => (['ng', 'report'].includes(params.get('tab') ?? '') ? params.get('tab') as Tab : 'status'));
  const tabs: [Tab, string][] = [['status', '점검 현황'], ['ng', 'NG 관리'], ['report', '월간 리포트']];
  if (acc.isAdmin) tabs.push(['admin', '양식 관리']);

  return (
    <div className="ck-page">
      <header className="ck-head ck-noprint">
        <div className="ck-head-title">
          <h2>QR 체크시트</h2>
          <span>3정 5S 점검</span>
        </div>
        <nav className="ck-nav">
          {tabs.map(([t, l]) => (
            <button key={t} className={tab === t ? 'on' : ''} onClick={() => setTab(t)}>{l}</button>
          ))}
        </nav>
      </header>
      <div className="ck-body">
        {tab === 'status' && <StatusTab onOpenNg={() => setTab('ng')} />}
        {tab === 'ng' && <NgTab />}
        {tab === 'report' && <ReportTab />}
        {tab === 'admin' && acc.isAdmin && <AdminTab />}
      </div>
    </div>
  );
}
