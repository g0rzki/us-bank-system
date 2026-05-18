import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { juniorLogin } from '../../api/auth';
import { useDarkMode } from '../../utils/useDarkMode';
import FormField from '../../components/FormField';
import Button from '../../components/Button';
import '../../styles/AuthPage.css';
import '../landing/LandingPage.css';

export default function KidLoginPage() {
    const navigate = useNavigate();
    const { dark, toggle } = useDarkMode();
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [error, setError] = useState<string | null>(null);
    const [loading, setLoading] = useState(false);

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setError(null);
        if (!email || !password) {
            setError('Enter email and password');
            return;
        }
        setLoading(true);
        try {
            await juniorLogin({ email, password });
            navigate('/kid');
        } catch {
            setError('Invalid email or password');
        } finally {
            setLoading(false);
        }
    };

    return (
        <div className="auth-container" style={{ '--color-primary': '#16a34a', '--color-primary-hover': '#15803d', '--color-text-link': '#16a34a' } as React.CSSProperties}>
            <button className="landing-theme-toggle" onClick={toggle}>
                {dark ? '☀️' : '🌙'}
            </button>
            <div className="auth-card">
                <h1 className="auth-title">Junior account</h1>
                <p className="auth-subtitle">US Bank</p>
                <form onSubmit={handleSubmit} noValidate>
                    {error && <p style={{ color: 'var(--color-error)', fontSize: '0.875rem', background: 'var(--color-error-bg)', border: '1px solid var(--color-error-border)', borderRadius: '8px', padding: '8px 16px', marginBottom: '12px' }}>{error}</p>}
                    <FormField id="email" label="Email" type="email" value={email} onChange={setEmail} placeholder="your@email.com" autoComplete="username" />
                    <FormField id="password" label="Password" type="password" value={password} onChange={setPassword} placeholder="Your password" autoComplete="current-password" />
                    <Button loading={loading} loadingText="Signing in…">Sign in</Button>
                </form>
            </div>
        </div>
    );
}
