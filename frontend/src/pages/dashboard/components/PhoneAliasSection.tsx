import { useState, useEffect } from 'react';
import { Smartphone } from 'lucide-react';
import type { Account } from '../../../api/accounts';
import {
    getPhoneAlias,
    registerPhoneAlias,
    deletePhoneAlias,
} from '../../../api/p2p';
import type { PhoneAliasResponse } from '../../../api/p2p';
import { useToast } from '../../../context/ToastContext';
import '../../../styles/PhoneAliasSection.css';

const US_PHONE_RE = /^\+1\d{10}$/;

interface Props {
    accounts: Account[];
}

export default function PhoneAliasSection({ accounts }: Props) {
    const { showToast } = useToast();
    const eligibleAccounts = accounts.filter(a => !a.type.startsWith('junior'));

    const [selectedAccountId, setSelectedAccountId] = useState(eligibleAccounts[0]?.id ?? '');
    const [alias, setAlias] = useState<PhoneAliasResponse | null>(null);
    const [inputPhone, setInputPhone] = useState('');
    const [phoneError, setPhoneError] = useState('');
    const [loading, setLoading] = useState(false);
    const [fetchLoading, setFetchLoading] = useState(false);

    useEffect(() => {
        if (!selectedAccountId) return;
        setAlias(null);
        setInputPhone('');
        setPhoneError('');
        setFetchLoading(true);
        getPhoneAlias(selectedAccountId)
            .then(setAlias)
            .catch(() => setAlias(null))
            .finally(() => setFetchLoading(false));
    }, [selectedAccountId]);

    if (eligibleAccounts.length === 0) return null;

    const handleEnable = async () => {
        if (!US_PHONE_RE.test(inputPhone)) {
            setPhoneError('Enter a valid US phone number in E.164 format: +1 followed by 10 digits');
            return;
        }
        setPhoneError('');
        setLoading(true);
        try {
            const result = await registerPhoneAlias(selectedAccountId, inputPhone);
            setAlias(result);
            setInputPhone('');
            showToast('Phone alias registered successfully', 'success');
        } catch (e: unknown) {
            const err = e as { response?: { status?: number; data?: { detail?: string } } };
            if (err?.response?.status === 409) {
                showToast('Ten numer jest już przypisany w systemie KLIK');
            } else {
                const detail = err?.response?.data?.detail ?? '';
                if (detail.includes('P2P_NOT_ENABLED') || detail.includes('P2P not enabled')) {
                    showToast('Przelew na telefon nie jest aktywowany dla Twojego konta. Skontaktuj się z bankiem.');
                } else {
                    showToast(detail || 'Nie udało się zarejestrować aliasu');
                }
            }
        } finally {
            setLoading(false);
        }
    };

    const handleDisable = async () => {
        setLoading(true);
        try {
            await deletePhoneAlias(selectedAccountId);
            setAlias(null);
            showToast('Phone alias removed', 'success');
        } catch (e: unknown) {
            const err = e as { response?: { status?: number; data?: { detail?: string } } };
            if (err?.response?.status === 404) {
                setAlias(null);
            } else {
                showToast(err?.response?.data?.detail ?? 'Nie udało się usunąć aliasu');
            }
        } finally {
            setLoading(false);
        }
    };

    return (
        <div className="p2p-section">
            <div className="p2p-section-header">
                <Smartphone size={20} color="var(--color-primary)" />
                <h2>Phone Transfer (P2P)</h2>
            </div>

            {eligibleAccounts.length > 1 && (
                <div className="p2p-account-select">
                    <div className="db-form-field">
                        <span className="db-label">Account</span>
                        <select
                            className="db-input"
                            value={selectedAccountId}
                            onChange={e => setSelectedAccountId(e.target.value)}
                        >
                            {eligibleAccounts.map(a => (
                                <option key={a.id} value={a.id}>
                                    {a.accountNumber} ({a.type}) — ${a.balance.toFixed(2)}
                                </option>
                            ))}
                        </select>
                    </div>
                </div>
            )}

            {fetchLoading ? (
                <div className="db-loading">Loading…</div>
            ) : alias ? (
                <div className="p2p-status-row">
                    <span className="p2p-status-badge">✓ Active</span>
                    <span className="p2p-active-phone">{alias.phone}</span>
                    <button
                        className="p2p-btn-danger"
                        onClick={handleDisable}
                        disabled={loading}
                    >
                        {loading ? 'Removing…' : 'Disable'}
                    </button>
                </div>
            ) : (
                <div>
                    <div className="p2p-input-row">
                        <input
                            className="db-input"
                            value={inputPhone}
                            onChange={e => { setInputPhone(e.target.value); setPhoneError(''); }}
                            placeholder="+15551234567"
                            disabled={loading}
                        />
                        <button
                            className="db-btn-primary"
                            onClick={handleEnable}
                            disabled={loading}
                        >
                            {loading ? 'Enabling…' : 'Enable P2P transfer'}
                        </button>
                    </div>
                    {phoneError && <p className="p2p-phone-error">{phoneError}</p>}
                    <p className="p2p-hint">
                        Register your US phone number (+1XXXXXXXXXX) to receive transfers by phone.
                    </p>
                </div>
            )}
        </div>
    );
}
