import type { Account } from '../../../api/accounts';
import AccountCard from '../components/AccountCard';

export default function AccountsView({ accounts }: { accounts: Account[] }) {
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
            <button className="db-btn-primary db-mt">+ Open new account</button>
        </div>
    );
}
