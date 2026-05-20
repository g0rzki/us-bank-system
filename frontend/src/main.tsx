import { StrictMode } from 'react';
import './styles/global.css';
import { createRoot } from 'react-dom/client';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import LandingPage from './pages/landing/LandingPage';
import LoginPage from './pages/auth/LoginPage';
import RegisterPage from './pages/auth/RegisterPage';
import DashboardPage from './pages/dashboard/DashboardPage';
import KidPage from './pages/kid/KidPage';
import { isAuthenticated, isJuniorAuthenticated } from './api/auth';

function ProtectedRoute({ children }: { children: React.ReactNode }) {
    return isAuthenticated() ? <>{children}</> : <Navigate to="/login" replace />;
}

function KidProtectedRoute({ children }: { children: React.ReactNode }) {
    return isJuniorAuthenticated() ? <>{children}</> : <Navigate to="/kid/login" replace />;
}

createRoot(document.getElementById('root')!).render(
    <StrictMode>
        <BrowserRouter>
            <Routes>
                <Route path="/" element={<LandingPage />} />
                <Route path="/login" element={<LoginPage />} />
                <Route path="/register" element={<RegisterPage />} />
                <Route path="/dashboard" element={<ProtectedRoute><DashboardPage /></ProtectedRoute>} />
                <Route path="/kid/login" element={<Navigate to="/login" replace />} />
                <Route path="/kid" element={<KidProtectedRoute><KidPage /></KidProtectedRoute>} />
            </Routes>
        </BrowserRouter>
    </StrictMode>
);
