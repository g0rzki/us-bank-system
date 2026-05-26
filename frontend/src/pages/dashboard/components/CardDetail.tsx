import { useState } from 'react';
import { X } from 'lucide-react';
import { updateCardStatus, updateCardLimits } from '../../../api/accounts';
import type { Card } from '../../../api/accounts';
import { useToast } from '../../../context/ToastContext';

const STATUS_LABELS: Record<string, string> = {
    active: 'Active',
    blocked: 'Blocked',
    expired: 'Expired',
};

function formatCooldown(blockedAt: string): string | null {
    const unblockAt = new Date(new Date(blockedAt).getTime() + 24 * 60 * 60 * 1000);
    const remaining = unblockAt.getTime() - Date.now();
    if (remaining <= 0) return null;
    const h = Math.floor(remaining / 3600000);
    const m = Math.floor((remaining % 3600000) / 60000);
    return h > 0 ? `${h}h ${m}m` : `${m}m`;
}

export default function CardDetail({ card: initial, accountId, canEditLimits = true, onClose, onUpdated }: {
    card: Card;
    accountId: string;
    canEditLimits?: boolean;
    onClose: () => void;
    onUpdated: (card: Card) => void;
}) {
    const { showToast } = useToast();
    const [card, setCard] = useState(initial);
    const [loading, setLoading] = useState(false);
    const [editingLimits, setEditingLimits] = useState(false);
    const [dailyLimit, setDailyLimit] = useState('');
    const [monthlyLimit, setMonthlyLimit] = useState('');
    const [limitsLoading, setLimitsLoading] = useState(false);

    const cooldown = card.status === 'blocked' && card.blockedAt ? formatCooldown(card.blockedAt) : null;
    const canUnblock = card.status === 'blocked' && cooldown === null;
    const toggleStatus = card.status === 'active' ? 'blocked' : 'active';

    const handleToggle = async () => {
        setLoading(true);
        try {
            const updated = await updateCardStatus(accountId, card.id, toggleStatus);
            setCard(updated);
            onUpdated(updated);
            showToast(`Card ${toggleStatus === 'blocked' ? 'blocked' : 'unblocked'} successfully`);
        } catch (e: any) {
            showToast(e?.response?.data?.detail ?? e?.response?.data?.message ?? 'Failed to update card status');
        } finally {
            setLoading(false);
        }
    };

    const handleSaveLimits = async () => {
        if (!dailyLimit && !monthlyLimit) {
            showToast('Provide at least one limit');
            return;
        }
        const d = dailyLimit ? parseFloat(dailyLimit) : card.dailyLimit;
        const m = monthlyLimit ? parseFloat(monthlyLimit) : card.monthlyLimit;
        if (d != null && m != null && m < d) {
            showToast('Monthly limit cannot be less than daily limit');
            return;
        }
        setLimitsLoading(true);
        try {
            const updated = await updateCardLimits(
                accountId,
                card.id,
                dailyLimit ? parseFloat(dailyLimit) : undefined,
                monthlyLimit ? parseFloat(monthlyLimit) : undefined,
            );
            setCard(updated);
            onUpdated(updated);
            setEditingLimits(false);
            setDailyLimit('');
            setMonthlyLimit('');
            showToast('Limits updated');
        } catch (e: any) {
            showToast(e?.response?.data?.detail ?? e?.response?.data?.message ?? 'Failed to update limits');
        } finally {
            setLimitsLoading(false);
        }
    };

    const rows = [
        { label: 'Number', value: `•••• •••• •••• ${card.last4}` },
        { label: 'Type', value: card.type.charAt(0).toUpperCase() + card.type.slice(1) },
        { label: 'Status', value: STATUS_LABELS[card.status] ?? card.status },
        { label: 'Expires', value: new Date(card.expiresAt).toLocaleDateString('en-US', { month: '2-digit', year: 'numeric' }) },
        { label: 'Added', value: new Date(card.createdAt).toLocaleDateString('en-US', { year: 'numeric', month: 'long', day: 'numeric' }) },
    ];

    return (
        <div className="db-modal-overlay" onClick={onClose}>
            <div className="db-modal" onClick={e => e.stopPropagation()}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <h2 className="db-section-title" style={{ margin: 0 }}>Card details</h2>
                    <button onClick={onClose} style={{ background: 'none', border: 'none', cursor: 'pointer', color: 'var(--color-text-muted)', padding: 4 }}>
                        <X size={18} />
                    </button>
                </div>

                <div className="db-card-detail-rows">
                    {rows.map(({ label, value }) => (
                        <div key={label} className="db-account-popup-row">
                            <span className="db-account-popup-label">{label}</span>
                            <span className={`db-account-popup-value${label === 'Status' ? ` db-card-status-${card.status}` : ''}`}>
                                {value}
                            </span>
                        </div>
                    ))}

                    <div className="db-account-popup-row">
                        <span className="db-account-popup-label">Daily limit</span>
                        <span className="db-account-popup-value">
                            {card.dailyLimit != null ? `$${card.dailyLimit.toFixed(2)}` : 'No limit'}
                        </span>
                    </div>
                    <div className="db-account-popup-row">
                        <span className="db-account-popup-label">Monthly limit</span>
                        <span className="db-account-popup-value">
                            {card.monthlyLimit != null ? `$${card.monthlyLimit.toFixed(2)}` : 'No limit'}
                        </span>
                    </div>
                </div>

                {canEditLimits && card.status !== 'expired' && (
                    editingLimits ? (
                        <div style={{ marginTop: 12, display: 'flex', flexDirection: 'column', gap: 8 }}>
                            <div className="db-form-field">
                                <span className="db-label">Daily limit ($10–$10,000)</span>
                                <input
                                    className="db-input"
                                    type="number" min="10" max="10000" step="1"
                                    placeholder={card.dailyLimit != null ? `Current: $${card.dailyLimit}` : 'e.g. 50'}
                                    value={dailyLimit}
                                    onChange={e => setDailyLimit(e.target.value)}
                                />
                            </div>
                            <div className="db-form-field">
                                <span className="db-label">Monthly limit ($50–$100,000)</span>
                                <input
                                    className="db-input"
                                    type="number" min="50" max="100000" step="1"
                                    placeholder={card.monthlyLimit != null ? `Current: $${card.monthlyLimit}` : 'e.g. 200'}
                                    value={monthlyLimit}
                                    onChange={e => setMonthlyLimit(e.target.value)}
                                />
                            </div>
                            <div className="db-form-actions">
                                <button className="db-btn-secondary" onClick={() => { setEditingLimits(false); setDailyLimit(''); setMonthlyLimit(''); }} disabled={limitsLoading}>
                                    Cancel
                                </button>
                                <button className="db-btn-primary" onClick={handleSaveLimits} disabled={limitsLoading}>
                                    {limitsLoading ? 'Saving…' : 'Save limits'}
                                </button>
                            </div>
                        </div>
                    ) : (
                        <button className="db-btn-secondary" style={{ fontSize: '0.875rem', marginTop: 8 }} onClick={() => setEditingLimits(true)}>
                            Edit limits
                        </button>
                    )
                )}

                {card.status === 'active' && (
                    <button className="db-btn-danger" style={{ marginTop: 8 }} onClick={handleToggle} disabled={loading}>
                        {loading ? 'Updating…' : 'Block card'}
                    </button>
                )}

                {card.status === 'blocked' && (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 6, marginTop: 8 }}>
                        <button className="db-btn-primary" onClick={handleToggle} disabled={!canUnblock || loading}>
                            {loading ? 'Updating…' : 'Unblock card'}
                        </button>
                        {cooldown && (
                            <span style={{ fontSize: 'var(--font-size-sm)', color: 'var(--color-text-muted)', textAlign: 'center' }}>
                                Available in {cooldown}
                            </span>
                        )}
                    </div>
                )}
            </div>
        </div>
    );
}
