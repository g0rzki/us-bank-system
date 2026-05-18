import { useState, useEffect } from 'react';
import { juniorLogout } from '../../api/auth';
import { useDarkMode } from '../../utils/useDarkMode';
import kidClient from '../../api/kidClient';
import type { Account, Transaction, PagedResponse } from '../../api/accounts';
import type { Transfer } from '../../api/transfers';
import '../dashboard/DashboardPage.css';
import '../../styles/TransferForm.css';

type View = 'overview' | 'transfers' | 'history';

const NAV_ITEMS: { id: View; label: string }[] = [
    { id: 'overview', label: 'Overview' },
    { id: 'transfers', label: 'Transfers' },
    { id: 'history', label: 'History' },
];

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
                <button className="db-logout" onClick={juniorLogout}>Log out</button>
            </aside>

            <main className="db-main">
                {view === 'overview' && <OverviewView />}
                {view === 'transfers' && <TransferView onSuccess={() => setView('overview')} />}
                {view === 'history' && <HistoryView />}
            </main>
        </div>
    );
}

function OverviewView() {
    const [account, setAccount] = useState<Account | null>(null);
    const [transactions, setTransactions] = useState<Transaction[]>([]);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        getKidAccount()
            .then(acc => {
                setAccount(acc);
                return getKidTransactions(acc.id, 1, 5);
            })
            .then(res => setTransactions(res.items))
            .catch(() => setError('Failed to load account data.'));
    }, []);

    if (error) return <div className="db-view"><p className="db-error">{error}</p></div>;
    if (!account) return <div className="db-view"><div className="db-loading">Loading...</div></div>;

    return (
        <div className="db-view">
            <h1 className="db-view-title">Overview</h1>
            <div className="db-stat-row">
                <div className="db-stat-card">
                    <span className="db-stat-label">Balance</span>
                    <span className="db-stat-value">${account.balance.toFixed(2)}</span>
                </div>
                <div className="db-stat-card">
                    <span className="db-stat-label">Account number</span>
                    <span className="db-stat-value" style={{ fontSize: '1rem' }}>{account.accountNumber}</span>
                </div>
                <div className="db-stat-card">
                    <span className="db-stat-label">Currency</span>
                    <span className="db-stat-value">{account.currency}</span>
                </div>
            </div>
            <div className="db-section">
                <div className="db-section-header">
                    <h2 className="db-section-title">Recent transactions</h2>
                </div>
                {transactions.length === 0 ? (
                    <p className="db-empty">No transactions yet.</p>
                ) : (
                    <ul className="db-tx-list">
                        {transactions.map(tx => (
                            <li key={tx.id} className="db-tx-item">
                                <div className="db-tx-left">
                                    <span className="db-tx-desc">{tx.description ?? 'Transaction'}</span>
                                    <span className="db-tx-date">{new Date(tx.createdAt).toLocaleDateString()}</span>
                                </div>
                                <div className="db-tx-right">
                                    <span className={`db-tx-amount ${tx.type}`}>
                                        {tx.type === 'debit' ? '-' : '+'}${tx.amount.toFixed(2)} {account.currency}
                                    </span>
                                    <span className={`db-tx-status ${tx.status}`}>{tx.status.replace('_', ' ')}</span>
                                </div>
                            </li>
                        ))}
                    </ul>
                )}
            </div>
        </div>
    );
}

function HistoryView() {
    const [account, setAccount] = useState<Account | null>(null);
    const [transactions, setTransactions] = useState<Transaction[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        getKidAccount()
            .then(acc => {
                setAccount(acc);
                return getKidTransactions(acc.id, 1, 50);
            })
            .then(res => setTransactions(res.items))
            .catch(() => setError('Failed to load transactions.'))
            .finally(() => setLoading(false));
    }, []);

    if (loading) return <div className="db-view"><div className="db-loading">Loading...</div></div>;

    return (
        <div className="db-view">
            <h1 className="db-view-title">History</h1>
            {error && <p className="db-error">{error}</p>}
            <div className="db-section">
                <h2 className="db-section-title">All transactions</h2>
                {transactions.length === 0 ? (
                    <p className="db-empty">No transactions yet.</p>
                ) : (
                    <ul className="db-tx-list">
                        {transactions.map(tx => (
                            <li key={tx.id} className="db-tx-item">
                                <div className="db-tx-left">
                                    <span className="db-tx-desc">{tx.description ?? 'Transaction'}</span>
                                    <span className="db-tx-date">{new Date(tx.createdAt).toLocaleString()}</span>
                                </div>
                                <div className="db-tx-right">
                                    <span className={`db-tx-amount ${tx.type}`}>
                                        {tx.type === 'debit' ? '-' : '+'}${tx.amount.toFixed(2)} {account?.currency ?? 'USD'}
                                    </span>
                                    <span className={`db-tx-status ${tx.status}`}>{tx.status.replace('_', ' ')}</span>
                                </div>
                            </li>
                        ))}
                    </ul>
                )}
            </div>
        </div>
    );
}

type Channel = 'internal' | 'ach' | 'rtp' | 'fednow' | 'swift';

