import { useState, useEffect } from 'react';
import { createAccount, getJuniorAccounts } from '../../../api/accounts';
import type { Account, JuniorAccount } from '../../../api/accounts';
import AccountCard from '../components/AccountCard';
import JuniorAccountList from '../components/JuniorAccountList';

export default function AccountsView({ accounts, onAccountCreated }: {
    accounts: Account[];
    onAccountCreated: (account: Account) => void;
}) {
    const [showForm, setShowForm] = useState(false);
    const [type, setType] = useState<'checking' | 'savings'>('checking');
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [juniorAccounts, setJuniorAccounts] = useState<JuniorAccount[]>([]);
    const [juniorLoading, setJuniorLoading] = useState(false);

    useEffect(() => {
        if (accounts.length === 0) return;
        setJuniorLoading(true);
        Promise.all(accounts.map(acc => getJuniorAccounts(acc.id)))
            .then(results => setJuniorAccounts(results.flat()))
            .finally(() => setJuniorLoading(false));
    }, [accounts]);

    const handleSubmit = async () => {
        setLoading(true);
        setError(null);
        try {
            const account = await createAccount(type);
            onAccountCreated(account);
            setShowForm(false);
        } catch {
            setError('Failed to create account. Please try again.');
        } finally {
            setLoading(false);
        }
    };

    return (
        <div className="db-view">
            <h1 className="db-view-title">Accounts</h1>
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
                </div>
                {juniorLoading ? (
                    <div className="db-loading">Loading...</div>
                ) : juniorAccounts.length === 0 ? (
                    <p className="db-empty">No junior accounts linked to your accounts.</p>
                ) : (
                    <JuniorAccountList accounts={juniorAccounts} />
                )}
            </div>

            {showForm ? (
                <div className="db-form-card db-mt">
                    <h2 className="db-section-title">Open new account</h2>
                    <div className="db-form-field">
                        <label className="db-label">Account type</label>
                        <div className="db-toggle-group">
                            {(['checking', 'savings'] as const).map(t => (
                                <button
                                    key={t}
                                    type="button"
                                    className={`db-toggle-btn${type === t ? ' active' : ''}`}
                                    onClick={() => setType(t)}
                                >
                                    {t.charAt(0).toUpperCase() + t.slice(1)}
                                </button>
                            ))}
                        </div>
                    </div>
                    <div className="db-form-field">
                        <label className="db-label">Currency</label>
                        <span className="db-value-static">USD</span>
                    </div>
                    {error && <p className="db-error">{error}</p>}
                    <div className="db-form-actions">
                        <button className="db-btn-primary" onClick={handleSubmit} disabled={loading}>
                            {loading ? 'Opening…' : 'Open account'}
                        </button>
                        <button className="db-btn-secondary" onClick={() => setShowForm(false)} disabled={loading}>
                            Cancel
                        </button>
                    </div>
                </div>
            ) : (
                <button className="db-btn-primary db-mt" onClick={() => setShowForm(true)}>
                    + Open new account
                </button>
            )}
        </div>
    );
}
