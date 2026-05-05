import { useState, useEffect } from 'react';
import { getTransactions } from '../../../api/accounts';
import type { Account, Transaction } from '../../../api/accounts';
import TransactionList from '../components/TransactionList';

export default function HistoryView({ accounts }: { accounts: Account[] }) {
    const [transactions, setTransactions] = useState<Transaction[]>([]);
    const [selected, setSelected] = useState<Account | null>(accounts[0] ?? null);
    const [loading, setLoading] = useState(false);

    useEffect(() => {
        if (!selected) return;
        setLoading(true);
        getTransactions(selected.id, 1, 20).then(d => setTransactions(d.items)).finally(() => setLoading(false));
    }, [selected]);

    return (
        <div className="db-view">
            <h1 className="db-view-title">Transaction history</h1>
            <div className="db-account-tabs">
                {accounts.map(acc => (
                    <button
                        key={acc.id}
                        className={`db-account-tab${selected?.id === acc.id ? ' active' : ''}`}
                        onClick={() => setSelected(acc)}
                    >
                        {acc.type} {'•'.repeat(acc.accountNumber.length - 4)}{acc.accountNumber.slice(-4)}
                    </button>
                ))}
            </div>
            {loading ? (
                <div className="db-loading">Loading...</div>
            ) : transactions.length === 0 ? (
                <p className="db-empty">No transactions for this account.</p>
            ) : (
                <TransactionList transactions={transactions} currency={selected?.currency ?? 'USD'} />
            )}
        </div>
    );
}
