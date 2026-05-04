import { useState, FormEvent } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { register } from '../../api/auth';
import { validateRegisterForm } from '../../utils/validators';
import type { RegisterErrors } from '../../utils/validators';
import { getApiErrorStatus, isNetworkError } from '../../utils/apiError';
import FormField from '../../components/FormField';
import Button from '../../components/Button';
import ErrorBanner from '../../components/ErrorBanner';
import '../../styles/AuthPage.css';

interface Errors extends RegisterErrors { general?: string; }

export default function RegisterPage() {
    const navigate = useNavigate();
    const [firstName, setFirstName] = useState('');
    const [lastName, setLastName] = useState('');
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [confirmPassword, setConfirmPassword] = useState('');
    const [errors, setErrors] = useState<Errors>({});
    const [loading, setLoading] = useState(false);

    async function handleSubmit(e: FormEvent) {
        e.preventDefault();
        const errs = validateRegisterForm(firstName, lastName, email, password, confirmPassword);
        if (Object.values(errs).some(Boolean)) { setErrors(errs); return; }

        setLoading(true);
        setErrors({});
        try {
            await register({ firstName, lastName, email, password });
            navigate('/login');
        } catch (err: unknown) {
            const status = getApiErrorStatus(err);
            setErrors({ general: isNetworkError(err) ? 'Unable to connect to server. Try again later.' : status === 409 ? 'An account with this email already exists' : 'Something went wrong. Try again.' });
        } finally {
            setLoading(false);
        }
    }

    return (
        <div className="auth-container">
            <div className="auth-card">
                <h1 className="auth-title">Create account</h1>
                <p className="auth-subtitle">US Bank</p>

                <form onSubmit={handleSubmit} noValidate>
                    <ErrorBanner message={errors.general} />
                    <div className="auth-name-row">
                        <FormField id="firstName" label="First name" value={firstName} onChange={setFirstName} error={errors.firstName} placeholder="John" autoComplete="given-name" />
                        <FormField id="lastName" label="Last name" value={lastName} onChange={setLastName} error={errors.lastName} placeholder="Doe" autoComplete="family-name" />
                    </div>
                    <FormField id="email" label="Email" type="email" value={email} onChange={setEmail} error={errors.email} placeholder="you@example.com" autoComplete="email" />
                    <FormField id="password" label="Password" type="password" value={password} onChange={setPassword} error={errors.password} placeholder="••••••••" autoComplete="new-password" />
                    <FormField id="confirmPassword" label="Confirm password" type="password" value={confirmPassword} onChange={setConfirmPassword} error={errors.confirmPassword} placeholder="••••••••" autoComplete="new-password" />
                    <Button loading={loading} loadingText="Creating account…">Create account</Button>
                </form>

                <p className="auth-switch">
                    Already have an account? <Link to="/login">Sign in</Link>
                </p>
            </div>
        </div>
    );
}
