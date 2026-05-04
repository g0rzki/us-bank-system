import './Button.css';

interface ButtonProps {
    children: React.ReactNode;
    loading?: boolean;
    loadingText?: string;
    disabled?: boolean;
    type?: 'submit' | 'button';
    onClick?: () => void;
    className?: string;
}

export default function Button({ children, loading, loadingText, disabled, type = 'submit', onClick, className = 'auth-btn' }: ButtonProps) {
    return (
        <button type={type} className={className} disabled={disabled || loading} onClick={onClick}>
            {loading && loadingText ? loadingText : children}
        </button>
    );
}
