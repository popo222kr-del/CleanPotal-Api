import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider, useAuth } from './auth/AuthContext';
import Layout from './components/Layout';
import Login from './pages/Login';
import Roster from './pages/Roster';

function Protected({ children }: { children: React.ReactNode }) {
  const { user } = useAuth();
  return user ? <>{children}</> : <Navigate to="/login" replace />;
}

function Placeholder({ title }: { title: string }) {
  return (
    <div style={{ padding: 40 }}>
      <h2 style={{ fontSize: 18, fontWeight: 800 }}>{title}</h2>
      <p style={{ fontSize: 13, color: 'var(--text-mid)', marginTop: 8 }}>이 화면은 다음 단계에서 구현됩니다.</p>
    </div>
  );
}

export default function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Routes>
          <Route path="/login" element={<Login />} />
          <Route element={<Protected><Layout /></Protected>}>
            <Route path="/roster" element={<Roster />} />
            <Route path="/handover" element={<Placeholder title="인수인계" />} />
            <Route path="/portal" element={<Placeholder title="업무 파일 통합 관리" />} />
          </Route>
          <Route path="*" element={<Navigate to="/roster" replace />} />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  );
}
