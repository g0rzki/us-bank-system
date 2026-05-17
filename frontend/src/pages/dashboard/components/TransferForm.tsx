import { useState } from 'react';
import type { Account } from '../../../api/accounts';
import {
    createInternalTransfer,
    createAchTransfer,
    createRtpTransfer,
    createFedNowTransfer,
    createSwiftTransfer,
} from '../../../api/transfers';
import '../../../styles/TransferForm.css';

type Channel = 'internal' | 'ach' | 'rtp' | 'fednow' | 'swift';

interface TransferFormProps {
    accounts: Account[];
    onSuccess: () => void;
    onCancel: () => void;
    initialChannel?: Channel;
}

const CHANNEL_INFO: Record<Channel, { label: string; desc: string; settlement: string }> = {
    internal: { label: 'Internal', desc: 'Between your accounts', settlement: 'Instant' },
    ach: { label: 'ACH', desc: 'Standard bank transfer', settlement: 'T+1 business day' },
    rtp: { label: 'RTP', desc: 'Real-time payment', settlement: 'Instant (24/7)' },
    fednow: { label: 'FedNow', desc: 'Instant RTGS settlement', settlement: 'Instant' },
    swift: { label: 'SWIFT', desc: 'International wire transfer', settlement: '1–5 business days' },
};

