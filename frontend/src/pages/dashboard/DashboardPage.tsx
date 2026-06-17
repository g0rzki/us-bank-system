import { useState, useEffect } from 'react';
import { logout } from '../../api/auth';
import { getAccounts, getTransactions } from '../../api/accounts';
import type { Account, Transaction } from '../../api/accounts';
import OverviewView from './views/OverviewView';
import AccountsView from './views/AccountsView';
import CardsView from './views/CardsView';
import TransfersView from './views/TransfersView';
import HistoryView from './views/HistoryView';
import BlikView from './views/BlikView';
import SettingsView from './views/SettingsView';
import './DashboardPage.css';
import { useDarkMode } from '../../utils/useDarkMode';
import { LayoutDashboard, Wallet, CreditCard, ArrowLeftRight, History, Smartphone, Settings, LogOut } from 'lucide-react';

export type View = 'overview' | 'accounts' | 'cards' | 'transfers' | 'history' | 'blik' | 'settings';

const NAV_ITEMS: { id: View; label: string; icon: React.ReactNode }[] = [
    { id: 'overview', label: 'Overview', icon: <LayoutDashboard size={16} /> },
    { id: 'accounts', label: 'Accounts', icon: <Wallet size={16} /> },
    { id: 'cards', label: 'Cards', icon: <CreditCard size={16} /> },
    { id: 'transfers', label: 'Transfers', icon: <ArrowLeftRight size={16} /> },
    { id: 'history', label: 'History', icon: <History size={16} /> },
    { id: 'blik', label: 'BLIK', icon: <Smartphone size={16} /> },
    { id: 'settings', label: 'Settings', icon: <Settings size={16} /> },
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
                            {item.icon}
                            <span>{item.label}</span>
                        </button>
                    ))}
                </nav>
                <button className="db-logout" onClick={logout}>
                    <LogOut size={14} />
                    Log out
                </button>
            </aside>

            <main className="db-main">
                {loading ? (
                    <div className="db-loading">Loading...</div>
                ) : (
                    <>
                        {view === 'overview' && <OverviewView accounts={accounts} transactions={transactions} onNavigate={setView} firstName={firstName} />}
                        {view === 'accounts' && <AccountsView accounts={accounts} onAccountCreated={acc => setAccounts(prev => [...prev, acc])} />}
                        {view === 'cards' && <CardsView accounts={accounts} />}
                        {view === 'transfers' && <TransfersView />}
                        {view === 'history' && <HistoryView accounts={accounts} />}
                        {view === 'blik' && <BlikView accounts={accounts} />}
                        {view === 'settings' && <SettingsView dark={dark} onToggleTheme={toggle} />}
                    </>
                )}
            </main>
        </div>
    );
}
