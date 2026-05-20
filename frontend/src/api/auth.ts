import client from './client';

export interface LoginRequest {
    email: string;
    password: string;
}

export interface RegisterRequest {
    email: string;
    password: string;
    firstName: string;
    lastName: string;
}

export async function login(data: LoginRequest): Promise<string> {
    const res = await client.post<{ token: string }>('/auth/login', data);
    const { token } = res.data;
    localStorage.setItem('token', token);
    return token;
}

export async function register(data: RegisterRequest): Promise<void> {
    await client.post('/auth/register', data);
}

export function logout(): void {
    localStorage.removeItem('token');
    window.location.href = '/login';
}

export function isAuthenticated(): boolean {
    return !!localStorage.getItem('token');
}

export async function juniorLogin(data: LoginRequest): Promise<void> {
    const res = await client.post<{ token: string }>('/auth/login', data);
    localStorage.setItem('kid_token', res.data.token);
}

export function juniorLogout(): void {
    localStorage.removeItem('kid_token');
    window.location.href = '/login';
}

export function isJuniorAuthenticated(): boolean {
    return !!localStorage.getItem('kid_token');
}
