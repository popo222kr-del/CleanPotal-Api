import { BrowserRouter, Routes, Route, Navigate, useLocation } from 'react-router-dom';
import { AuthProvider, useAuth } from './auth/AuthContext';
import Layout from './components/Layout';
import ErrorBoundary from './components/ErrorBoundary';
import { lazyPage } from './lazyPage';
import Login from './pages/Login';
const Roster = lazyPage(() => import('./pages/Roster'));
const Material = lazyPage(() => import('./pages/Material'));
const Calendar = lazyPage(() => import('./pages/Calendar'));
const Mes = lazyPage(() => import('./pages/Mes'));
const MesDashboard = lazyPage(() => import('./pages/mes/Dashboard'));
const MesLotHistory = lazyPage(() => import('./pages/mes/LotHistory'));
const MesBatch = lazyPage(() => import('./pages/mes/Batch'));
const MesCertificates = lazyPage(() => import('./pages/mes/Certificates'));
const MesCleaningHistory = lazyPage(() => import('./pages/mes/CleaningHistory'));
const MesHistoryVoid = lazyPage(() => import('./pages/mes/HistoryVoid'));
const MesHolds = lazyPage(() => import('./pages/mes/Holds'));
const MesLotInOut = lazyPage(() => import('./pages/mes/LotInOut'));
const MesOper = lazyPage(() => import('./pages/mes/Oper'));
const MesReworks = lazyPage(() => import('./pages/mes/Reworks'));
const MesSetup = lazyPage(() => import('./pages/mes/setup/Setup'));
const MesRegister = lazyPage(() => import('./pages/mes/Register'));
const MesScan = lazyPage(() => import('./pages/mes/Scan'));
const MesTat = lazyPage(() => import('./pages/mes/Tat'));
const MesShell = lazyPage(() => import('./pages/mes/shell/MesShell'));
const Handover = lazyPage(() => import('./pages/Handover'));
const ProdReq = lazyPage(() => import('./pages/ProdReq'));
const ProdReqOptions = lazyPage(() => import('./pages/ProdReqOptions'));
const Weekly = lazyPage(() => import('./pages/Weekly'));
const Meeting = lazyPage(() => import('./pages/Meeting'));
const ScheduleBoard = lazyPage(() => import('./pages/ScheduleBoard'));
const Checklist = lazyPage(() => import('./pages/Checklist'));
const Broken = lazyPage(() => import('./pages/Broken'));
const Quotation = lazyPage(() => import('./pages/Quotation'));
const ProductMaster = lazyPage(() => import('./pages/ProductMaster'));
const Notice = lazyPage(() => import('./pages/Notice'));
const Dispatch = lazyPage(() => import('./pages/Dispatch'));
const EduDashboard = lazyPage(() => import('./pages/EduDashboard'));
const WorkAssignment = lazyPage(() => import('./pages/WorkAssignment'));
const TempHumidity = lazyPage(() => import('./pages/TempHumidity'));
const Inventory = lazyPage(() => import('./pages/Inventory'));
const Icpms = lazyPage(() => import('./pages/Icpms'));
const Vendors = lazyPage(() => import('./pages/Vendors'));
const Portal = lazyPage(() => import('./pages/Portal'));
const Dashboard = lazyPage(() => import('./pages/Dashboard'));
const Users = lazyPage(() => import('./pages/Users'));
const Holidays = lazyPage(() => import('./pages/Holidays'));
const CheckZone = lazyPage(() => import('./pages/checklist/CheckZone'));

function Protected({ children }: { children: React.ReactNode }) {
  const { user } = useAuth();
  const loc = useLocation();
  // QR 로 들어왔는데 로그인이 안 돼 있으면, 로그인 뒤 그 구역 화면으로 돌아오게 주소를 들고 간다.
  return user ? <>{children}</> : <Navigate to="/login" replace state={{ from: loc.pathname + loc.search }} />;
}

