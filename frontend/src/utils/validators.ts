export function validateEmail(email: string): string | undefined {
    if (!email) return 'Email is required';
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) return 'Enter a valid email';
}

export function validatePassword(password: string): string | undefined {
    if (!password) return 'Password is required';
    if (password.length < 8) return 'Password must be at least 8 characters';
    if (!/[A-Z]/.test(password)) return 'Password must contain an uppercase letter';
    if (!/[0-9]/.test(password)) return 'Password must contain a number';
}

export function validateRequired(value: string, fieldName: string): string | undefined {
    if (!value.trim()) return `${fieldName} is required`;
}

export function validateConfirmPassword(password: string, confirmPassword: string): string | undefined {
    if (!confirmPassword) return 'Please confirm your password';
    if (confirmPassword !== password) return 'Passwords do not match';
}

export interface LoginErrors {
    email?: string;
    password?: string;
}

export interface RegisterErrors {
    firstName?: string;
    lastName?: string;
    email?: string;
    password?: string;
    confirmPassword?: string;
}

export function validateLoginForm(email: string, password: string): LoginErrors {
    return {
        email: validateEmail(email),
        password: !password ? 'Password is required' : undefined,
    };
}

export function validateRegisterForm(firstName: string, lastName: string, email: string, password: string, confirmPassword: string): RegisterErrors {
    return {
        firstName: validateRequired(firstName, 'First name'),
        lastName: validateRequired(lastName, 'Last name'),
        email: validateEmail(email),
        password: validatePassword(password),
        confirmPassword: validateConfirmPassword(password, confirmPassword),
    };
}
