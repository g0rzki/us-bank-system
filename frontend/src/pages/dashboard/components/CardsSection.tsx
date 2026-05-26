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

const EMPTY_FORM: FormState = { type: 'debit', dailyLimit: '', monthlyLimit: '' };

export default function CardsSection({ accountId }: { accountId: string }) {
    const { showToast } = useToast();
    const [cards, setCards] = useState<Card[]>([]);
    const [loading, setLoading] = useState(true);
    const [showForm, setShowForm] = useState(false);
    const [form, setForm] = useState<FormState>(EMPTY_FORM);
    const [submitting, setSubmitting] = useState(false);
    const [selectedCard, setSelectedCard] = useState<Card | null>(null);

    useEffect(() => {
        setLoading(true);
        getCards(accountId)
            .then(setCards)
            .catch((e: any) => showToast(e?.response?.data?.detail ?? e?.response?.data?.message ?? 'Failed to load cards'))
            .finally(() => setLoading(false));
    }, [accountId]);

    const handleSubmit = async () => {
        setSubmitting(true);
        try {
            const card = await registerCard(accountId, {
                type: form.type,
                dailyLimit: form.dailyLimit ? parseFloat(form.dailyLimit) : undefined,
                monthlyLimit: form.monthlyLimit ? parseFloat(form.monthlyLimit) : undefined,
            });
            setCards(prev => [card, ...prev]);
            setShowForm(false);
            setForm(EMPTY_FORM);
            showToast('Card registered successfully');
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

    return (
        <div className="db-section db-mt">
            <div className="db-section-header">
                <h2>Cards</h2>
                {!showForm && (
                    <button className="db-btn-secondary" style={{ fontSize: '0.875rem', display: 'flex', alignItems: 'center', gap: 4 }} onClick={() => setShowForm(true)}>
                        <Plus size={14} /> Add card
                    </button>
                )}
            </div>

            {showForm && (
                <div className="db-modal-overlay" onClick={() => !submitting && setShowForm(false)}>
                    <div className="db-modal" onClick={e => e.stopPropagation()}>
                        <h2 className="db-section-title">Register card</h2>
                        <div className="db-form-field">
                            <span className="db-label">Card type</span>
                            <select className="db-input" {...field('type')}>
                                {CARD_TYPES.map(t => (
                                    <option key={t} value={t}>{t.charAt(0).toUpperCase() + t.slice(1)}</option>
                                ))}
                            </select>
                        </div>
                        <div className="db-form-field">
                            <span className="db-label">Daily limit (optional, $10–$10,000)</span>
                            <input className="db-input" type="number" min="10" max="10000" step="1" placeholder="e.g. 500" {...field('dailyLimit')} />
                        </div>
                        <div className="db-form-field">
                            <span className="db-label">Monthly limit (optional, $50–$100,000)</span>
                            <input className="db-input" type="number" min="50" max="100000" step="1" placeholder="e.g. 2000" {...field('monthlyLimit')} />
                        </div>
                        <div className="db-form-actions">
                            <button className="db-btn-secondary" onClick={() => { setShowForm(false); setForm(EMPTY_FORM); }} disabled={submitting}>
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
