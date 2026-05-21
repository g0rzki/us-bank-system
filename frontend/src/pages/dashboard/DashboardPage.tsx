import { useState, useEffect } from 'react';
import { logout } from '../../api/auth';
import { getAccounts, getTransactions } from '../../api/accounts';
import type { Account, Transaction } from '../../api/accounts';
import OverviewView from './views/OverviewView';
import AccountsView from './views/AccountsView';
import TransfersView from './views/TransfersView';
import HistoryView from './views/HistoryView';
import SettingsView from './views/SettingsView';
import './DashboardPage.css';
import { useDarkMode } from '../../utils/useDarkMode';

export type View = 'overview' | 'accounts' | 'transfers' | 'history' | 'settings';

const NAV_ITEMS: { id: View; label: string }[] = [
    { id: 'overview', label: 'Overview' },
    { id: 'accounts', label: 'Accounts' },
    { id: 'transfers', label: 'Transfers' },
    { id: 'history', label: 'History' },
    { id: 'settings', label: 'Settings' },
];

function getFirstNameFromToken(): string {
    const token = localStorage.getItem('token');
    if (!token) return '';
    try {
        const payload = JSON.parse(atob(token.split('.')[1]));
        return payload['given_name'] ?? payload['firstName'] ?? '';
    } catch {
        return '';
    }
}

export default function DashboardPage() {
    const [view, setView] = useState<View>('overview');
    const [accounts, setAccounts] = useState<Account[]>([]);
    const [transactions, setTransactions] = useState<Transaction[]>([]);
    const [loading, setLoading] = useState(true);
    const { dark, toggle } = useDarkMode();
    const firstName = getFirstNameFromToken();

    useEffect(() => {
        getAccounts().then(async data => {
            setAccounts(data);
            if (data.length > 0) {
                const tx = await getTransactions(data[0].id, 1, 10);
                setTransactions(tx.items);
            }
        }).finally(() => setLoading(false));
    }, []);

    return (
        <div className="db-layout">
            <aside className="db-sidebar">
                <div className="db-sidebar-logo">US Bank</div>
                <nav className="db-nav">
                    {NAV_ITEMS.map(item => (
                        <button
                            key={item.id}
                            className={`db-nav-item${view === item.id ? ' active' : ''}`}
                            onClick={() => setView(item.id)}
                        >
                            <span>{item.label}</span>
                        </button>
                    ))}
                </nav>
                <button className="db-logout" onClick={logout}>Log out</button>
            </aside>

            <main className="db-main">
                {loading ? (
                    <div className="db-loading">Loading...</div>
                ) : (
                    <>
                        {view === 'overview' && <OverviewView accounts={accounts} transactions={transactions} onNavigate={setView} firstName={firstName} />}
                        {view === 'accounts' && <AccountsView accounts={accounts} onAccountCreated={acc => setAccounts(prev => [...prev, acc])} />}
                        {view === 'transfers' && <TransfersView />}
                        {view === 'history' && <HistoryView accounts={accounts} />}
                        {view === 'settings' && <SettingsView dark={dark} onToggleTheme={toggle} />}
                    </>
                )}
            </main>
        </div>
    );
}
