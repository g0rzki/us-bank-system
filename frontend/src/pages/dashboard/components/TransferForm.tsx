import { useState, useEffect } from 'react';
import type { Account } from '../../../api/accounts';
import {
    createInternalTransfer,
    createAchTransfer,
    createRtpTransfer,
    createFedNowTransfer,
    createSwiftTransfer,
} from '../../../api/transfers';
import { sendToPhone } from '../../../api/p2p';
import { useToast } from '../../../context/ToastContext';
import '../../../styles/TransferForm.css';

type Channel = 'internal' | 'ach' | 'rtp' | 'fednow' | 'swift' | 'p2p';

const US_PHONE_RE = /^\+1\d{10}$/;

interface Preset {
    label: string;
    toAccountNumber?: string;
    toRoutingNumber?: string;
    recipientName?: string;
    iban?: string;
    bic?: string;
    beneficiaryName?: string;
    phone?: string;
}

const PRESETS: Record<Channel, Preset[]> = {
    internal: [
        { label: 'Jane — checking 2000000001', toAccountNumber: '2000000001' },
        { label: 'Bob — checking 3000000001', toAccountNumber: '3000000001' },
    ],
    ach: [
        { label: 'External (RTN 021000021 / acc 987654321)', toRoutingNumber: '021000021', toAccountNumber: '987654321', recipientName: 'Jane Doe' },
        { label: 'External savings (RTN 021000021 / acc 111222333)', toRoutingNumber: '021000021', toAccountNumber: '111222333', recipientName: 'Test User' },
    ],
    rtp: [
        { label: 'On-us — Jane (2000000001)', toAccountNumber: '2000000001', toRoutingNumber: '' },
        { label: 'On-us — Bob (3000000001)', toAccountNumber: '3000000001', toRoutingNumber: '' },
        { label: 'TCH external (RTN 021000021 / acc 987654321)', toAccountNumber: '987654321', toRoutingNumber: '021000021' },
    ],
    fednow: [
        { label: 'External (RTN 010101012 / acc 3000000001)', toAccountNumber: '3000000001', toRoutingNumber: '010101012' },
        { label: 'External (RTN 021000021 / acc 987654321)', toAccountNumber: '987654321', toRoutingNumber: '021000021' },
    ],
    swift: [
        { label: 'Poland — Jan Kowalski', iban: 'PL61109010140000071219812874', bic: 'PLBKPL01XXX', beneficiaryName: 'Jan Kowalski' },
        { label: 'Germany — Hans Mueller', iban: 'DE89370400440532013000', bic: 'DEBKDE01XXX', beneficiaryName: 'Hans Mueller' },
        { label: 'UK — John Smith', iban: 'GB29NWBK60161331926819', bic: 'UKBKGB01XXX', beneficiaryName: 'John Smith' },
    ],
    p2p: [
        { label: '+15551234567 (test)', phone: '+15551234567' },
    ],
};

interface Props {
    accounts: Account[];
    channel: Channel;
    onSuccess: () => void;
}

