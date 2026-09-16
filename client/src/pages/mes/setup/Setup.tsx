import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { api } from '../../../api/client';
import Mes from '../../Mes';
import CustomerTab from './CustomerTab';
import PriceImageTab from './PriceImageTab';
import ProcessTab from './ProcessTab';
import '../Mes.css';

// MES 셋업 — 마스터 데이터 관리. 탭은 MES 세부 권한(PermissionCode)에 따라 보이거나 숨는다.
// 권한 판정은 서버가 하고, 쓰기는 서비스 계층에서 한 번 더 막는다.

type Permissions = { isAdmin: boolean; product: boolean; customer: boolean; process: boolean };

type Tab = { key: string; title: string; allowed: (p: Permissions) => boolean };

const TABS: Tab[] = [
  { key: 'product', title: '제품 셋업', allowed: p => p.product },
  { key: 'price', title: '단가/이미지', allowed: p => p.product },
  { key: 'customer', title: '업체 관리', allowed: p => p.customer },
  { key: 'process', title: '공정 관리', allowed: p => p.process },
];

export default function MesSetup() {
  const [params, setParams] = useSearchParams();
  const [perms, setPerms] = useState<Permissions | null>(null);

  useEffect(() => {
    void (async () => {
      try { setPerms(await api.get<Permissions>('/api/mes/setup/permissions')); }
      catch { setPerms({ isAdmin: false, product: false, customer: false, process: false }); }
    })();
  }, []);

  if (!perms) return <div className="mes-page"><div className="pg-body"><p className="mes-dim">권한 확인 중…</p></div></div>;

  const allowed = TABS.filter(t => t.allowed(perms));
  if (allowed.length === 0) {
    return (
      <div className="mes-page">
        <header className="pg-header"><div><h2>셋업</h2></div></header>
        <div className="pg-body">
          <p className="mes-alert warn">이 계정에는 셋업(마스터 관리) 권한이 없습니다. 관리자에게 권한 부여를 요청하세요.</p>
        </div>
      </div>
    );
  }

  const key = params.get('tab');
  const active = allowed.find(t => t.key === key) ?? allowed[0];

  return (
    <div className="mes-page">
      <header className="pg-header"><div><h2>셋업 — {active.title}</h2></div></header>
      <div className="pg-body">
        <div className="mes-tabs">
          {allowed.map(t => (
            <button key={t.key} className={t === active ? 'active' : ''}
                    onClick={() => setParams({ tab: t.key }, { replace: true })}>
              {t.title}
            </button>
          ))}
        </div>

        {/* 아직 안 옮긴 탭은 기존 MES 화면을 그대로 띄운다 — 옮기는 중이라고 비워 두면 그 사이에
            쓰던 기능이 사라진다. 옮기는 대로 여기에 한 줄씩 추가된다. */}
        {active.key === 'customer' ? <CustomerTab />
          : active.key === 'process' ? <ProcessTab />
          : active.key === 'price' ? <PriceImageTab />
          : <Mes path={`setup?tab=${active.key}`} />}
      </div>
    </div>
  );
}
