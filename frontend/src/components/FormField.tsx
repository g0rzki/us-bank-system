import './FormField.css';

interface FormFieldProps {
    id: string;
    label: string;
    type?: string;
    value: string;
    onChange: (value: string) => void;
    error?: string;
    placeholder?: string;
    autoComplete?: string;
}

export default function FormField({ id, label, type = 'text', value, onChange, error, placeholder, autoComplete }: FormFieldProps) {
    return (
        <div className="auth-field">
            <label htmlFor={id}>{label}</label>
            <input
                id={id}
                type={type}
                value={value}
                onChange={e => onChange(e.target.value)}
                className={error ? 'error' : ''}
                placeholder={placeholder}
                autoComplete={autoComplete}
            />
            {error && <span className="auth-field-error">{error}</span>}
        </div>
    );
}
