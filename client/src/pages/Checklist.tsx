import { useState } from 'react';
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
  const [tab, setTab] = useState<Tab>('status');
  const tabs: [Tab, string][] = [['status', '점검 현황'], ['ng', '미조치 NG'], ['report', '월간 리포트']];
  if (acc.isAdmin) tabs.push(['admin', '양식 관리']);

  return (
    <div>
      <header className="pg-header ck-noprint">
        <div>
          <h2>QR 체크시트</h2>
          <p>3정 5S 점검 — 구역마다 붙인 QR 을 휴대폰으로 찍어 점검합니다. 여기서는 현황·NG·월간 리포트를 봅니다.</p>
        </div>
      </header>
      <div className="pg-body">
        <div className="ck-tabs ck-noprint">
          {tabs.map(([t, l]) => (
            <button key={t} className={`ck-tab ${tab === t ? 'on' : ''}`} onClick={() => setTab(t)}>{l}</button>
          ))}
        </div>
        {tab === 'status' && <StatusTab onOpenNg={() => setTab('ng')} />}
        {tab === 'ng' && <NgTab />}
        {tab === 'report' && <ReportTab />}
        {tab === 'admin' && acc.isAdmin && <AdminTab />}
      </div>
    </div>
  );
}
