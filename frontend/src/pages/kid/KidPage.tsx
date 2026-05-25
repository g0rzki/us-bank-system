import { useState, useEffect } from 'react';
import { juniorLogout } from '../../api/auth';
import { useDarkMode } from '../../utils/useDarkMode';
import { useToast } from '../../context/ToastContext';
import kidClient from '../../api/kidClient';
import type { Account, Transaction, PagedResponse } from '../../api/accounts';
import type { Transfer } from '../../api/transfers';
import { LayoutDashboard, ArrowLeftRight, History, LogOut, Building2, Banknote, Zap, Timer, Globe, DollarSign, CreditCard } from 'lucide-react';
import TransactionDetails from '../dashboard/components/TransactionDetails';
import '../dashboard/DashboardPage.css';
import '../../styles/TransferForm.css';

type View = 'overview' | 'transfers' | 'history';
type Channel = 'internal' | 'ach' | 'rtp' | 'fednow' | 'swift';

const NAV_ITEMS: { id: View; label: string; icon: React.ReactNode }[] = [
    { id: 'overview', label: 'Overview', icon: <LayoutDashboard size={16} /> },
    { id: 'transfers', label: 'Transfers', icon: <ArrowLeftRight size={16} /> },
    { id: 'history', label: 'History', icon: <History size={16} /> },
];

const CHANNEL_INFO: Record<Channel, { label: string; desc: string; settlement: string; icon: React.ReactNode }> = {
    internal: { label: 'Internal', desc: 'Between accounts', settlement: 'Instant', icon: <Building2 size={16} /> },
    ach: { label: 'ACH', desc: 'Standard bank transfer', settlement: 'T+1 business day', icon: <Banknote size={16} /> },
    rtp: { label: 'RTP', desc: 'Real-time payment', settlement: 'Instant (24/7)', icon: <Zap size={16} /> },
    fednow: { label: 'FedNow', desc: 'Instant RTGS settlement', settlement: 'Instant', icon: <Timer size={16} /> },
    swift: { label: 'SWIFT', desc: 'International wire transfer', settlement: '1–5 business days', icon: <Globe size={16} /> },
};

function getFirstNameFromKidToken(): string {
    const token = localStorage.getItem('kid_token');
    if (!token) return '';
    try {
        const payload = JSON.parse(atob(token.split('.')[1]));
        return payload['given_name'] ?? payload['firstName'] ?? '';
    } catch {
        return '';
    }
}

async function getKidAccount(): Promise<Account> {
    const res = await kidClient.get<Account[]>('/accounts');
    if (!res.data.length) throw new Error('No account found');
    return res.data[0];
}

async function getKidTransactions(accountId: string, page = 1, pageSize = 20): Promise<PagedResponse<Transaction>> {
    const res = await kidClient.get<PagedResponse<Transaction>>(`/accounts/${accountId}/transactions`, { params: { page, pageSize } });
    return res.data;
}

export default function KidPage() {
    const { dark, toggle } = useDarkMode();
    const [view, setView] = useState<View>('overview');
    const firstName = getFirstNameFromKidToken();

    return (
        <div className="db-layout" style={{ '--color-primary': '#16a34a', '--color-primary-hover': '#15803d' } as React.CSSProperties}>
            <aside className="db-sidebar">
                <div className="db-sidebar-logo" style={{ color: 'var(--color-primary)' }}>
                    US Bank
                    <span style={{ display: 'block', fontSize: '0.65rem', fontWeight: 600, letterSpacing: '0.12em', textTransform: 'uppercase', background: 'var(--color-primary)', color: '#fff', borderRadius: '4px', padding: '1px 6px', marginTop: '2px', width: 'fit-content' }}>Junior</span>
                </div>
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
                <button className="db-theme-toggle" onClick={toggle}>
                    <span>{dark ? 'Light mode' : 'Dark mode'}</span>
                    <span className={`db-theme-track${dark ? ' on' : ''}`}>
                        <span className="db-theme-thumb" />
                    </span>
                </button>
                <button className="db-logout" onClick={juniorLogout}>
                    <LogOut size={14} />
                    Log out
                </button>
            </aside>

            <main className="db-main">
                {view === 'overview' && <OverviewView firstName={firstName} onNavigate={setView} />}
                {view === 'transfers' && <TransferView />}
                {view === 'history' && <HistoryView />}
            </main>
        </div>
    );
}

