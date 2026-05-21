import { useState, useEffect } from 'react';
import { getTransfers, getTransferStatus, getPendingApprovalTransfers } from '../../../api/transfers';
import type { Transfer, TransferStatus, PendingApprovalTransfer } from '../../../api/transfers';
import TransferStatusCard from '../components/TransferStatusCard';
import PendingApprovalList from '../components/PendingApprovalList';
import { getAccounts } from '../../../api/accounts';
import type { Account } from '../../../api/accounts';
import TransferForm from '../components/TransferForm';
import { useToast } from '../../../context/ToastContext';

type Channel = 'internal' | 'ach' | 'rtp' | 'fednow' | 'swift';

const CHANNEL_INFO: Record<Channel, { label: string; desc: string; settlement: string }> = {
    internal: { label: 'Internal', desc: 'Between your accounts', settlement: 'Instant' },
    ach: { label: 'ACH', desc: 'Standard bank transfer', settlement: 'T+1 business day' },
    rtp: { label: 'RTP', desc: 'Real-time payment', settlement: 'Instant (24/7)' },
    fednow: { label: 'FedNow', desc: 'Instant RTGS settlement', settlement: 'Instant' },
    swift: { label: 'SWIFT', desc: 'International wire transfer', settlement: '1–5 business days' },
};

const ACTIVE_STATUSES = new Set(['pending', 'pending_approval', 'processing']);

export default function TransfersView() {
    const { showToast } = useToast();
    const [transfers, setTransfers] = useState<Transfer[]>([]);
    const [pendingApproval, setPendingApproval] = useState<PendingApprovalTransfer[]>([]);
    const [selected, setSelected] = useState<TransferStatus | null>(null);
    const [loading, setLoading] = useState(true);
    const [detailLoading, setDetailLoading] = useState(false);
    const [showForm, setShowForm] = useState(false);
    const [channel, setChannel] = useState<Channel>('internal');
    const [accounts, setAccounts] = useState<Account[]>([]);

    useEffect(() => {
        Promise.all([
            getTransfers(),
            getPendingApprovalTransfers(),
            getAccounts(),
        ]).then(([all, pending, accs]) => {
            setTransfers(all);
            setPendingApproval(pending);
            setAccounts(accs);
        }).catch(() => {
            showToast('Failed to load transfers. Please try again.');
        }).finally(() => setLoading(false));
    }, []);

    const handleSelect = async (id: string) => {
        if (selected?.transferId === id) { setSelected(null); return; }
        setDetailLoading(true);
        try {
            const data = await getTransferStatus(id);
            setSelected(data);
        } finally {
            setDetailLoading(false);
        }
    };

    const activeTransfers = transfers.filter(t => ACTIVE_STATUSES.has(t.status));

    return (
        <div className="db-view">
            <h1 className="db-view-title">Transfers</h1>

            <div className="db-section db-mb">
                <h2 className="db-section-title">New transfer</h2>
                <div className="db-transfer-grid">
                    {(Object.keys(CHANNEL_INFO) as Channel[]).map(ch => (
                        <button
                            key={ch}
                            className="db-transfer-card"
                            onClick={() => { setChannel(ch); setShowForm(true); }}
                            disabled={accounts.length === 0}
                        >
                            <span className="db-transfer-label">{CHANNEL_INFO[ch].label}</span>
                            <span className="db-transfer-desc">{CHANNEL_INFO[ch].desc}</span>
                            <span className="db-transfer-settlement">{CHANNEL_INFO[ch].settlement}</span>
                        </button>
                    ))}
                </div>
            </div>

            <div className="db-section db-mb">
                <h2 className="db-section-title">Pending approval</h2>
                {loading ? (
                    <div className="db-loading">Loading...</div>
                ) : (
                    <PendingApprovalList
                        transfers={pendingApproval}
                        onResolved={id => setPendingApproval(prev => prev.filter(t => t.id !== id))}
                    />
                )}
            </div>

            <div className="db-section">
                <h2 className="db-section-title">In progress</h2>
                {loading ? (
                    <div className="db-loading">Loading...</div>
                ) : activeTransfers.length === 0 ? (
                    <p className="db-empty">No transfers in progress.</p>
                ) : (
                    <div className="db-transfer-list">
                        {activeTransfers.map(t => (
                                <div key={t.id} style={{ display: 'contents' }}>
                                    <button
                                        className={`db-transfer-row${selected?.transferId === t.id ? ' active' : ''}`}
                                        onClick={() => handleSelect(t.id)}
                                    >
                                        <div className="db-transfer-row-left">
                                            <span className="db-transfer-channel">{t.channel.toUpperCase()}</span>
                                            <span className="db-tx-date">{new Date(t.createdAt).toLocaleDateString()}</span>
                                        </div>
                                        <div className="db-transfer-row-right">
                                            <span className="db-tx-amount debit">${t.amount.toFixed(2)} {t.currency}</span>
                                            <span className={`db-tx-status ${t.status.toLowerCase()}`}>{t.status}</span>
                                        </div>
                                    </button>
                                    {selected?.transferId === t.id && (
                                        detailLoading
                                            ? <div className="db-loading" style={{ padding: '12px 16px' }}>Loading details...</div>
                                            : <TransferStatusCard status={selected} />
                                    )}
                                </div>
                            ))}
                        </div>
                    )}
            </div>

            {showForm && accounts.length > 0 && (
                <TransferForm
                    accounts={accounts}
                    initialChannel={channel}
                    onSuccess={() => {
                        setShowForm(false);
                        getTransfers().then(setTransfers);
                    }}
                    onCancel={() => setShowForm(false)}
                />
            )}
        </div>
    );
}
