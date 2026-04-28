import client from './client';

export const register = (data: { email: string; password: string; firstName: string; lastName: string }) =>
    client.post('/auth/register', data);

export const login = async (data: { email: string; password: string }) => {
    const res = await client.post('/auth/login', data);
    localStorage.setItem('token', res.data.token);
    return res;
};

export const logout = () => {
    localStorage.removeItem('token');
    window.location.href = '/login';
};
