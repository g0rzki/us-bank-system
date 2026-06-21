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
const ABA_WEIGHTS = [3, 7, 1, 3, 7, 1, 3, 7, 1];
const BIC_RE = /^[A-Z]{4}[A-Z]{2}[A-Z0-9]{2}([A-Z0-9]{3})?$/;
const IBAN_RE = /^[A-Z]{2}\d{2}[A-Z0-9]{11,30}$/;

// ── Validation helpers ────────────────────────────────────────────────────────

function warnRouting(v: string): string | null {
    if (!v) return null;
    if (!/^\d+$/.test(v)) return 'Routing number must contain only digits';
    if (v.length !== 9) return `Must be exactly 9 digits (currently ${v.length})`;
    const sum = v.split('').reduce((acc, d, i) => acc + parseInt(d) * ABA_WEIGHTS[i], 0);
    if (sum % 10 !== 0) return 'Invalid ABA checksum — double-check the routing number';
    return null;
}

function warnAccountNumber(v: string): string | null {
    if (!v) return null;
    if (!/^\d+$/.test(v)) return 'Account number should contain only digits';
    if (v.length < 4) return `Too short (${v.length} digits) — typically 4–17 digits`;
    if (v.length > 17) return `Too long (${v.length} digits) — max 17`;
    return null;
}

function warnIban(v: string): string | null {
    if (!v) return null;
    const s = v.replace(/\s/g, '').toUpperCase();
    if (!/^[A-Z]{2}/.test(s)) return 'IBAN must start with 2-letter country code (e.g. DE, PL, GB)';
    if (s.length < 15) return `Too short (${s.length} chars) — minimum 15`;
    if (s.length > 34) return `Too long (${s.length} chars) — maximum 34`;
    if (!IBAN_RE.test(s)) return 'Invalid format — expected: CC99BBAN (e.g. DE89370400440532013000)';
    return null;
}

function warnBic(v: string): string | null {
    if (!v) return null;
    const s = v.toUpperCase();
    if (s.length !== 8 && s.length !== 11) return `BIC must be 8 or 11 characters (currently ${s.length})`;
    if (!BIC_RE.test(s)) return 'Invalid BIC format — expected: AAAABBCC or AAAABBCCDDD';
    return null;
}

function warnPhone(v: string): string | null {
    if (!v) return null;
    if (!v.startsWith('+1')) return 'US number must start with +1';
    if (!/^\+1\d*$/.test(v)) return 'Must contain only digits after +1';
    if (v.length !== 12) return `Must be +1 followed by 10 digits (currently ${v.length - 2} digits after +1)`;
    return null;
}

function warnAmount(v: string): string | null {
    if (!v) return null;
    const n = parseFloat(v);
    if (isNaN(n)) return 'Not a valid number';
    if (n <= 0) return 'Amount must be greater than 0';
    if (n < 0.01) return 'Minimum amount is $0.01';
    return null;
}

// ── Warn component ────────────────────────────────────────────────────────────

function Warn({ msg }: { msg: string | null }) {
    if (!msg) return null;
    return <span className="tf-hint-warn">⚠ {msg}</span>;
}

// ── Presets ───────────────────────────────────────────────────────────────────

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

