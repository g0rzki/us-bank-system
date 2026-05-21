import { useState, useEffect } from 'react';
import { createAccount, createJuniorAccount, getJuniorAccounts } from '../../../api/accounts';
import type { Account, JuniorAccount } from '../../../api/accounts';
import AccountCard from '../components/AccountCard';
import JuniorAccountList from '../components/JuniorAccountList';
import { useToast } from '../../../context/ToastContext';

export default function AccountsView({ accounts, onAccountCreated }: {
    accounts: Account[];
    onAccountCreated: (account: Account) => void;
}) {
    const { showToast } = useToast();
    const [loadingType, setLoadingType] = useState<'checking' | 'savings' | null>(null);
    const [juniorAccounts, setJuniorAccounts] = useState<JuniorAccount[]>([]);
    const [juniorLoading, setJuniorLoading] = useState(false);
    const [showJuniorForm, setShowJuniorForm] = useState(false);
    const [juniorParentAccountId, setJuniorParentAccountId] = useState('');
    const [juniorEmail, setJuniorEmail] = useState('');
    const [juniorPassword, setJuniorPassword] = useState('');
    const [juniorFirstName, setJuniorFirstName] = useState('');
    const [juniorLastName, setJuniorLastName] = useState('');
    const [juniorDob, setJuniorDob] = useState('');
    const [juniorLoading2, setJuniorLoading2] = useState(false);

    useEffect(() => {
        if (accounts.length === 0) return;
        setJuniorLoading(true);
        setJuniorParentAccountId(accounts[0]?.id ?? '');
        getJuniorAccounts(accounts[0].id)
            .then(results => setJuniorAccounts(results))
            .finally(() => setJuniorLoading(false));
    }, [accounts]);

    const handleCreateJunior = async () => {
        if (!juniorParentAccountId || !juniorEmail || !juniorPassword || !juniorFirstName || !juniorLastName || !juniorDob) {
            showToast('All fields are required');
            return;
        }
        setJuniorLoading2(true);
        try {
            const created = await createJuniorAccount(juniorParentAccountId, juniorEmail, juniorPassword, juniorFirstName, juniorLastName, juniorDob);
            setJuniorAccounts(prev => [...prev, created]);
            setShowJuniorForm(false);
            setJuniorEmail(''); setJuniorPassword(''); setJuniorFirstName(''); setJuniorLastName(''); setJuniorDob('');
        } catch (e: any) {
            showToast(e?.response?.data?.detail ?? e?.response?.data?.message ?? 'Failed to create junior account.');
        } finally {
            setJuniorLoading2(false);
        }
    };

    const handleCreate = async (type: 'checking' | 'savings') => {
        setLoadingType(type);
        try {
            const account = await createAccount(type);
            onAccountCreated(account);
        } catch (e: any) {
            showToast(e?.response?.data?.detail ?? e?.response?.data?.message ?? 'Failed to create account. Please try again.');
        } finally {
            setLoadingType(null);
        }
    };

    const canCreateMore = true;

    return (
        <div className="db-view">
            <div className="db-section-header db-mb">
                <h1 className="db-view-title" style={{ margin: 0 }}>Accounts</h1>
                {canCreateMore && (
                    <div style={{ display: 'flex', gap: 8 }}>
                        <button
                            className="db-btn-secondary"
                            onClick={() => handleCreate('checking')}
                            disabled={loadingType !== null}
                        >
                            {loadingType === 'checking' ? 'Opening…' : '+ Checking account'}
                        </button>
                        <button
                            className="db-btn-secondary"
                            onClick={() => handleCreate('savings')}
                            disabled={loadingType !== null}
                        >
                            {loadingType === 'savings' ? 'Opening…' : '+ Savings account'}
                        </button>
                    </div>
                )}
            </div>

            {accounts.length === 0 ? (
                <p className="db-empty">No accounts yet.</p>
            ) : (
                <div className="db-account-cards">
                    {accounts.map(acc => <AccountCard key={acc.id} account={acc} detailed />)}
                </div>
            )}

            <div className="db-section db-mt">
                <div className="db-section-header">
                    <h2>Junior accounts</h2>
                    {!showJuniorForm && (
                        <button className="db-btn-secondary" style={{ fontSize: '0.875rem' }} onClick={() => setShowJuniorForm(true)}>
                            + Add junior account
                        </button>
                    )}
                </div>
                {showJuniorForm && (
                    <div className="db-modal-overlay" onClick={() => !juniorLoading2 && setShowJuniorForm(false)}>
                        <div className="db-modal" onClick={e => e.stopPropagation()}>
                            <h2 className="db-section-title">New junior account</h2>
                            <div className="db-form-field">
                                <span className="db-label">Parent account</span>
                                <select className="db-input" value={juniorParentAccountId} onChange={e => setJuniorParentAccountId(e.target.value)}>
                                    {accounts.map(a => (
                                        <option key={a.id} value={a.id}>{a.accountNumber} ({a.type})</option>
                                    ))}
                                </select>
                            </div>
                            <div className="db-form-field">
                                <span className="db-label">First name</span>
                                <input className="db-input" type="text" value={juniorFirstName} onChange={e => setJuniorFirstName(e.target.value)} placeholder="First name" />
                            </div>
                            <div className="db-form-field">
                                <span className="db-label">Last name</span>
                                <input className="db-input" type="text" value={juniorLastName} onChange={e => setJuniorLastName(e.target.value)} placeholder="Last name" />
                            </div>
                            <div className="db-form-field">
                                <span className="db-label">Email</span>
                                <input className="db-input" type="email" value={juniorEmail} onChange={e => setJuniorEmail(e.target.value)} placeholder="child@example.com" />
                            </div>
                            <div className="db-form-field">
                                <span className="db-label">Password</span>
                                <input className="db-input" type="password" value={juniorPassword} onChange={e => setJuniorPassword(e.target.value)} placeholder="Min. 8 characters" />
                            </div>
                            <div className="db-form-field">
                                <span className="db-label">Date of birth (age 7–13)</span>
                                <input className="db-input" type="date" value={juniorDob} onChange={e => setJuniorDob(e.target.value)} />
                            </div>
                            <div className="db-form-actions">
                                <button className="db-btn-primary" onClick={handleCreateJunior} disabled={juniorLoading2}>
                                    {juniorLoading2 ? 'Creating…' : 'Create account'}
                                </button>
                            </div>
                        </div>
                    </div>
                )}
                {juniorLoading ? (
                    <div className="db-loading">Loading...</div>
                ) : juniorAccounts.length === 0 ? (
                    <p className="db-empty">No junior accounts linked to your accounts.</p>
                ) : (
                    <JuniorAccountList accounts={juniorAccounts} />
                )}
            </div>
        </div>
    );
}
