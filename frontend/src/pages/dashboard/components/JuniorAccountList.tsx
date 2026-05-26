import { useState } from 'react';
import { CreditCard } from 'lucide-react';
import { addJuniorCard } from '../../../api/accounts';
import type { JuniorAccount } from '../../../api/accounts';
import { approveTransfer, rejectTransfer } from '../../../api/transfers';
import type { PendingApprovalTransfer } from '../../../api/transfers';
import { useToast } from '../../../context/ToastContext';

function formatLimit(value: number | null): string {
    return value !== null ? `$${value.toFixed(2)}` : '—';
}

function calcAge(dateOfBirth: string): number {
    const dob = new Date(dateOfBirth);
    const today = new Date();
    let age = today.getFullYear() - dob.getFullYear();
    if (today < new Date(today.getFullYear(), dob.getMonth(), dob.getDate())) age--;
    return age;
}

function PendingRow({ transfer, onAction }: { transfer: PendingApprovalTransfer; onAction: () => void }) {
    const { showToast } = useToast();
    const [acting, setActing] = useState(false);

    const handle = async (action: 'approve' | 'reject') => {
        setActing(true);
        try {
            if (action === 'approve') await approveTransfer(transfer.id);
            else await rejectTransfer(transfer.id);
            showToast(action === 'approve' ? 'Transfer approved' : 'Transfer rejected');
            onAction();
        } catch (e: any) {
            showToast(e?.response?.data?.detail ?? e?.response?.data?.message ?? 'Action failed');
        } finally {
            setActing(false);
        }
    };

    return (
        <div className="db-transfer-item" style={{ paddingLeft: 16, borderLeft: '2px solid var(--color-border)' }}>
            <div className="db-transfer-row" style={{ cursor: 'default' }}>
                <div className="db-transfer-row-left">
                    <span className="db-transfer-channel">{transfer.channel.toUpperCase()}</span>
                    <span className="db-tx-date">{new Date(transfer.createdAt).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })}</span>
                    {transfer.description && <span className="db-tx-date">{transfer.description}</span>}
                </div>
                <div className="db-transfer-row-right" style={{ gap: 8 }}>
                    <span className="db-tx-amount debit">−${transfer.amount.toFixed(2)} {transfer.currency}</span>
                    <button className="db-btn-primary" style={{ fontSize: '0.75rem', padding: '4px 10px' }} disabled={acting} onClick={() => handle('approve')}>Approve</button>
                    <button className="db-btn-secondary" style={{ fontSize: '0.75rem', padding: '4px 10px' }} disabled={acting} onClick={() => handle('reject')}>Reject</button>
                </div>
            </div>
        </div>
    );
}

