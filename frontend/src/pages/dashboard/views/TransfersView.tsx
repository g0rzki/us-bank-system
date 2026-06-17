import { useState, useEffect } from 'react';
import { getTransfers, getTransferStatus, getPendingApprovalTransfers } from '../../../api/transfers';
import type { Transfer, TransferStatus, PendingApprovalTransfer } from '../../../api/transfers';
import TransferStatusCard from '../components/TransferStatusCard';
import PendingApprovalList from '../components/PendingApprovalList';
import { getAccounts, getJuniorAccounts } from '../../../api/accounts';
import type { Account } from '../../../api/accounts';
import TransferForm from '../components/TransferForm';
import { useToast } from '../../../context/ToastContext';
import { Building2, Banknote, Zap, Timer, Globe, Smartphone } from 'lucide-react';

type Channel = 'internal' | 'ach' | 'rtp' | 'fednow' | 'swift' | 'p2p';

const CHANNEL_INFO: Record<Channel, { label: string; desc: string; settlement: string; icon: React.ReactNode }> = {
    internal: { label: 'Internal', desc: 'Between your accounts', settlement: 'Instant', icon: <Building2 size={16} /> },
    ach: { label: 'ACH', desc: 'Standard bank transfer', settlement: 'T+1 business day', icon: <Banknote size={16} /> },
    rtp: { label: 'RTP', desc: 'Real-time payment', settlement: 'Instant (24/7)', icon: <Zap size={16} /> },
    fednow: { label: 'FedNow', desc: 'Instant RTGS settlement', settlement: 'Instant', icon: <Timer size={16} /> },
    swift: { label: 'SWIFT', desc: 'International wire transfer', settlement: '1–5 business days', icon: <Globe size={16} /> },
    p2p: { label: 'P2P', desc: 'Send to phone number', settlement: 'Instant', icon: <Smartphone size={16} /> },
};

const ACTIVE_STATUSES = new Set(['pending', 'pending_approval', 'processing']);

export default function TransfersView() {
    const { showToast } = useToast();
    const [transfers, setTransfers] = useState<Transfer[]>([]);
    const [pendingApproval, setPendingApproval] = useState<PendingApprovalTransfer[]>([]);
    const [selected, setSelected] = useState<TransferStatus | null>(null);
    const [loading, setLoading] = useState(true);
    const [detailLoading, setDetailLoading] = useState(false);
    const [channel, setChannel] = useState<Channel>('internal');
    const [accounts, setAccounts] = useState<Account[]>([]);

    useEffect(() => {
        const load = async () => {
            const [all, pending, accs] = await Promise.all([
                getTransfers(),
                getPendingApprovalTransfers(),
                getAccounts(),
            ]);
            setTransfers(all);
            setPendingApproval(pending);
            const juniorResults = await Promise.all(accs.map(a => getJuniorAccounts(a.id)));
            const seen = new Set<string>();
            const juniorAsAccounts: Account[] = juniorResults.flat()
                .filter(j => seen.has(j.juniorAccountId) ? false : seen.add(j.juniorAccountId) && true)
                .map(j => ({
                    id: j.accountId,
                    accountNumber: j.accountNumber,
                    type: `junior (${j.firstName})`,
                    balance: j.balance,
                    currency: j.currency,
                    status: j.status,
                    createdAt: j.createdAt,
                }));
            setAccounts([...accs, ...juniorAsAccounts]);
        };
        load().catch(() => showToast('Failed to load transfers. Please try again.')).finally(() => setLoading(false));
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
            <div className="db-section db-mb">
                <h2 className="db-section-title">New transfer</h2>
                <div className="db-surface">
                    <div className="db-channel-tabs">
                        {(Object.keys(CHANNEL_INFO) as Channel[]).map(ch => (
                            <button
                                key={ch}
                                className={`db-channel-tab${channel === ch ? ' active' : ''}`}
                                onClick={() => setChannel(ch)}
                                disabled={accounts.length === 0}
                            >
                                <span className="db-channel-tab-icon">{CHANNEL_INFO[ch].icon}</span>
                                <span className="db-channel-tab-label">{CHANNEL_INFO[ch].label}</span>
                                <span className="db-channel-tab-desc">{CHANNEL_INFO[ch].desc}</span>
                                <span className="db-channel-tab-settlement">{CHANNEL_INFO[ch].settlement}</span>
                            </button>
                        ))}
                    </div>
                    <div className="db-channel-form">
                        {accounts.length === 0 ? (
                            <p className="db-empty">You need an account to make a transfer.</p>
                        ) : (
                            <TransferForm
                                accounts={accounts}
                                channel={channel}
                                onSuccess={() => getTransfers().then(setTransfers)}
                            />
                        )}
                    </div>
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
        </div>
    );
}