const CHANNEL_INFO: Record<Channel, { label: string; settlement: string }> = {
    internal: { label: 'Internal', settlement: 'Instant' },
    ach: { label: 'ACH', settlement: 'T+1 business day' },
    rtp: { label: 'RTP', settlement: 'Instant (24/7)' },
    fednow: { label: 'FedNow', settlement: 'Instant' },
    swift: { label: 'SWIFT', settlement: '1–5 business days' },
};

function TransferView({ onSuccess }: { onSuccess: () => void }) {
    const [account, setAccount] = useState<Account | null>(null);
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
    const [error, setError] = useState<string | null>(null);
    const [success, setSuccess] = useState(false);

    useEffect(() => {
        getKidAccount().then(setAccount).catch(() => {});
    }, []);

    const info = CHANNEL_INFO[channel];

    const handleSubmit = async () => {
        if (!account) return;
        setError(null);
        const amt = parseFloat(amount);
        if (!amount || isNaN(amt) || amt <= 0) { setError('Enter a valid amount'); return; }
        setLoading(true);
        try {
            let body: Record<string, unknown> = { fromAccountId: account.id, amount: amt, currency: 'USD', description: description || undefined };
            if (channel === 'internal') {
                if (!toAccountNumber) { setError('Enter recipient account number'); setLoading(false); return; }
                body = { ...body, toAccountNumber };
            } else if (channel === 'ach') {
                if (!toRoutingNumber || !toAccountNumber) { setError('Routing and account numbers are required'); setLoading(false); return; }
                body = { ...body, toRoutingNumber, toAccountNumber };
            } else if (channel === 'rtp' || channel === 'fednow') {
                if (!toAccountNumber) { setError('Enter recipient account number'); setLoading(false); return; }
                body = { ...body, toAccountNumber };
            } else if (channel === 'swift') {
                if (!iban || !bic || !beneficiaryName) { setError('IBAN, BIC and beneficiary name are required'); setLoading(false); return; }
                body = { ...body, iban, bic, beneficiaryName, beneficiaryAddress: beneficiaryAddress || undefined, chargeBearer, remittanceInfo: remittanceInfo || undefined };
            }
            await kidClient.post<Transfer>(`/transfers/${channel}`, body);
            setSuccess(true);
            setTimeout(onSuccess, 1500);
        } catch (e: unknown) {
            const err = e as { response?: { data?: { detail?: string; message?: string } } };
            setError(err?.response?.data?.detail ?? err?.response?.data?.message ?? 'Transfer failed. Please try again.');
        } finally {
            setLoading(false);
        }
    };

    if (success) {
        return (
            <div className="db-view">
                <h1 className="db-view-title">Transfers</h1>
                <div className="db-section">
                    <p style={{ color: '#16a34a', background: '#f0fdf4', border: '1px solid #bbf7d0', borderRadius: '8px', padding: '12px 16px', fontSize: 'var(--font-size-sm)' }}>
                        Transfer submitted — waiting for parent approval.
                    </p>
                </div>
            </div>
        );
    }

    return (
        <div className="db-view">
            <h1 className="db-view-title">Transfers</h1>
            <div className="db-section">
                <h2 className="db-section-title">New transfer</h2>
                <p style={{ fontSize: 'var(--font-size-sm)', color: 'var(--color-text-muted)', marginBottom: '16px' }}>
                    All transfers require parent approval before processing.
                </p>
                <div className="tf-channels" style={{ marginBottom: '16px' }}>
                    {(Object.keys(CHANNEL_INFO) as Channel[]).map(ch => (
                        <button key={ch} className={`tf-channel-btn${channel === ch ? ' active' : ''}`} onClick={() => { setChannel(ch); setToAccountNumber(''); setError(null); }}>
                            {CHANNEL_INFO[ch].label}
                        </button>
                    ))}
                </div>
                <div className="tf-settlement" style={{ marginBottom: '16px' }}>
                    <span className="tf-settlement-label">Settlement:</span>
                    <span className="tf-settlement-value">{info.settlement}</span>
                </div>
                {error && <p className="db-error">{error}</p>}
                <div className="tf-fields">
                    {(channel === 'internal' || channel === 'rtp' || channel === 'fednow') && (
                        <div className="tf-field">
                            <label>Recipient account number</label>
                            <input value={toAccountNumber} onChange={e => setToAccountNumber(e.target.value)} placeholder="16-digit account number" />
                        </div>
                    )}
                    {channel === 'ach' && (
                        <>
                            <div className="tf-field"><label>Routing number</label><input value={toRoutingNumber} onChange={e => setToRoutingNumber(e.target.value)} placeholder="9 digits" /></div>
                            <div className="tf-field"><label>Account number</label><input value={toAccountNumber} onChange={e => setToAccountNumber(e.target.value)} placeholder="Recipient account number" /></div>
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
                    <div className="tf-field"><label>Amount (USD)</label><input type="number" min="0.01" step="0.01" value={amount} onChange={e => setAmount(e.target.value)} placeholder="0.00" /></div>
                    <div className="tf-field"><label>Description <span className="tf-optional">(optional)</span></label><input value={description} onChange={e => setDescription(e.target.value)} placeholder="What's this for?" /></div>
                </div>
                <div className="tf-actions" style={{ marginTop: '8px' }}>
                    <button className="tf-btn-submit" onClick={handleSubmit} disabled={loading || !account}>
                        {loading ? 'Sending…' : `Send ${info.label}`}
                    </button>
                </div>
            </div>
        </div>
    );
}