function OverviewView({ firstName, onNavigate }: { firstName: string; onNavigate: (v: View) => void }) {
    const [account, setAccount] = useState<Account | null>(null);
    const [transactions, setTransactions] = useState<Transaction[]>([]);

    useEffect(() => {
        getKidAccount()
            .then(acc => {
                setAccount(acc);
                return getKidTransactions(acc.id, 1, 10);
            })
            .then(res => setTransactions(res.items))
            .catch(() => {});
    }, []);

    if (!account) return <div className="db-view"><div className="db-loading">Loading...</div></div>;

    const recentSpent = transactions.filter(t => t.type === 'debit').reduce((sum, t) => sum + t.amount, 0);

    return (
        <div className="db-view">
            <h1 className="db-view-title">{firstName ? `Welcome, ${firstName}` : 'Overview'}</h1>
            <div className="db-stat-row">
                <div className="db-stat-card">
                    <span className="db-stat-label"><DollarSign size={14} style={{ verticalAlign: 'middle', marginRight: 4 }} />Balance</span>
                    <span className="db-stat-value">${account.balance.toFixed(2)}</span>
                </div>
                <div className="db-stat-card">
                    <span className="db-stat-label"><CreditCard size={14} style={{ verticalAlign: 'middle', marginRight: 4 }} />Account number</span>
                    <span className="db-stat-value" style={{ fontSize: '1rem' }}>{account.accountNumber}</span>
                </div>
                <div className="db-stat-card">
                    <span className="db-stat-label">Recent spending</span>
                    <span className="db-stat-value">${recentSpent.toFixed(2)}</span>
                </div>
            </div>
            <div className="db-section">
                <div className="db-section-header">
                    <h2>Recent transactions</h2>
                    <button className="db-link" onClick={() => onNavigate('history')}>View all</button>
                </div>
                {transactions.length === 0 ? (
                    <p className="db-empty">No transactions yet.</p>
                ) : (
                    <div className="db-transfer-list">
                        {transactions.map(tx => {
                            const isDebit = tx.type === 'debit';
                            return (
                                <div key={tx.id} className="db-transfer-item">
                                    <div className="db-transfer-row" style={{ cursor: 'default' }}>
                                        <div className="db-transfer-row-left">
                                            <span className="db-transfer-channel">{tx.description ?? 'Transaction'}</span>
                                            <span className="db-tx-date">{new Date(tx.createdAt).toLocaleDateString()}</span>
                                        </div>
                                        <div className="db-transfer-row-right">
                                            <span className={`db-tx-amount ${isDebit ? 'debit' : 'credit'}`}>
                                                {isDebit ? '−' : '+'}${tx.amount.toFixed(2)}
                                            </span>
                                            <span className={`db-tx-status ${tx.status}`}>{tx.status.replace('_', ' ')}</span>
                                        </div>
                                    </div>
                                </div>
                            );
                        })}
                    </div>
                )}
            </div>
        </div>
    );
}

