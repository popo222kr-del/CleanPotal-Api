import { Suspense } from 'react';
import { lazyPage } from '../../../lazyPage';
const MesBatch = lazyPage(() => import('../Batch'));
const MesCertificates = lazyPage(() => import('../Certificates'));
const MesCleaningHistory = lazyPage(() => import('../CleaningHistory'));
const MesHistoryVoid = lazyPage(() => import('../HistoryVoid'));
const MesHolds = lazyPage(() => import('../Holds'));
const MesLotHistory = lazyPage(() => import('../LotHistory'));
const MesLotInOut = lazyPage(() => import('../LotInOut'));
const MesRegister = lazyPage(() => import('../Register'));
const MesReworks = lazyPage(() => import('../Reworks'));
const MesScan = lazyPage(() => import('../Scan'));
const MesTat = lazyPage(() => import('../Tat'));
const MesSetup = lazyPage(() => import('../setup/Setup'));
import type { MesWindowArgs, MesWindowKey } from './windowTypes';

/**
 * 창으로 열 수 있는 화면 목록. 화면 자체는 이미 있는 것을 그대로 쓴다 —
 * 창으로 띄우든 주소로 들어오든 같은 화면이어야 한다.
 */
export const MES_WINDOW_TITLES: Record<MesWindowKey, string> = {
  scan: 'LOT 스캔',
  register: 'CREATE (전산등록)',
  batch: 'Batch',
  'history-void': '이력 삭제',
  history: 'LOT 현황 조회',
  'cleaning-history': '세정 이력 조회',
  'lot-inout': '입 · 출고 현황 조회',
  tat: 'TAT 조회',
  certificates: '성적서 조회',
  holds: 'HOLD 관리',
  reworks: '재작업 관리',
  setup: '셋업',
};

// 창 화면은 창을 열 때 받아 온다 — 포털 틀(Layout)이 이 목록을 쓰므로, 예전에는 MES 를 안 써도
// 첫 화면에 MES 화면 전부와 엑셀 도구(입·출고 현황)가 딸려 왔다.
export function renderMesWindow(key: MesWindowKey, args: MesWindowArgs) {
  return <Suspense fallback={<div className="page-loading">불러오는 중…</div>}>{renderInner(key, args)}</Suspense>;
}

function renderInner(key: MesWindowKey, args: MesWindowArgs) {
  switch (key) {
    case 'scan': return <MesScan />;
    case 'register': return <MesRegister />;
    case 'batch': return <MesBatch />;
    case 'history-void': return <MesHistoryVoid />;
    // LOT 번호를 들고 열리면(목록에서 눌러 들어오는 길) 바로 그 LOT 을 조회한다.
    case 'history': return <MesLotHistory initialKeyword={args.lot} />;
    case 'cleaning-history': return <MesCleaningHistory />;
    case 'lot-inout': return <MesLotInOut />;
    case 'tat': return <MesTat />;
    case 'certificates': return <MesCertificates />;
    case 'holds': return <MesHolds />;
    case 'reworks': return <MesReworks />;
    case 'setup': return <MesSetup />;
  }
}

/** 상단 메뉴 구성 — 데스크톱 MES 의 메뉴바 자리다. 포털 말(등록·조회·설정)로 적는다. */
export const MES_MENUS: { label: string; items: MesWindowKey[] }[] = [
  { label: '등록', items: ['register', 'batch', 'history-void'] },
  { label: '조회', items: ['scan', 'history', 'cleaning-history', 'lot-inout', 'tat', 'certificates', 'holds', 'reworks'] },
  { label: '설정', items: ['setup'] },
];
