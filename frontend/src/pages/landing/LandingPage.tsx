import { Link } from 'react-router-dom';
import { useDarkMode } from '../../utils/useDarkMode';
import './LandingPage.css';

export default function LandingPage() {
    const { dark, toggle } = useDarkMode();

    return (
        <div className="landing-container">
            <button className="landing-theme-toggle" onClick={toggle}>
                {dark ? '☀️' : '🌙'}
            </button>
            <div className="landing-box">
                <h1>US Bank</h1>
                <p>Secure online banking</p>
                <div className="landing-actions">
                    <Link to="/login" className="landing-btn-primary">Sign in</Link>
                    <Link to="/register" className="landing-btn-secondary">Create account</Link>
                </div>
            </div>
        </div>
    );
}