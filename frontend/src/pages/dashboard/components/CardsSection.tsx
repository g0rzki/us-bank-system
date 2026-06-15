import { useState, useEffect } from 'react';
import { CreditCard, Plus } from 'lucide-react';
import { getCards, registerCard } from '../../../api/accounts';
import type { Card } from '../../../api/accounts';
import { useToast } from '../../../context/ToastContext';
import CardDetail from './CardDetail';

const CARD_TYPES = ['debit', 'prepaid'] as const;

function CardItem({ card, onClick }: { card: Card; onClick: () => void }) {
    const expires = new Date(card.expiresAt).toLocaleDateString('en-US', { month: '2-digit', year: '2-digit' });
    return (
        <div className="db-card-item" onClick={onClick} style={{ cursor: 'pointer' }}>
            <div className="db-card-item-icon">
                <CreditCard size={18} />
            </div>
            <div className="db-card-item-info">
                <span className="db-card-item-name">
                    {card.type.charAt(0).toUpperCase() + card.type.slice(1)} •••• {card.last4}
                </span>
                <span className="db-card-item-meta">
                    Expires {expires}
                    {card.dailyLimit != null && <> · Daily ${card.dailyLimit.toFixed(0)}</>}
                    {card.monthlyLimit != null && <> · Monthly ${card.monthlyLimit.toFixed(0)}</>}
                </span>
            </div>
            <span className={`db-card-status-${card.status} db-card-status-badge`}>
                {card.status.charAt(0).toUpperCase() + card.status.slice(1)}
            </span>
        </div>
    );
}

interface FormState {
    type: string;
    dailyLimit: string;
    monthlyLimit: string;
}

export default function CardsSection({ accountId, isJunior = false }: { accountId: string; isJunior?: boolean }) {
    const { showToast } = useToast();
    const [cards, setCards] = useState<Card[]>([]);
    const [loading, setLoading] = useState(true);
    const [showForm, setShowForm] = useState(false);
    const [form, setForm] = useState<FormState>({ type: isJunior ? 'prepaid' : 'debit', dailyLimit: '', monthlyLimit: '' });

    const [submitting, setSubmitting] = useState(false);
    const [selectedCard, setSelectedCard] = useState<Card | null>(null);

    useEffect(() => {
        setLoading(true);
        getCards(accountId)
            .then(setCards)
            .catch((e: any) => showToast(e?.response?.data?.detail ?? e?.response?.data?.message ?? 'Failed to load cards'))
            .finally(() => setLoading(false));
    }, [accountId]);

    // isJunior może zmienić się asynchronicznie po załadowaniu tabs — resetujemy typ
    useEffect(() => {
        setForm(prev => ({ ...prev, type: isJunior ? 'prepaid' : prev.type }));
    }, [isJunior]);

    const handleSubmit = async () => {
        const d = form.dailyLimit ? parseFloat(form.dailyLimit) : null;
        const m = form.monthlyLimit ? parseFloat(form.monthlyLimit) : null;
        if (d != null && m != null && m < d) {
            showToast('Monthly limit cannot be less than daily limit');
            return;
        }
        setSubmitting(true);
        try {
            const card = await registerCard(accountId, {
                type: isJunior ? 'prepaid' : form.type,
                dailyLimit: form.dailyLimit ? parseFloat(form.dailyLimit) : undefined,
                monthlyLimit: form.monthlyLimit ? parseFloat(form.monthlyLimit) : undefined,
            });
            setCards(prev => [card, ...prev]);
            setShowForm(false);
            setForm({ type: isJunior ? 'prepaid' : 'debit', dailyLimit: '', monthlyLimit: '' });
            showToast('Card registered successfully', 'success');
        } catch (e: any) {
            showToast(e?.response?.data?.detail ?? e?.response?.data?.message ?? 'Failed to register card');
        } finally {
            setSubmitting(false);
        }
    };

    const field = (key: keyof FormState) => ({
        value: form[key],
        onChange: (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) =>
            setForm(prev => ({ ...prev, [key]: e.target.value })),
    });

    const hasActiveDebit = cards.some(c => c.type === 'debit' && c.status !== 'expired');
    const hasActivePrepaid = cards.some(c => c.type === 'prepaid' && c.status !== 'expired');
    const availableTypes = CARD_TYPES.filter(t => t === 'debit' ? !hasActiveDebit : !hasActivePrepaid);
    const canAddCard = !loading && (isJunior ? !cards.some(c => c.status !== 'expired') : availableTypes.length > 0);

    return (
        <div className="db-section db-mt">
            <div className="db-section-header">
                <h2>Cards</h2>
                {canAddCard && (
                    <button
                        className="db-btn-secondary"
                        style={{ fontSize: '0.875rem', display: 'flex', alignItems: 'center', gap: 4 }}
                        onClick={() => { setForm(prev => ({ ...prev, type: availableTypes[0] ?? 'debit' })); setShowForm(true); }}
                    >
                        <Plus size={14} /> Add card
                    </button>
                )}
            </div>

            {showForm && (
                <div className="db-modal-overlay" onClick={() => !submitting && setShowForm(false)}>
                    <div className="db-modal" onClick={e => e.stopPropagation()}>
                        <h2 className="db-section-title">{isJunior ? 'Add prepaid card' : 'Register card'}</h2>
                        {!isJunior && (
                            <div className="db-form-field">
                                <span className="db-label">Card type</span>
                                <select className="db-input" {...field('type')}>
                                    {availableTypes.map(t => (
                                        <option key={t} value={t}>{t.charAt(0).toUpperCase() + t.slice(1)}</option>
                                    ))}
                                </select>
                            </div>
                        )}
                        <div className="db-form-field">
                            <span className="db-label">Daily limit (optional, $10–$10,000)</span>
                            <input className="db-input" type="number" min="10" max="10000" step="1" placeholder="e.g. 500" {...field('dailyLimit')} />
                        </div>
                        <div className="db-form-field">
                            <span className="db-label">Monthly limit (optional, $50–$100,000)</span>
                            <input className="db-input" type="number" min="50" max="100000" step="1" placeholder="e.g. 2000" {...field('monthlyLimit')} />
                        </div>
                        <div className="db-form-actions">
                            <button className="db-btn-secondary" onClick={() => { setShowForm(false); setForm({ type: isJunior ? 'prepaid' : 'debit', dailyLimit: '', monthlyLimit: '' }); }} disabled={submitting}>
                                Cancel
                            </button>
                            <button className="db-btn-primary" onClick={handleSubmit} disabled={submitting}>
                                {submitting ? 'Registering…' : 'Register card'}
                            </button>
                        </div>
                    </div>
                </div>
            )}

            {loading ? (
                <div className="db-loading">Loading...</div>
            ) : cards.length === 0 ? (
                <p className="db-empty">No cards linked to this account.</p>
            ) : (
                <div className="db-card-list">
                    {cards.map(card => (
                        <CardItem key={card.id} card={card} onClick={() => setSelectedCard(card)} />
                    ))}
                </div>
            )}

            {selectedCard && (
                <CardDetail
                    card={selectedCard}
                    accountId={accountId}
                    onClose={() => setSelectedCard(null)}
                    onUpdated={updated => {
                        setCards(prev => prev.map(c => c.id === updated.id ? updated : c));
                        setSelectedCard(updated);
                    }}
                />
            )}
        </div>
    );
}
