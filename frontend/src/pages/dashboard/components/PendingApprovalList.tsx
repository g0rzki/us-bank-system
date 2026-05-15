import type { PendingApprovalTransfer } from '../../../api/transfers';

export default function PendingApprovalList({ transfers }: { transfers: PendingApprovalTransfer[] }) {
    if (transfers.length === 0)
        return <p className="db-empty">No transfers awaiting approval.</p>;

    return (
        <div className="db-transfer-list">
            {transfers.map(t => (
                <div key={t.id} className="db-transfer-row db-pending-approval-row">
                    <div className="db-transfer-row-left">
                        <span className="db-transfer-channel">{t.channel.toUpperCase()}</span>
                        <span className="db-stat-label">From: {t.fromAccountNumber}</span>
                        {t.description && <span className="db-tx-date">{t.description}</span>}
                        <span className="db-tx-date">{new Date(t.createdAt).toLocaleString()}</span>
                    </div>
                    <div className="db-transfer-row-right">
                        <span className="db-tx-amount debit">{t.amount.toFixed(2)} {t.currency}</span>
                        <span className="db-tx-status pending_approval">pending approval</span>
                    </div>
                </div>
            ))}
        </div>
    );
}
