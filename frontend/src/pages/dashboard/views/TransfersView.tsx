import { useState, useEffect } from 'react';
import { getTransfers, getTransferStatus } from '../../../api/transfers';
import type { Transfer, TransferStatus } from '../../../api/transfers';
import TransferStatusCard from '../components/TransferStatusCard';

export default function TransfersView() {
    const [transfers, setTransfers] = useState<Transfer[]>([]);
    const [selected, setSelected] = useState<TransferStatus | null>(null);
    const [loading, setLoading] = useState(true);
    const [detailLoading, setDetailLoading] = useState(false);

    useEffect(() => {
        getTransfers().then(setTransfers).finally(() => setLoading(false));
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

    return (
        <div className="db-view">
            <h1 className="db-view-title">Transfers</h1>

            <div className="db-section db-mb">
                <h2 className="db-section-title">Transfer history</h2>
                {loading ? (
                    <div className="db-loading">Loading...</div>
                ) : transfers.length === 0 ? (
                    <p className="db-empty">No transfers yet.</p>
                ) : (
                    <div className="db-transfer-list">
                        {transfers.map(t => (
                            <button
                                key={t.id}
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
                        ))}
                    </div>
                )}
                {detailLoading && <div className="db-loading">Loading details...</div>}
                {selected && !detailLoading && <TransferStatusCard status={selected} />}
            </div>

            <div className="db-section">
                <h2 className="db-section-title">New transfer</h2>
                <div className="db-transfer-grid">
                    {[
                        { label: 'Internal transfer', desc: 'Between your accounts' },
                        { label: 'ACH transfer', desc: 'Standard bank transfer (T+1)' },
                        { label: 'RTP transfer', desc: 'Real-time payment' },
                        { label: 'FedNow', desc: 'Instant RTGS settlement' },
                        { label: 'SWIFT', desc: 'International wire transfer' },
                    ].map(t => (
                        <button key={t.label} className="db-transfer-card" disabled>
                            <span className="db-transfer-label">{t.label}</span>
                            <span className="db-transfer-desc">{t.desc}</span>
                            <span className="db-coming-soon">Coming soon</span>
                        </button>
                    ))}
                </div>
            </div>
        </div>
    );
}
