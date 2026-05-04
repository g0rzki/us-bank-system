import './ErrorBanner.css';

interface ErrorBannerProps {
    message?: string;
}

export default function ErrorBanner({ message }: ErrorBannerProps) {
    if (!message) return null;
    return <div className="auth-error-banner">{message}</div>;
}
