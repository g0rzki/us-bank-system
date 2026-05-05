import type { Transaction } from '../../../api/accounts';

export default function TransactionList({ transactions, currency }: { transactions: Transaction[]; currency: string }) {
    return (
        <ul className="db-tx-list">
            {transactions.map(tx => (
                <li key={tx.id} className="db-tx-item">
                    <div className="db-tx-left">
                        <span className="db-tx-desc">{tx.description ?? 'Transaction'}</span>
                        <span className="db-tx-date">{new Date(tx.createdAt).toLocaleDateString()}</span>
                    </div>
                    <div className="db-tx-right">
                        <span className={`db-tx-amount ${tx.type}`}>
                            {tx.type === 'debit' ? '-' : '+'}${tx.amount.toFixed(2)} {currency}
                        </span>
                        <span className={`db-tx-status ${tx.status}`}>{tx.status}</span>
                    </div>
                </li>
            ))}
        </ul>
    );
}