export default function JuniorAccountList({ accounts, pendingByAccount = {}, onPendingAction }: {
    accounts: JuniorAccount[];
    pendingByAccount?: Record<string, PendingApprovalTransfer[]>;
    onPendingAction?: () => void;
}) {
    const { showToast } = useToast();
    const [selected, setSelected] = useState<JuniorAccount | null>(null);
    const [localAccounts, setLocalAccounts] = useState(accounts);
    const [addingCard, setAddingCard] = useState(false);
    const [dailyLimit, setDailyLimit] = useState('');
    const [monthlyLimit, setMonthlyLimit] = useState('');
    const [submitting, setSubmitting] = useState(false);

    const open = (acc: JuniorAccount) => {
        setSelected(acc);
        setAddingCard(false);
        setDailyLimit('');
        setMonthlyLimit('');
    };

    const close = () => {
        setSelected(null);
        setAddingCard(false);
    };

    const handleAddCard = async () => {
        if (!selected) return;
        const d = dailyLimit ? parseFloat(dailyLimit) : null;
        const m = monthlyLimit ? parseFloat(monthlyLimit) : null;
        if (d != null && m != null && m < d) {
            showToast('Monthly limit cannot be less than daily limit');
            return;
        }
        setSubmitting(true);
        try {
            const card = await addJuniorCard(
                selected.accountId,
                dailyLimit ? parseFloat(dailyLimit) : undefined,
                monthlyLimit ? parseFloat(monthlyLimit) : undefined,
            );
            const updated = {
                ...selected,
                cardDailyLimit: card.dailyLimit,
                cardMonthlyLimit: card.monthlyLimit,
            };
            setSelected(updated);
            setLocalAccounts(prev => prev.map(a => a.juniorAccountId === selected.juniorAccountId ? updated : a));
            setAddingCard(false);
            showToast('Card added successfully');
        } catch (e: any) {
            showToast(e?.response?.data?.detail ?? e?.response?.data?.message ?? 'Failed to add card');
        } finally {
            setSubmitting(false);
        }
    };

    const hasCard = (acc: JuniorAccount) => acc.cardDailyLimit !== null || acc.cardMonthlyLimit !== null;

    return (
        <>
            <div className="db-transfer-list">
                {localAccounts.map(acc => {
                    const pending = pendingByAccount[acc.accountId] ?? [];
                    return (
                        <div key={acc.juniorAccountId} className="db-transfer-item">
                            <button className="db-transfer-row" onClick={() => open(acc)}>
                                <div className="db-transfer-row-left">
                                    <span className="db-transfer-channel">JUNIOR</span>
                                    <span className="db-stat-label">{acc.firstName} {acc.lastName}</span>
                                    <span className="db-tx-date">••••{acc.accountNumber.slice(-4)} · Age {calcAge(acc.dateOfBirth)} · {acc.status}</span>
                                </div>
                                <div className="db-transfer-row-right">
                                    <span className="db-junior-balance">${acc.balance.toFixed(2)} {acc.currency}</span>
                                    {hasCard(acc)
                                        ? <span className="db-tx-date">Daily {formatLimit(acc.cardDailyLimit)} · Monthly {formatLimit(acc.cardMonthlyLimit)}</span>
                                        : <span className="db-tx-date" style={{ color: 'var(--color-text-muted)' }}>No card</span>
                                    }
                                </div>
                            </button>
                            {pending.length > 0 && (
                                <div style={{ marginTop: 4 }}>
                                    <div style={{ fontSize: 'var(--font-size-sm)', color: 'var(--color-text-muted)', fontWeight: 600, padding: '4px 0 4px 16px' }}>
                                        Pending approvals
                                    </div>
                                    {pending.map(t => (
                                        <PendingRow key={t.id} transfer={t} onAction={onPendingAction ?? (() => {})} />
                                    ))}
                                </div>
                            )}
                        </div>
                    );
                })}
            </div>

            {selected && (
                <div className="db-modal-overlay" onClick={close}>
                    <div className="db-modal" onClick={e => e.stopPropagation()}>
                        {!addingCard ? (
                            <>
                                <h2 className="db-section-title">{selected.firstName} {selected.lastName}</h2>
                                <div className="db-form-field">
                                    <span className="db-label">Account number</span>
                                    <span className="db-value-static">{selected.accountNumber}</span>
                                </div>
                                <div className="db-form-field">
                                    <span className="db-label">Balance</span>
                                    <span className="db-value-static">${selected.balance.toFixed(2)} {selected.currency}</span>
                                </div>
                                <div className="db-form-field">
                                    <span className="db-label">Age</span>
                                    <span className="db-value-static">{calcAge(selected.dateOfBirth)}</span>
                                </div>

                                <div className="db-junior-card-section">
                                    <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginBottom: 8 }}>
                                        <CreditCard size={15} style={{ color: 'var(--color-text-muted)' }} />
                                        <span style={{ fontSize: 'var(--font-size-sm)', fontWeight: 600, color: 'var(--color-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>Prepaid card</span>
                                    </div>
                                    {hasCard(selected) ? (
                                        <>
                                            <div className="db-form-field">
                                                <span className="db-label">Daily limit</span>
                                                <span className="db-value-static">{formatLimit(selected.cardDailyLimit)}</span>
                                            </div>
                                            <div className="db-form-field">
                                                <span className="db-label">Monthly limit</span>
                                                <span className="db-value-static">{formatLimit(selected.cardMonthlyLimit)}</span>
                                            </div>
                                        </>
                                    ) : (
                                        <button className="db-btn-secondary" style={{ fontSize: '0.875rem' }} onClick={() => { setDailyLimit(''); setMonthlyLimit(''); setAddingCard(true); }}>
                                            + Add prepaid card
                                        </button>
                                    )}
                                </div>

                                <div className="db-form-actions">
                                    <button className="db-btn-secondary" onClick={close}>Close</button>
                                </div>
                            </>
                        ) : (
                            <>
                                <h2 className="db-section-title">Add prepaid card — {selected.firstName}</h2>
                                <div className="db-form-field">
                                    <span className="db-label">Daily limit (optional, $10–$10,000)</span>
                                    <input className="db-input" type="number" min="10" max="10000" step="1" placeholder="e.g. 50" value={dailyLimit} onChange={e => setDailyLimit(e.target.value)} />
                                </div>
                                <div className="db-form-field">
                                    <span className="db-label">Monthly limit (optional, $50–$100,000)</span>
                                    <input className="db-input" type="number" min="50" max="100000" step="1" placeholder="e.g. 200" value={monthlyLimit} onChange={e => setMonthlyLimit(e.target.value)} />
                                </div>
                                <div className="db-form-actions">
                                    <button className="db-btn-secondary" onClick={() => setAddingCard(false)} disabled={submitting}>Back</button>
                                    <button className="db-btn-primary" onClick={handleAddCard} disabled={submitting}>
                                        {submitting ? 'Adding…' : 'Add card'}
                                    </button>
                                </div>
                            </>
                        )}
                    </div>
                </div>
            )}
        </>
    );
}