// ── Component ─────────────────────────────────────────────────────────────────

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
    const [touched, setTouched] = useState<Set<string>>(new Set());

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
        setTouched(new Set());
    }, [channel]);

    const touch = (field: string) =>
        setTouched(prev => new Set(prev).add(field));

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

    // Computed warnings (only shown for touched fields)
    const w = {
        toRoutingNumber: touched.has('toRoutingNumber') ? warnRouting(toRoutingNumber) : null,
        toAccountNumber: touched.has('toAccountNumber') ? warnAccountNumber(toAccountNumber) : null,
        iban:            touched.has('iban')            ? warnIban(iban)               : null,
        bic:             touched.has('bic')             ? warnBic(bic)                 : null,
        phone:           touched.has('phone')           ? warnPhone(phone)             : null,
        amount:          touched.has('amount')          ? warnAmount(amount)           : null,
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
            setTouched(new Set());
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
                        <input
                            className={w.toAccountNumber ? 'warn' : ''}
                            value={toAccountNumber}
                            onChange={e => setToAccountNumber(e.target.value)}
                            onBlur={() => touch('toAccountNumber')}
                            placeholder="Account number"
                        />
                        <Warn msg={w.toAccountNumber} />
                    </div>
                )}

                {channel === 'rtp' && (
                    <div className="tf-field">
                        <label>Routing number <span className="tf-optional">(optional — leave empty for on-us)</span></label>
                        <input
                            className={w.toRoutingNumber ? 'warn' : ''}
                            value={toRoutingNumber}
                            onChange={e => setToRoutingNumber(e.target.value)}
                            onBlur={() => touch('toRoutingNumber')}
                            placeholder="9 digits — TCH external only"
                        />
                        <Warn msg={w.toRoutingNumber} />
                    </div>
                )}

                {channel === 'fednow' && (
                    <>
                        <div className="tf-field">
                            <label>Recipient account number</label>
                            <input
                                className={w.toAccountNumber ? 'warn' : ''}
                                value={toAccountNumber}
                                onChange={e => setToAccountNumber(e.target.value)}
                                onBlur={() => touch('toAccountNumber')}
                                placeholder="Account number"
                            />
                            <Warn msg={w.toAccountNumber} />
                        </div>
                        <div className="tf-field">
                            <label>Routing number</label>
                            <input
                                className={w.toRoutingNumber ? 'warn' : ''}
                                value={toRoutingNumber}
                                onChange={e => setToRoutingNumber(e.target.value)}
                                onBlur={() => touch('toRoutingNumber')}
                                placeholder="9 digits"
                            />
                            <Warn msg={w.toRoutingNumber} />
                        </div>
                    </>
                )}

                {channel === 'ach' && (
                    <>
                        <div className="tf-field">
                            <label>Routing number</label>
                            <input
                                className={w.toRoutingNumber ? 'warn' : ''}
                                value={toRoutingNumber}
                                onChange={e => setToRoutingNumber(e.target.value)}
                                onBlur={() => touch('toRoutingNumber')}
                                placeholder="9 digits"
                            />
                            <Warn msg={w.toRoutingNumber} />
                        </div>
                        <div className="tf-field">
                            <label>Recipient account number</label>
                            <input
                                className={w.toAccountNumber ? 'warn' : ''}
                                value={toAccountNumber}
                                onChange={e => setToAccountNumber(e.target.value)}
                                onBlur={() => touch('toAccountNumber')}
                                placeholder="Account number"
                            />
                            <Warn msg={w.toAccountNumber} />
                        </div>
                        <div className="tf-field">
                            <label>Recipient name</label>
                            <input
                                value={recipientName}
                                onChange={e => setRecipientName(e.target.value)}
                                placeholder="Max 22 characters"
                                maxLength={22}
                            />
                            {recipientName.length > 18 && (
                                <span className="tf-hint-warn">
                                    ⚠ {recipientName.length}/22 characters — backend truncates at 22
                                </span>
                            )}
                        </div>
                    </>
                )}

                {channel === 'p2p' && (
                    <div className="tf-field">
                        <label>Recipient phone number</label>
                        <input
                            className={w.phone ? 'warn' : ''}
                            value={phone}
                            onChange={e => setPhone(e.target.value)}
                            onBlur={() => touch('phone')}
                            placeholder="+15551234567"
                        />
                        <Warn msg={w.phone} />
                    </div>
                )}

                {channel === 'swift' && (
                    <>
                        <div className="tf-field">
                            <label>IBAN</label>
                            <input
                                className={w.iban ? 'warn' : ''}
                                value={iban}
                                onChange={e => setIban(e.target.value)}
                                onBlur={() => touch('iban')}
                                placeholder="e.g. DE89370400440532013000"
                            />
                            <Warn msg={w.iban} />
                        </div>
                        <div className="tf-field">
                            <label>BIC / SWIFT code</label>
                            <input
                                className={w.bic ? 'warn' : ''}
                                value={bic}
                                onChange={e => setBic(e.target.value)}
                                onBlur={() => touch('bic')}
                                placeholder="e.g. DEUTDEDB"
                            />
                            <Warn msg={w.bic} />
                        </div>
                        <div className="tf-field">
                            <label>Beneficiary name</label>
                            <input
                                value={beneficiaryName}
                                onChange={e => setBeneficiaryName(e.target.value)}
                                placeholder="Full name"
                            />
                        </div>
                        <div className="tf-field">
                            <label>Beneficiary address <span className="tf-optional">(optional)</span></label>
                            <input
                                value={beneficiaryAddress}
                                onChange={e => setBeneficiaryAddress(e.target.value)}
                                placeholder="Street, city, country"
                            />
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
                            <input
                                value={remittanceInfo}
                                onChange={e => setRemittanceInfo(e.target.value)}
                                placeholder="Invoice number, purpose..."
                            />
                        </div>
                    </>
                )}

                <div className="tf-field">
                    <label>Amount (USD)</label>
                    <input
                        type="number"
                        min="0.01"
                        step="0.01"
                        className={w.amount ? 'warn' : ''}
                        value={amount}
                        onChange={e => setAmount(e.target.value)}
                        onBlur={() => touch('amount')}
                        placeholder="0.00"
                    />
                    <Warn msg={w.amount} />
                </div>
                <div className="tf-field">
                    <label>Description <span className="tf-optional">(optional)</span></label>
                    <input
                        value={description}
                        onChange={e => setDescription(e.target.value)}
                        placeholder="What's this for?"
                    />
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