export default function TransferForm({ accounts, channel, onSuccess }: Props) {
    const { showToast } = useToast();
    const [fromAccountId, setFromAccountId] = useState(accounts[0]?.id ?? '');
    const [toAccountNumber, setToAccountNumber] = useState('');
    const [toRoutingNumber, setToRoutingNumber] = useState('');
    const [recipientName, setRecipientName] = useState('');
    const [iban, setIban] = useState('');
    const [bic, setBic] = useState('');
    const [beneficiaryName, setBeneficiaryName] = useState('');
    const [beneficiaryAddress, setBeneficiaryAddress] = useState('');
    const [chargeBearer, setChargeBearer] = useState('SHA');
    const [remittanceInfo, setRemittanceInfo] = useState('');
    const [amount, setAmount] = useState('');
    const [description, setDescription] = useState('');
    const [phone, setPhone] = useState('');
    const [loading, setLoading] = useState(false);

    useEffect(() => {
        setToAccountNumber('');
        setToRoutingNumber('');
        setRecipientName('');
        setIban('');
        setBic('');
        setBeneficiaryName('');
        setBeneficiaryAddress('');
        setRemittanceInfo('');
        setAmount('');
        setDescription('');
        setPhone('');
    }, [channel]);

    const applyPreset = (idx: string) => {
        if (!idx) return;
        const preset = PRESETS[channel][parseInt(idx)];
        if (!preset) return;
        if (preset.toAccountNumber !== undefined) setToAccountNumber(preset.toAccountNumber);
        if (preset.toRoutingNumber !== undefined) setToRoutingNumber(preset.toRoutingNumber);
        if (preset.recipientName !== undefined) setRecipientName(preset.recipientName);
        if (preset.iban !== undefined) setIban(preset.iban);
        if (preset.bic !== undefined) setBic(preset.bic);
        if (preset.beneficiaryName !== undefined) setBeneficiaryName(preset.beneficiaryName);
        if (preset.phone !== undefined) setPhone(preset.phone);
    };

    const handleSubmit = async () => {
        const amt = parseFloat(amount);
        if (!amount || isNaN(amt) || amt <= 0) { showToast('Enter a valid amount'); return; }

        setLoading(true);
        try {
            if (channel === 'internal') {
                if (!toAccountNumber) { showToast('Enter recipient account number'); return; }
                await createInternalTransfer({ fromAccountId, toAccountNumber, amount: amt, currency: 'USD', description: description || undefined });
            } else if (channel === 'ach') {
                if (!toRoutingNumber || !toAccountNumber || !recipientName) { showToast('Routing number, account number and recipient name are required'); return; }
                await createAchTransfer({ fromAccountId, toRoutingNumber, toAccountNumber, recipientName, amount: amt, currency: 'USD', description: description || undefined });
            } else if (channel === 'rtp') {
                if (!toAccountNumber) { showToast('Enter recipient account number'); return; }
                await createRtpTransfer({ fromAccountId, toAccountNumber, toRoutingNumber: toRoutingNumber || undefined, amount: amt, currency: 'USD', description: description || undefined });
            } else if (channel === 'fednow') {
                if (!toAccountNumber || !toRoutingNumber) { showToast('Account number and routing number are required'); return; }
                await createFedNowTransfer({ fromAccountId, toAccountNumber, toRoutingNumber, amount: amt, currency: 'USD', description: description || undefined });
            } else if (channel === 'swift') {
                if (!iban || !bic || !beneficiaryName) { showToast('IBAN, BIC and beneficiary name are required'); return; }
                await createSwiftTransfer({ fromAccountId, iban, bic, beneficiaryName, beneficiaryAddress: beneficiaryAddress || undefined, amount: amt, currency: 'USD', chargeBearer, remittanceInfo: remittanceInfo || undefined, description: description || undefined });
            } else if (channel === 'p2p') {
                if (!phone || !US_PHONE_RE.test(phone)) { showToast('Enter a valid US phone number in format +1XXXXXXXXXX'); return; }
                await sendToPhone(fromAccountId, phone, amt);
            }
            setAmount('');
            setDescription('');
            setToAccountNumber('');
            setToRoutingNumber('');
            setRecipientName('');
            setPhone('');
            onSuccess();
            showToast('Transfer submitted successfully', 'success');
        } catch (e: any) {
            if (channel === 'p2p' && e?.response?.status === 404) {
                showToast('Ten numer nie ma włączonego przelewu na telefon. Spróbuj przelewu na numer konta.');
            } else {
                showToast(e?.response?.data?.detail ?? e?.response?.data?.message ?? 'Transfer failed. Please try again.');
            }
        } finally {
            setLoading(false);
        }
    };

    return (
        <div className="tf-fields">
            <div className="tf-row">
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

                {PRESETS[channel].length > 0 && (
                    <div className="tf-field">
                        <label>Quick fill <span className="tf-optional">(example data)</span></label>
                        <select onChange={e => { applyPreset(e.target.value); e.currentTarget.value = ''; }}>
                            <option value="">— select preset —</option>
                            {PRESETS[channel].map((p, i) => (
                                <option key={i} value={i}>{p.label}</option>
                            ))}
                        </select>
                    </div>
                )}

                {(channel === 'internal' || channel === 'rtp') && (
                    <div className="tf-field">
                        <label>Recipient account number</label>
                        <input value={toAccountNumber} onChange={e => setToAccountNumber(e.target.value)} placeholder="Account number" />
                    </div>
                )}

                {channel === 'rtp' && (
                    <div className="tf-field">
                        <label>Routing number <span className="tf-optional">(optional — leave empty for on-us)</span></label>
                        <input value={toRoutingNumber} onChange={e => setToRoutingNumber(e.target.value)} placeholder="9 digits — TCH external only" />
                    </div>
                )}

                {channel === 'fednow' && (
                    <>
                        <div className="tf-field">
                            <label>Recipient account number</label>
                            <input value={toAccountNumber} onChange={e => setToAccountNumber(e.target.value)} placeholder="Account number" />
                        </div>
                        <div className="tf-field">
                            <label>Routing number</label>
                            <input value={toRoutingNumber} onChange={e => setToRoutingNumber(e.target.value)} placeholder="9 digits" />
                        </div>
                    </>
                )}

                {channel === 'ach' && (
                    <>
                        <div className="tf-field">
                            <label>Routing number</label>
                            <input value={toRoutingNumber} onChange={e => setToRoutingNumber(e.target.value)} placeholder="9 digits" />
                        </div>
                        <div className="tf-field">
                            <label>Recipient account number</label>
                            <input value={toAccountNumber} onChange={e => setToAccountNumber(e.target.value)} placeholder="Account number" />
                        </div>
                        <div className="tf-field">
                            <label>Recipient name</label>
                            <input value={recipientName} onChange={e => setRecipientName(e.target.value)} placeholder="Max 22 characters" maxLength={22} />
                        </div>
                    </>
                )}

                {channel === 'p2p' && (
                    <div className="tf-field">
                        <label>Recipient phone number</label>
                        <input
                            value={phone}
                            onChange={e => setPhone(e.target.value)}
                            placeholder="+15551234567"
                        />
                    </div>
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

            <div className="tf-submit-row">
                <button className="tf-btn-submit" onClick={handleSubmit} disabled={loading}>
                    {loading ? 'Sending...' : 'Send transfer'}
                </button>
            </div>
        </div>
    );
}
