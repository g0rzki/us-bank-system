import { useState, useEffect } from 'react';
import { getTransactions } from '../../../api/accounts';
import type { Account, Transaction } from '../../../api/accounts';
import AccountTabs from '../components/AccountTabs';
import TransactionDetails from '../components/TransactionDetails';

function groupByDate(items: Transaction[]): [string, Transaction[]][] {
    const map = new Map<string, Transaction[]>();
    for (const item of items) {
        const day = new Date(item.createdAt).toLocaleDateString('en-US', { year: 'numeric', month: 'long', day: 'numeric' });
        if (!map.has(day)) map.set(day, []);
        map.get(day)!.push(item);
    }
    return Array.from(map.entries());
}

function statusLabel(status: string): string {
    switch (status) {
        case 'completed': return 'Completed';
        case 'pending': return 'Processing';
        case 'pending_approval': return 'Awaiting approval';
        case 'failed': return 'Failed';
        default: return status;
    }
}


export default function HistoryView({ accounts }: { accounts: Account[] }) {
    const [selectedAccount, setSelectedAccount] = useState<Account | null>(accounts[0] ?? null);
    const [transactions, setTransactions] = useState<Transaction[]>([]);
    const [loading, setLoading] = useState(false);
    const [expandedId, setExpandedId] = useState<string | null>(null);

    useEffect(() => {
        if (!selectedAccount) return;
        setLoading(true);
        setExpandedId(null);
        getTransactions(selectedAccount.id, 1, 50)
            .then(d => setTransactions(d.items))
            .finally(() => setLoading(false));
    }, [selectedAccount]);

    const groups = groupByDate(transactions);

    return (
        <div className="db-view">
            <h1 className="db-view-title">History</h1>

            <AccountTabs
                accounts={accounts}
                selectedId={selectedAccount?.id ?? ''}
                onSelect={id => setSelectedAccount(accounts.find(a => a.id === id) ?? null)}
            />

            {loading ? (
                <div className="db-loading">Loading...</div>
            ) : transactions.length === 0 ? (
                <p className="db-empty">No transactions for this account.</p>
            ) : (
                groups.map(([day, txs]) => (
                    <div key={day} style={{ marginBottom: 'var(--spacing-lg)' }}>
                        <div style={{ fontSize: 'var(--font-size-sm)', color: 'var(--color-text-muted)', fontWeight: 600, marginBottom: 6, marginTop: 'var(--spacing-md)' }}>
                            {day}
                        </div>
                        <div className="db-transfer-list">
                            {txs.map(tx => {
                                const expanded = expandedId === tx.id;
                                const isDebit = tx.type === 'debit';
                                return (
                                    <div key={tx.id} className="db-transfer-item">
                                        <button
                                            className={`db-transfer-row${expanded ? ' active' : ''}`}
                                            onClick={() => setExpandedId(expanded ? null : tx.id)}
                                        >
                                            <div className="db-transfer-row-left">
                                                <span className="db-transfer-channel">{tx.description ?? (isDebit ? 'Payment' : 'Received')}</span>
                                                <span className="db-tx-date">{new Date(tx.createdAt).toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' })}</span>
                                            </div>
                                            <div className="db-transfer-row-right">
                                                <span className={`db-tx-amount ${isDebit ? 'debit' : 'credit'}`}>
                                                    {isDebit ? '−' : '+'}${tx.amount.toFixed(2)}
                                                </span>
                                                <span className={`db-tx-status ${tx.status}`}>{statusLabel(tx.status)}</span>
                                            </div>
                                        </button>
                                        {expanded && <TransactionDetails tx={tx} />}
                                    </div>
                                );
                            })}
                        </div>
                    </div>
                ))
            )}
        </div>
    );
}
