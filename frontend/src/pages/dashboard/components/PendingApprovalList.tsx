import { useState } from 'react';
import { approveTransfer, rejectTransfer } from '../../../api/transfers';
import type { PendingApprovalTransfer } from '../../../api/transfers';
import { useToast } from '../../../context/ToastContext';

export default function PendingApprovalList({
    transfers,
    onResolved,
}: {
    transfers: PendingApprovalTransfer[];
    onResolved: (id: string) => void;
}) {
    const { showToast } = useToast();
    const [selected, setSelected] = useState<PendingApprovalTransfer | null>(null);
    const [loading, setLoading] = useState(false);

    const handle = async (action: 'approve' | 'reject') => {
        if (!selected) return;
        setLoading(true);
        try {
            if (action === 'approve') await approveTransfer(selected.id);
            else await rejectTransfer(selected.id);
            onResolved(selected.id);
            setSelected(null);
        } catch {
            showToast('Action failed. Please try again.');
        } finally {
            setLoading(false);
        }
    };

    return (
        <>
            {transfers.length === 0 ? (
                <p className="db-empty">No transfers awaiting approval.</p>
            ) : (
            <div className="db-transfer-list">
                {transfers.map(t => (
                    <button
                        key={t.id}
                        className="db-transfer-row"
                        onClick={() => setSelected(t)}
                    >
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
                    </button>
                ))}
            </div>
            )}

            {selected && (
                <div className="db-modal-overlay" onClick={() => !loading && setSelected(null)}>
                    <div className="db-modal" onClick={e => e.stopPropagation()}>
                        <h2 className="db-section-title">Transfer details</h2>
                        <div className="db-form-field">
                            <span className="db-label">From account</span>
                            <span className="db-value-static">{selected.fromAccountNumber}</span>
                        </div>
                        <div className="db-form-field">
                            <span className="db-label">Amount</span>
                            <span className="db-value-static">{selected.amount.toFixed(2)} {selected.currency}</span>
                        </div>
                        <div className="db-form-field">
                            <span className="db-label">Channel</span>
                            <span className="db-value-static">{selected.channel.toUpperCase()}</span>
                        </div>
                        {selected.description && (
                            <div className="db-form-field">
                                <span className="db-label">Description</span>
                                <span className="db-value-static">{selected.description}</span>
                            </div>
                        )}
                        <div className="db-form-field">
                            <span className="db-label">Date</span>
                            <span className="db-value-static">{new Date(selected.createdAt).toLocaleString()}</span>
                        </div>
                        <div className="db-form-actions">
                            <button className="db-btn-primary" onClick={() => handle('approve')} disabled={loading}>
                                {loading ? '…' : 'Approve'}
                            </button>
                            <button className="db-btn-secondary" onClick={() => handle('reject')} disabled={loading}>
                                Reject
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </>
    );
}