export default function TransferForm({ accounts, onSuccess, onCancel, initialChannel }: TransferFormProps) {
    const [channel, setChannel] = useState<Channel>(initialChannel ?? 'internal');
    const [fromAccountId, setFromAccountId] = useState(accounts[0]?.id ?? '');
    const [toAccountId, setToAccountId] = useState('');
    const [amount, setAmount] = useState('');
    const [description, setDescription] = useState('');
    const [toRoutingNumber, setToRoutingNumber] = useState('');
    const [toAccountNumber, setToAccountNumber] = useState('');
    const [iban, setIban] = useState('');
    const [bic, setBic] = useState('');
    const [beneficiaryName, setBeneficiaryName] = useState('');
    const [beneficiaryAddress, setBeneficiaryAddress] = useState('');
    const [chargeBearer, setChargeBearer] = useState('SHA');
    const [remittanceInfo, setRemittanceInfo] = useState('');
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const info = CHANNEL_INFO[channel];

    const handleSubmit = async () => {
        setError(null);
        const amt = parseFloat(amount);
        if (!amount || isNaN(amt) || amt <= 0) {
            setError('Enter a valid amount');
            return;
        }

        setLoading(true);
        try {
            if (channel === 'internal') {
                if (!toAccountId) { setError('Select destination account'); return; }
                await createInternalTransfer({ fromAccountId, toAccountId, amount: amt, currency: 'USD', description: description || undefined });
            } else if (channel === 'ach') {
                if (!toRoutingNumber || !toAccountNumber) { setError('Routing and account numbers are required'); return; }
                await createAchTransfer({ fromAccountId, toRoutingNumber, toAccountNumber, amount: amt, currency: 'USD', description: description || undefined });
            } else if (channel === 'rtp') {
                if (!toAccountNumber) { setError('Enter recipient account number'); return; }
                await createRtpTransfer({ fromAccountId, toAccountNumber, amount: amt, currency: 'USD', description: description || undefined });
            } else if (channel === 'fednow') {
                if (!toAccountNumber) { setError('Enter recipient account number'); return; }
                await createFedNowTransfer({ fromAccountId, toAccountNumber, amount: amt, currency: 'USD', description: description || undefined });
            } else if (channel === 'swift') {
                if (!iban || !bic || !beneficiaryName) { setError('IBAN, BIC and beneficiary name are required'); return; }
                await createSwiftTransfer({ fromAccountId, iban, bic, beneficiaryName, beneficiaryAddress: beneficiaryAddress || undefined, amount: amt, currency: 'USD', chargeBearer, remittanceInfo: remittanceInfo || undefined, description: description || undefined });
            }
            onSuccess();
        } catch (e: any) {
            setError(e?.response?.data?.detail ?? e?.response?.data?.message ?? 'Transfer failed. Please try again.');
        } finally {
            setLoading(false);
        }
    };

    const toAccounts = accounts.filter(a => a.id !== fromAccountId);

    return (
        <div className="tf-overlay" onClick={onCancel}>
            <div className="tf-modal" onClick={e => e.stopPropagation()}>
                <div className="tf-header">
                    <h2 className="tf-title">New transfer</h2>
                    <button className="tf-close" onClick={onCancel}>✕</button>
                </div>

                <div className="tf-channels">
                    {(Object.keys(CHANNEL_INFO) as Channel[]).map(ch => (
                        <button
                            key={ch}
                            className={`tf-channel-btn${channel === ch ? ' active' : ''}`}
                            onClick={() => { setChannel(ch); setToAccountId(''); setToAccountNumber(''); }}
                        >
                            {CHANNEL_INFO[ch].label}
                        </button>
                    ))}
                </div>

                <div className="tf-settlement">
                    <span className="tf-settlement-label">Settlement:</span>
                    <span className="tf-settlement-value">{info.settlement}</span>
                </div>

                <div className="tf-fields">
                    <div className="tf-field">
                        <label>From account</label>
                        <select value={fromAccountId} onChange={e => setFromAccountId(e.target.value)}>
                            {accounts.map(a => (
                                <option key={a.id} value={a.id}>
                                    {a.accountNumber} ({a.type}) — ${a.balance.toFixed(2)}
                                </option>
                            ))}
                        </select>
                    </div>

                    {channel === 'internal' && (
                        <div className="tf-field">
                            <label>To account</label>
                            <select value={toAccountId} onChange={e => setToAccountId(e.target.value)}>
                                <option value="">Select account</option>
                                {toAccounts.map(a => (
                                    <option key={a.id} value={a.id}>
                                        {a.accountNumber} ({a.type})
                                    </option>
                                ))}
                            </select>
                        </div>
                    )}

                    {(channel === 'rtp' || channel === 'fednow') && (
                        <div className="tf-field">
                            <label>Recipient account number</label>
                            <input
                                value={toAccountNumber}
                                onChange={e => setToAccountNumber(e.target.value)}
                                placeholder="16-digit account number"
                            />
                        </div>
                    )}

                    {channel === 'ach' && (
                        <>
                            <div className="tf-field">
                                <label>Routing number</label>
                                <input value={toRoutingNumber} onChange={e => setToRoutingNumber(e.target.value)} placeholder="9 digits" />
                            </div>
                            <div className="tf-field">
                                <label>Account number</label>
                                <input value={toAccountNumber} onChange={e => setToAccountNumber(e.target.value)} placeholder="Recipient account number" />
                            </div>
                        </>
                    )}

                    {channel === 'swift' && (
                        <>
                            <div className="tf-field">
                                <label>IBAN</label>
                                <input value={iban} onChange={e => setIban(e.target.value)} placeholder="e.g. DE89370400440532013000" />
                            </div>
                            <div className="tf-field">
                                <label>BIC / SWIFT code</label>
                                <input value={bic} onChange={e => setBic(e.target.value)} placeholder="e.g. DEUTDEDB" />
                            </div>
                            <div className="tf-field">
                                <label>Beneficiary name</label>
                                <input value={beneficiaryName} onChange={e => setBeneficiaryName(e.target.value)} placeholder="Full name" />
                            </div>
                            <div className="tf-field">
                                <label>Beneficiary address <span className="tf-optional">(optional)</span></label>
                                <input value={beneficiaryAddress} onChange={e => setBeneficiaryAddress(e.target.value)} placeholder="Street, city, country" />
                            </div>
                            <div className="tf-field">
                                <label>Charge bearer</label>
                                <select value={chargeBearer} onChange={e => setChargeBearer(e.target.value)}>
                                    <option value="SHA">SHA — shared costs</option>
                                    <option value="OUR">OUR — sender pays all</option>
                                    <option value="BEN">BEN — beneficiary pays all</option>
                                </select>
                            </div>
                            <div className="tf-field">
                                <label>Remittance info <span className="tf-optional">(optional)</span></label>
                                <input value={remittanceInfo} onChange={e => setRemittanceInfo(e.target.value)} placeholder="Invoice number, purpose..." />
                            </div>
                        </>
                    )}

                    <div className="tf-field">
                        <label>Amount (USD)</label>
                        <input
                            type="number"
                            min="0.01"
                            step="0.01"
                            value={amount}
                            onChange={e => setAmount(e.target.value)}
                            placeholder="0.00"
                        />
                    </div>

                    <div className="tf-field">
                        <label>Description <span className="tf-optional">(optional)</span></label>
                        <input value={description} onChange={e => setDescription(e.target.value)} placeholder="What's this for?" />
                    </div>
                </div>

                {error && <p className="tf-error">{error}</p>}

                <div className="tf-actions">
                    <button className="tf-btn-cancel" onClick={onCancel} disabled={loading}>Cancel</button>
                    <button className="tf-btn-submit" onClick={handleSubmit} disabled={loading}>
                        {loading ? 'Sending...' : `Send ${info.label}`}
                    </button>
                </div>
            </div>
        </div>
    );
}