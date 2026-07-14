import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider, useAuth } from './auth/AuthContext';
import Layout from './components/Layout';
import Login from './pages/Login';
import Roster from './pages/Roster';
import Material from './pages/Material';
import Calendar from './pages/Calendar';
import Handover from './pages/Handover';
import ProdReq from './pages/ProdReq';
import ReportPage from './pages/ReportPage';
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
import Vendors from './pages/Vendors';
import Portal from './pages/Portal';
import Users from './pages/Users';

function Protected({ children }: { children: React.ReactNode }) {
  const { user } = useAuth();
  return user ? <>{children}</> : <Navigate to="/login" replace />;
}

function AdminOnly({ children }: { children: React.ReactNode }) {
  const { user } = useAuth();
  return user?.isAdmin ? <>{children}</> : <Navigate to="/portal" replace />;
}

export default function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Routes>
          <Route path="/login" element={<Login />} />
          <Route element={<Protected><Layout /></Protected>}>
            <Route path="/calendar" element={<Calendar />} />
            <Route path="/status/material" element={<Material />} />
            <Route path="/roster" element={<Roster />} />
            <Route path="/handover" element={<Handover />} />
            <Route path="/weekly" element={<Handover weekly />} />
            <Route path="/prodreq" element={<ProdReq />} />
            <Route path="/meeting" element={<Meeting />} />
            <Route path="/schedule-board" element={<ScheduleBoard />} />
            <Route path="/weekly-report" element={<ReportPage kind="weekly" />} />
            <Route path="/checklist" element={<Checklist />} />
            <Route path="/broken" element={<Broken />} />
            <Route path="/quotation" element={<Quotation />} />
            <Route path="/product-master" element={<ProductMaster />} />
            <Route path="/notice" element={<Notice />} />
            <Route path="/dispatch" element={<Dispatch />} />
            <Route path="/edu-dashboard" element={<EduDashboard />} />
            <Route path="/work-assignment" element={<WorkAssignment />} />
            <Route path="/inventory" element={<Inventory />} />
            <Route path="/vendors" element={<Vendors />} />
            <Route path="/portal" element={<Portal />} />
            <Route path="/users" element={<AdminOnly><Users /></AdminOnly>} />
          </Route>
          <Route path="*" element={<Navigate to="/portal" replace />} />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  );
}
