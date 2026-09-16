import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
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
import Inventory from './pages/Inventory';
import Icpms from './pages/Icpms';
import Vendors from './pages/Vendors';
import Portal from './pages/Portal';
import Dashboard from './pages/Dashboard';
import Users from './pages/Users';

function Protected({ children }: { children: React.ReactNode }) {
  const { user } = useAuth();
  return user ? <>{children}</> : <Navigate to="/login" replace />;
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
            {/* MES 화면을 포털로 옮기는 중이다. 옮긴 화면은 아래에 하나씩 추가하고,
                아직 안 옮긴 화면은 /mes/* 가 받아 MES 를 그대로 띄운다. */}
            <Route path="/mes" element={<MesDashboard />} />
            <Route path="/mes/history" element={<MesLotHistory />} />
            <Route path="/mes/*" element={<Mes />} />
            <Route path="/handover" element={<Handover />} />
            <Route path="/weekly" element={<Handover weekly />} />
            <Route path="/prodreq" element={<ProdReq />} />
            <Route path="/prodreq/options" element={<ProdReqOptions />} />
            <Route path="/meeting" element={<Meeting />} />
            <Route path="/schedule-board" element={<ScheduleBoard />} />
            <Route path="/weekly-report" element={<Weekly />} />
            <Route path="/checklist" element={<Checklist />} />
            <Route path="/broken" element={<Broken />} />
            <Route path="/quotation" element={<Quotation />} />
            <Route path="/product-master" element={<ProductMaster />} />
            <Route path="/notice" element={<Notice />} />
            <Route path="/dispatch" element={<Dispatch />} />
            <Route path="/edu-dashboard" element={<EduDashboard />} />
            <Route path="/work-assignment" element={<WorkAssignment />} />
            <Route path="/inventory" element={<Inventory />} />
            <Route path="/icpms" element={<Icpms />} />
            <Route path="/vendors" element={<Vendors />} />
            <Route path="/portal" element={<Portal />} />
            <Route path="/users" element={<AdminOnly><Users /></AdminOnly>} />
          </Route>
          <Route path="*" element={<Navigate to="/dashboard" replace />} />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  );
}
