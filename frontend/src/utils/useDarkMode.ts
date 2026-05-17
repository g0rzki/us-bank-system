import { useState, useEffect } from 'react';

export function useDarkMode() {
    const [dark, setDark] = useState(() => {
        const saved = localStorage.getItem('theme');
        return saved ? saved === 'dark' : true;
    });

    useEffect(() => {
        document.documentElement.setAttribute('data-theme', dark ? 'dark' : 'light');
    }, [dark]);

    const toggle = () => {
        const next = !dark;
        localStorage.setItem('theme', next ? 'dark' : 'light');
        setDark(next);
    };

    return { dark, toggle };
}