function AdminOnly({ children }: { children: React.ReactNode }) {
  const { user } = useAuth();
  return user?.isAdmin ? <>{children}</> : <Navigate to="/dashboard" replace />;
}

export default function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Routes>
          <Route path="/login" element={<Login />} />
          <Route element={<Protected><ErrorBoundary><Layout /></ErrorBoundary></Protected>}>
            <Route path="/dashboard" element={<Dashboard />} />
            <Route path="/calendar" element={<Calendar />} />
            <Route path="/status/material" element={<Material />} />
            <Route path="/roster" element={<Roster />} />
            {/* MES — 데스크톱 MES Client 처럼 상단 메뉴바를 두고 화면을 창으로 띄운다(MesShell).
                사이드바에 남는 것은 Dash Board 와 OPER 뿐이고, 나머지는 메뉴에서 창으로 연다.
                아래 주소들도 그대로 살려 둔다 — 즐겨찾기·예전 링크로 들어오면 그 화면이 통째로 열린다. */}
            <Route element={<MesShell />}>
              <Route path="/mes" element={<MesDashboard />} />
              <Route path="/mes/oper/:operCode" element={<MesOper />} />
              <Route path="/mes/scan" element={<MesScan />} />
              <Route path="/mes/register" element={<MesRegister />} />
              <Route path="/mes/batch" element={<MesBatch />} />
              <Route path="/mes/lot-inout" element={<MesLotInOut />} />
              <Route path="/mes/cleaning-history" element={<MesCleaningHistory />} />
              <Route path="/mes/setup" element={<MesSetup />} />
              <Route path="/mes/holds" element={<MesHolds />} />
              <Route path="/mes/reworks" element={<MesReworks />} />
              <Route path="/mes/certificates" element={<MesCertificates />} />
              <Route path="/mes/history-void" element={<MesHistoryVoid />} />
              <Route path="/mes/history" element={<MesLotHistory />} />
              <Route path="/mes/tat" element={<MesTat />} />
            </Route>
            {/* 아직 안 옮긴 주소는 기존 MES 를 그대로 띄운다(셸 밖 — 통째로 iframe 이다). */}
            <Route path="/mes/*" element={<Mes />} />
            {/* 두 메뉴가 같은 화면을 쓴다 — key 를 달리해 필터·선택·검색이 서로 넘어가지 않게 따로 만든다 */}
            <Route path="/handover" element={<Handover key="handover" />} />
            <Route path="/weekly" element={<Handover key="weekly" weekly />} />
            <Route path="/prodreq" element={<ProdReq />} />
            <Route path="/prodreq/options" element={<ProdReqOptions />} />
            <Route path="/meeting" element={<Meeting />} />
            <Route path="/schedule-board" element={<ScheduleBoard />} />
            <Route path="/weekly-report" element={<Weekly />} />
            <Route path="/checklist" element={<Checklist />} />
            {/* 구역 QR 이 가리키는 주소 — http://서버/c/M-OUT */}
            <Route path="/c/:code" element={<CheckZone />} />
            <Route path="/broken" element={<Broken />} />
            <Route path="/quotation" element={<Quotation />} />
            <Route path="/product-master" element={<ProductMaster />} />
            <Route path="/notice" element={<Notice />} />
            <Route path="/dispatch" element={<Dispatch />} />
            <Route path="/edu-dashboard" element={<EduDashboard />} />
            <Route path="/work-assignment" element={<WorkAssignment />} />
            <Route path="/temp-humidity" element={<TempHumidity />} />
            <Route path="/inventory" element={<Inventory />} />
            <Route path="/icpms" element={<Icpms />} />
            <Route path="/vendors" element={<Vendors />} />
            <Route path="/portal" element={<Portal />} />
            <Route path="/users" element={<AdminOnly><Users /></AdminOnly>} />
            <Route path="/holidays" element={<AdminOnly><Holidays /></AdminOnly>} />
          </Route>
          <Route path="*" element={<Navigate to="/dashboard" replace />} />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  );
}
