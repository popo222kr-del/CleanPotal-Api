import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useCurrentMesWindow } from '../shell/windowTypes';
import { api } from '../../../api/client';
import PriceImageTab from './PriceImageTab';
import ProcessTab from './ProcessTab';
import ProductSetupTab from './ProductSetupTab';
import '../Mes.css';

// MES 셋업 — 마스터 데이터 관리. 탭은 MES 세부 권한(PermissionCode)에 따라 보이거나 숨는다.
// 권한 판정은 서버가 하고, 쓰기는 서비스 계층에서 한 번 더 막는다.

type Permissions = { isAdmin: boolean; product: boolean; customer: boolean; process: boolean };

type Tab = { key: string; title: string; allowed: (p: Permissions) => boolean };

const TABS: Tab[] = [
  { key: 'product', title: '제품 셋업', allowed: p => p.product },
  { key: 'price', title: '단가/이미지', allowed: p => p.product },
  { key: 'process', title: '공정 관리', allowed: p => p.process },
];

export default function MesSetup() {
  const [params, setParams] = useSearchParams();
  // 창으로 열렸으면 주소를 건드리지 않는다 — 뒤에 떠 있는 화면(OPER 등)의 주소를 덮어써
  // 고르던 LOT 이 주소에서 사라진다. 창 안에서는 탭을 화면 안에서만 기억한다.
  const self = useCurrentMesWindow();
  const [windowTab, setWindowTab] = useState<string | null>(null);
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

  const key = self ? windowTab : params.get('tab');
  const active = allowed.find(t => t.key === key) ?? allowed[0];

  return (
    <div className="mes-page">
      <header className="pg-header"><div><h2>셋업 — {active.title}</h2></div></header>
      <div className="pg-body">
        <div className="mes-tabs">
          {allowed.map(t => (
            <button key={t.key} className={t === active ? 'active' : ''}
                    onClick={() => (self ? setWindowTab(t.key) : setParams({ tab: t.key }, { replace: true }))}>
              {t.title}
            </button>
          ))}
        </div>

        {active.key === 'process' ? <ProcessTab />
          : active.key === 'price' ? <PriceImageTab />
          : <ProductSetupTab />}
      </div>
    </div>
  );
}
