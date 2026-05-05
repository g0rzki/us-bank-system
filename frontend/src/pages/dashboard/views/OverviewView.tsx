import type { Account, Transaction } from '../../../api/accounts';
import type { View } from '../DashboardPage';
import AccountCard from '../components/AccountCard';
import TransactionList from '../components/TransactionList';

export default function OverviewView({ accounts, transactions, onNavigate }: {
    accounts: Account[];
    transactions: Transaction[];
    onNavigate: (v: View) => void;
}) {
    const totalBalance = accounts.reduce((sum, a) => sum + a.balance, 0);

    return (
        <div className="db-view">
            <h1 className="db-view-title">Overview</h1>

            <div className="db-stat-row">
                <div className="db-stat-card">
                    <span className="db-stat-label">Total balance</span>
                    <span className="db-stat-value">${totalBalance.toFixed(2)}</span>
                </div>
                <div className="db-stat-card">
                    <span className="db-stat-label">Accounts</span>
                    <span className="db-stat-value">{accounts.length}</span>
                </div>
                <div className="db-stat-card">
                    <span className="db-stat-label">Recent transactions</span>
                    <span className="db-stat-value">{transactions.length}</span>
                </div>
            </div>

            <div className="db-overview-grid">
                <div className="db-section">
                    <div className="db-section-header">
                        <h2>My accounts</h2>
                        <button className="db-link" onClick={() => onNavigate('accounts')}>View all</button>
                    </div>
                    {accounts.length === 0 ? (
                        <p className="db-empty">No accounts yet.</p>
                    ) : (
                        <div className="db-account-cards-col">
                            {accounts.map(acc => <AccountCard key={acc.id} account={acc} />)}
                        </div>
                    )}
                </div>

                <div className="db-section">
                    <div className="db-section-header">
                        <h2>Recent transactions</h2>
                        <button className="db-link" onClick={() => onNavigate('history')}>View all</button>
                    </div>
                    {transactions.length === 0 ? (
                        <p className="db-empty">No transactions yet.</p>
                    ) : (
                        <TransactionList transactions={transactions} currency={accounts[0]?.currency ?? 'USD'} />
                    )}
                </div>
            </div>
        </div>
    );
}
