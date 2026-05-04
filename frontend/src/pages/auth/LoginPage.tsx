import { useState, FormEvent } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { login } from '../../api/auth';
import { validateLoginForm, LoginErrors } from '../../utils/validators';
import { getApiErrorStatus } from '../../utils/apiError';
import FormField from '../../components/FormField';
import Button from '../../components/Button';
import ErrorBanner from '../../components/ErrorBanner';
import '../../styles/AuthPage.css';

interface Errors extends LoginErrors { general?: string; }

export default function LoginPage() {
    const navigate = useNavigate();
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [errors, setErrors] = useState<Errors>({});
    const [loading, setLoading] = useState(false);

    async function handleSubmit(e: FormEvent) {
        e.preventDefault();
        const errs = validateLoginForm(email, password);
        if (Object.keys(errs).length > 0) { setErrors(errs); return; }

        setLoading(true);
        setErrors({});
        try {
            await login({ email, password });
            navigate('/dashboard');
        } catch (err: unknown) {
            const status = getApiErrorStatus(err);
            setErrors({ general: status === 401 ? 'Invalid email or password' : 'Something went wrong. Try again.' });
        } finally {
            setLoading(false);
        }
    }

    return (
        <div className="auth-container">
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
