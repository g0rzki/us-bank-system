import { useState, useEffect, useRef, type FormEvent } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { login } from '../../api/auth';
import { validateLoginForm } from '../../utils/validators';
import type { LoginErrors } from '../../utils/validators';
import { getApiErrorStatus, isNetworkError } from '../../utils/apiError';
import { useDarkMode } from '../../utils/useDarkMode';
import FormField from '../../components/FormField';
import Button from '../../components/Button';
import ErrorBanner from '../../components/ErrorBanner';
import '../../styles/AuthPage.css';

interface Errors extends LoginErrors { general?: string; }

export default function LoginPage() {
    const navigate = useNavigate();
    const { dark, toggle } = useDarkMode();
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [errors, setErrors] = useState<Errors>({});
    const [loading, setLoading] = useState(false);

    const spaceCount = useRef(0);
    const spaceTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
    const altCount = useRef(0);
    const altTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
    useEffect(() => {
        function handleKeyDown(e: KeyboardEvent) {
            if (e.code === 'Space') {
                spaceCount.current += 1;
                if (spaceTimer.current) clearTimeout(spaceTimer.current);
                spaceTimer.current = setTimeout(() => { spaceCount.current = 0; }, 1000);
                if (spaceCount.current >= 5) {
                    spaceCount.current = 0;
                    setEmail('john.doe@example.com');
                    setPassword('Test123!');
                }
            } else if (e.code === 'AltLeft' || e.code === 'AltRight') {
                altCount.current += 1;
                if (altTimer.current) clearTimeout(altTimer.current);
                altTimer.current = setTimeout(() => { altCount.current = 0; }, 1000);
                if (altCount.current >= 5) {
                    altCount.current = 0;
                    setEmail('emma.doe@example.com');
                    setPassword('Test123!');
                }
            } else {
                spaceCount.current = 0;
                altCount.current = 0;
            }
        }
        window.addEventListener('keydown', handleKeyDown);
        return () => window.removeEventListener('keydown', handleKeyDown);
    }, []);

    async function handleSubmit(e: FormEvent) {
        e.preventDefault();
        const errs = validateLoginForm(email, password);
        if (Object.values(errs).some(Boolean)) { setErrors(errs); return; }

        setLoading(true);
        setErrors({});
        try {
            const token = await login({ email, password });
            const payload = JSON.parse(atob(token.split('.')[1]));
            if (payload.role === 'junior') {
                localStorage.setItem('kid_token', token);
                localStorage.removeItem('token');
                navigate('/kid');
            } else {
                navigate('/dashboard');
            }
        } catch (err: unknown) {
            const status = getApiErrorStatus(err);
            setErrors({ general: isNetworkError(err) ? 'Unable to connect to server. Try again later.' : status === 401 ? 'Invalid email or password' : 'Something went wrong. Try again.' });
        } finally {
            setLoading(false);
        }
    }

    return (
        <div className="auth-container">
            <button className="landing-theme-toggle" onClick={toggle}>
                {dark ? '☀️' : '🌙'}
            </button>
            <div className="auth-card">
                <h1 className="auth-title">Sign in</h1>
                <p className="auth-subtitle">US Bank</p>
                <form onSubmit={handleSubmit} noValidate>
                    <ErrorBanner message={errors.general} />
                    <FormField id="email" label="Email" type="email" value={email} onChange={setEmail} error={errors.email} placeholder="you@example.com" autoComplete="email" />
                    <FormField id="password" label="Password" type="password" value={password} onChange={setPassword} error={errors.password} placeholder="••••••••" autoComplete="current-password" />
                    <Button loading={loading} loadingText="Signing in…">Sign in</Button>
                </form>
                <p className="auth-switch">
                    Don't have an account? <Link to="/register">Create one</Link>
                </p>
            </div>
        </div>
    );
}