function HistoryView() {
    const [account, setAccount] = useState<Account | null>(null);
    const [transactions, setTransactions] = useState<Transaction[]>([]);
    const [loading, setLoading] = useState(true);
    const [expandedId, setExpandedId] = useState<string | null>(null);

    useEffect(() => {
        getKidAccount()
            .then(acc => {
                setAccount(acc);
                return getKidTransactions(acc.id, 1, 50);
            })
            .then(res => setTransactions(res.items))
            .finally(() => setLoading(false));
    }, []);

    if (loading) return <div className="db-view"><div className="db-loading">Loading...</div></div>;

    return (
        <div className="db-view">
            <div className="db-section">
                <h2 className="db-section-title">All transactions</h2>
                {transactions.length === 0 ? (
                    <p className="db-empty">No transactions yet.</p>
                ) : (
                    <div className="db-transfer-list">
                        {transactions.map(tx => {
                            const isDebit = tx.type === 'debit';
                            const expanded = expandedId === tx.id;
                            return (
                                <div key={tx.id} className="db-transfer-item">
                                    <button
                                        className={`db-transfer-row${expanded ? ' active' : ''}`}
                                        onClick={() => setExpandedId(expanded ? null : tx.id)}
                                    >
                                        <div className="db-transfer-row-left">
                                            <span className="db-transfer-channel">{tx.description ?? 'Transaction'}</span>
                                            <span className="db-tx-date">{new Date(tx.createdAt).toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' })}</span>
                                        </div>
                                        <div className="db-transfer-row-right">
                                            <span className={`db-tx-amount ${isDebit ? 'debit' : 'credit'}`}>
                                                {isDebit ? '−' : '+'}${tx.amount.toFixed(2)} {account?.currency ?? 'USD'}
                                            </span>
                                            <span className={`db-tx-status ${tx.status}`}>{tx.status.replace('_', ' ')}</span>
                                        </div>
                                    </button>
                                    {expanded && <TransactionDetails tx={tx} />}
                                </div>
                            );
                        })}
                    </div>
                )}
            </div>
        </div>
    );
}

function TransferView() {
    const { showToast } = useToast();
    const [account, setAccount] = useState<Account | null>(null);
    const [pendingTransfers, setPendingTransfers] = useState<Transfer[]>([]);
    const [channel, setChannel] = useState<Channel>('internal');
    const [toAccountNumber, setToAccountNumber] = useState('');
    const [toRoutingNumber, setToRoutingNumber] = useState('');
    const [iban, setIban] = useState('');
    const [bic, setBic] = useState('');
    const [beneficiaryName, setBeneficiaryName] = useState('');
    const [beneficiaryAddress, setBeneficiaryAddress] = useState('');
    const [chargeBearer, setChargeBearer] = useState('SHA');
    const [remittanceInfo, setRemittanceInfo] = useState('');
    const [amount, setAmount] = useState('');
    const [description, setDescription] = useState('');
    const [loading, setLoading] = useState(false);

    const loadData = () => {
        getKidAccount().then(acc => {
            setAccount(acc);
            return kidClient.get<Transfer[]>('/transfers');
        }).then(res => {
            setPendingTransfers(res.data.filter(t => t.status === 'pending_approval'));
        }).catch(() => {});
    };

    useEffect(() => {
        loadData();
    }, []);

    useEffect(() => {
        setToAccountNumber('');
        setToRoutingNumber('');
        setIban('');
        setBic('');
        setBeneficiaryName('');
        setBeneficiaryAddress('');
        setRemittanceInfo('');
        setAmount('');
        setDescription('');
    }, [channel]);

    const handleSubmit = async () => {
        if (!account) return;
        const amt = parseFloat(amount);
        if (!amount || isNaN(amt) || amt <= 0) { showToast('Enter a valid amount'); return; }

        setLoading(true);
        try {
            let body: Record<string, unknown> = { fromAccountId: account.id, amount: amt, currency: 'USD', description: description || undefined };
            if (channel === 'internal') {
                if (!toAccountNumber) { showToast('Enter recipient account number'); return; }
                body = { ...body, toAccountNumber };
            } else if (channel === 'ach') {
                if (!toRoutingNumber || !toAccountNumber) { showToast('Routing and account numbers are required'); return; }
                body = { ...body, toRoutingNumber, toAccountNumber };
            } else if (channel === 'rtp' || channel === 'fednow') {
                if (!toAccountNumber) { showToast('Enter recipient account number'); return; }
                body = { ...body, toAccountNumber };
            } else if (channel === 'swift') {
                if (!iban || !bic || !beneficiaryName) { showToast('IBAN, BIC and beneficiary name are required'); return; }
                body = { ...body, iban, bic, beneficiaryName, beneficiaryAddress: beneficiaryAddress || undefined, chargeBearer, remittanceInfo: remittanceInfo || undefined };
            }
            await kidClient.post<Transfer>(`/transfers/${channel}`, body);
            showToast('Transfer submitted — waiting for parent approval.', 'success');
            setAmount('');
            setDescription('');
            setToAccountNumber('');
            loadData();
        } catch (e: unknown) {
            const err = e as { response?: { data?: { detail?: string; message?: string } } };
            showToast(err?.response?.data?.detail ?? err?.response?.data?.message ?? 'Transfer failed. Please try again.');
        } finally {
            setLoading(false);
        }
    };

    return (
        <div className="db-view">
            <div className="db-section db-mb">
                <h2 className="db-section-title">New transfer</h2>
                <p style={{ fontSize: 'var(--font-size-sm)', color: 'var(--color-text-muted)', marginBottom: 'var(--spacing-md)' }}>
                    All transfers require parent approval before processing.
                </p>
                <div className="db-surface">
                    <div className="db-channel-tabs">
                        {(Object.keys(CHANNEL_INFO) as Channel[]).map(ch => (
                            <button
                                key={ch}
                                className={`db-channel-tab${channel === ch ? ' active' : ''}`}
                                onClick={() => setChannel(ch)}
                                disabled={!account}
                            >
                                <span className="db-channel-tab-icon">{CHANNEL_INFO[ch].icon}</span>
                                <span className="db-channel-tab-label">{CHANNEL_INFO[ch].label}</span>
                                <span className="db-channel-tab-desc">{CHANNEL_INFO[ch].desc}</span>
                                <span className="db-channel-tab-settlement">{CHANNEL_INFO[ch].settlement}</span>
                            </button>
                        ))}
                    </div>
                    <div className="db-channel-form">
                        <div className="tf-fields">
                            {(channel === 'internal' || channel === 'rtp' || channel === 'fednow') && (
                                <div className="tf-field">
                                    <label>Recipient account number</label>
                                    <input value={toAccountNumber} onChange={e => setToAccountNumber(e.target.value)} placeholder="Account number" />
                                </div>
                            )}
                            {channel === 'ach' && (
                                <>
                                    <div className="tf-field">
                                        <label>Routing number</label>
                                        <input value={toRoutingNumber} onChange={e => setToRoutingNumber(e.target.value)} placeholder="9 digits" />
                                    </div>
                                    <div className="tf-field">
                                        <label>Recipient account number</label>
                                        <input value={toAccountNumber} onChange={e => setToAccountNumber(e.target.value)} placeholder="Account number" />
                                    </div>
                                </>
                            )}
                            {channel === 'swift' && (
                                <>
                                    <div className="tf-field"><label>IBAN</label><input value={iban} onChange={e => setIban(e.target.value)} placeholder="e.g. DE89370400440532013000" /></div>
                                    <div className="tf-field"><label>BIC / SWIFT code</label><input value={bic} onChange={e => setBic(e.target.value)} placeholder="e.g. DEUTDEDB" /></div>
                                    <div className="tf-field"><label>Beneficiary name</label><input value={beneficiaryName} onChange={e => setBeneficiaryName(e.target.value)} placeholder="Full name" /></div>
                                    <div className="tf-field"><label>Beneficiary address <span className="tf-optional">(optional)</span></label><input value={beneficiaryAddress} onChange={e => setBeneficiaryAddress(e.target.value)} placeholder="Street, city, country" /></div>
                                    <div className="tf-field">
                                        <label>Charge bearer</label>
                                        <select value={chargeBearer} onChange={e => setChargeBearer(e.target.value)}>
                                            <option value="SHA">SHA — shared costs</option>
                                            <option value="OUR">OUR — sender pays all</option>
                                            <option value="BEN">BEN — beneficiary pays all</option>
                                        </select>
                                    </div>
                                    <div className="tf-field"><label>Remittance info <span className="tf-optional">(optional)</span></label><input value={remittanceInfo} onChange={e => setRemittanceInfo(e.target.value)} placeholder="Invoice number, purpose..." /></div>
                                </>
                            )}
                            <div className="tf-field">
                                <label>Amount (USD)</label>
                                <input type="number" min="0.01" step="0.01" value={amount} onChange={e => setAmount(e.target.value)} placeholder="0.00" />
                            </div>
                            <div className="tf-field">
                                <label>Description <span className="tf-optional">(optional)</span></label>
                                <input value={description} onChange={e => setDescription(e.target.value)} placeholder="What's this for?" />
                            </div>
                            <div className="tf-submit-row">
                                <button className="tf-btn-submit" onClick={handleSubmit} disabled={loading || !account}>
                                    {loading ? 'Sending…' : 'Send transfer'}
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            </div>

            <div className="db-section">
                <h2 className="db-section-title">Awaiting parent approval</h2>
                {pendingTransfers.length === 0 ? (
                    <p className="db-empty">No transfers awaiting approval.</p>
                ) : (
                    <div className="db-transfer-list">
                        {pendingTransfers.map(t => (
                            <div key={t.id} className="db-transfer-item">
                                <div className="db-transfer-row" style={{ cursor: 'default' }}>
                                    <div className="db-transfer-row-left">
                                        <span className="db-transfer-channel">{t.channel.toUpperCase()}</span>
                                        <span className="db-tx-date">{new Date(t.createdAt).toLocaleDateString()}</span>
                                    </div>
                                    <div className="db-transfer-row-right">
                                        <span className="db-tx-amount debit">${t.amount.toFixed(2)} {t.currency}</span>
                                        <span className="db-tx-status pending">Awaiting approval</span>
                                    </div>
                                </div>
                            </div>
                        ))}
                    </div>
                )}
            </div>
        </div>
    );
}
