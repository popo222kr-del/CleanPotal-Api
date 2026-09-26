import { BrowserRouter, Routes, Route, Navigate, useLocation } from 'react-router-dom';
import { AuthProvider, useAuth } from './auth/AuthContext';
import Layout from './components/Layout';
import ErrorBoundary from './components/ErrorBoundary';
import Login from './pages/Login';
import Roster from './pages/Roster';
import Material from './pages/Material';
import Calendar from './pages/Calendar';
import Mes from './pages/Mes';
import MesDashboard from './pages/mes/Dashboard';
import MesLotHistory from './pages/mes/LotHistory';
import MesBatch from './pages/mes/Batch';
import MesCertificates from './pages/mes/Certificates';
import MesCleaningHistory from './pages/mes/CleaningHistory';
import MesHistoryVoid from './pages/mes/HistoryVoid';
import MesHolds from './pages/mes/Holds';
import MesLotInOut from './pages/mes/LotInOut';
import MesOper from './pages/mes/Oper';
import MesReworks from './pages/mes/Reworks';
import MesSetup from './pages/mes/setup/Setup';
import MesRegister from './pages/mes/Register';
import MesScan from './pages/mes/Scan';
import MesTat from './pages/mes/Tat';
import MesShell from './pages/mes/shell/MesShell';
import Handover from './pages/Handover';
import ProdReq from './pages/ProdReq';
import ProdReqOptions from './pages/ProdReqOptions';
import Weekly from './pages/Weekly';
import Meeting from './pages/Meeting';
import ScheduleBoard from './pages/ScheduleBoard';
import Checklist from './pages/Checklist';
import Broken from './pages/Broken';
import Quotation from './pages/Quotation';
import ProductMaster from './pages/ProductMaster';
import Notice from './pages/Notice';
import Dispatch from './pages/Dispatch';
import EduDashboard from './pages/EduDashboard';
import WorkAssignment from './pages/WorkAssignment';
import TempHumidity from './pages/TempHumidity';
import Inventory from './pages/Inventory';
import Icpms from './pages/Icpms';
import Vendors from './pages/Vendors';
import Portal from './pages/Portal';
import Dashboard from './pages/Dashboard';
import Users from './pages/Users';
import Holidays from './pages/Holidays';
import CheckZone from './pages/checklist/CheckZone';

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
