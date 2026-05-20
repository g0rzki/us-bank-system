import axios from 'axios';

const kidClient = axios.create({
    baseURL: import.meta.env.VITE_API_URL ?? 'http://localhost:5000',
});

kidClient.interceptors.request.use(config => {
    const token = localStorage.getItem('kid_token');
    if (token) {
        config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
});

kidClient.interceptors.response.use(
    response => response,
    error => {
        if (error.response?.status === 401 && localStorage.getItem('kid_token')) {
            localStorage.removeItem('kid_token');
            window.location.href = '/login';
        }
        return Promise.reject(error);
    }
);

export default kidClient;
