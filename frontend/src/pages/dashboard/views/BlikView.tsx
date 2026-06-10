import { useState, useEffect } from 'react';
import type { Account } from '../../../api/accounts';
import {
    generateBlikCode,
    getPendingBlikAuthorizations,
    approveBlikAuthorization,
    rejectBlikAuthorization,
    getBlikTransactions,
} from '../../../api/blik';
import type { BlikAuthorization } from '../../../api/blik';
import { useToast } from '../../../context/ToastContext';
import '../../../styles/BlikView.css';

const DEFAULT_TOTAL_SECONDS = 120;
const RING_RADIUS = 42;
const RING_CIRCUMFERENCE = 2 * Math.PI * RING_RADIUS;

function formatCode(code: string): string {
    return code.length === 6 ? `${code.slice(0, 3)} ${code.slice(3)}` : code;
}

function statusLabel(status: string): string {
    switch (status) {
        case 'accepted': return 'Completed';
        case 'rejected': return 'Rejected';
        case 'timeout': return 'Expired';
        default: return status;
    }
}

function findActivePending(list: BlikAuthorization[]): BlikAuthorization | undefined {
    return list.find(a => new Date(a.expiryTime).getTime() > Date.now());
}

function getErrorDetail(e: unknown): string | undefined {
    return (e as { response?: { data?: { detail?: string } } })?.response?.data?.detail;
}

export default function BlikView({ accounts }: { accounts: Account[] }) {
    const { showToast } = useToast();
    const [accountId, setAccountId] = useState(accounts[0]?.id ?? '');

    const [code, setCode] = useState<string | null>(null);
    const [expiresAt, setExpiresAt] = useState<Date | null>(null);
    const [totalSeconds, setTotalSeconds] = useState(DEFAULT_TOTAL_SECONDS);
    const [secondsLeft, setSecondsLeft] = useState(0);
    const [expired, setExpired] = useState(false);
    const [generating, setGenerating] = useState(false);

    const [pending, setPending] = useState<BlikAuthorization | null>(null);
    const [decisionSecondsLeft, setDecisionSecondsLeft] = useState(0);
    const [deciding, setDeciding] = useState(false);

    const [history, setHistory] = useState<BlikAuthorization[]>([]);
    const [historyLoading, setHistoryLoading] = useState(true);

    useEffect(() => {
        getBlikTransactions()
            .then(setHistory)
            .catch(() => showToast('Could not load BLIK history.'))
            .finally(() => setHistoryLoading(false));
    }, [showToast]);

    // Catch an authorization that was already pending before this view was opened.
    useEffect(() => {
        getPendingBlikAuthorizations()
            .then(list => {
                const active = findActivePending(list);
                if (active) setPending(active);
            })
            .catch(() => {});
    }, []);

    // Countdown for the active KLIK code.
    useEffect(() => {
        if (!expiresAt) return;
        const tick = () => {
            const left = Math.max(0, Math.round((expiresAt.getTime() - Date.now()) / 1000));
            setSecondsLeft(left);
            if (left === 0) {
                setCode(null);
                setExpiresAt(null);
                setExpired(true);
            }
        };
        tick();
        const id = setInterval(tick, 1000);
        return () => clearInterval(id);
    }, [expiresAt]);

    // While a code is active, poll for an incoming authorization request.
    useEffect(() => {
        if (!code) return;
        const checkPending = () => {
            getPendingBlikAuthorizations()
                .then(list => {
                    const active = findActivePending(list);
                    if (active) {
                        setPending(active);
                        setCode(null);
                        setExpiresAt(null);
                        setExpired(false);
                    }
                })
                .catch(() => {});
        };
        checkPending();
        const id = setInterval(checkPending, 2000);
        return () => clearInterval(id);
    }, [code]);

    // Countdown for the decision window of a pending authorization.
    useEffect(() => {
        if (!pending) return;
        const tick = () => {
            const left = Math.max(0, Math.round((new Date(pending.expiryTime).getTime() - Date.now()) / 1000));
            setDecisionSecondsLeft(left);
        };
        tick();
        const id = setInterval(tick, 1000);
        return () => clearInterval(id);
    }, [pending]);

    const handleGenerate = async () => {
        if (!accountId) return;
        setGenerating(true);
        try {
            const res = await generateBlikCode(accountId);
            setCode(res.code);
            setExpiresAt(new Date(res.expiresAt));
            setTotalSeconds(res.expiresIn || DEFAULT_TOTAL_SECONDS);
            setExpired(false);
        } catch (e: unknown) {
            showToast(getErrorDetail(e) ?? 'Could not generate a KLIK code. Please try again.');
        } finally {
            setGenerating(false);
        }
    };

    const dismissPending = () => {
        setPending(null);
        getBlikTransactions().then(setHistory).catch(() => {});
    };

    const handleDecision = async (approve: boolean) => {
        if (!pending) return;
        setDeciding(true);
        try {
            const updated = approve
                ? await approveBlikAuthorization(pending.id)
                : await rejectBlikAuthorization(pending.id);
            setHistory(prev => [updated, ...prev]);
            setPending(null);
            showToast(
                approve ? `Payment to ${updated.merchantName} approved` : 'Payment rejected',
                approve ? 'success' : 'info'
            );
        } catch (e: unknown) {
            showToast(getErrorDetail(e) ?? 'Could not process your decision. Please try again.');
            dismissPending();
        } finally {
            setDeciding(false);
        }
    };

    const ringOffset = RING_CIRCUMFERENCE * (1 - secondsLeft / Math.max(totalSeconds, 1));

    return (
        <div className="db-view">
            <h1 className="db-view-title">BLIK</h1>

            <div className="db-section db-mb">
                <h2 className="db-section-title">Generate KLIK code</h2>
                <div className="db-surface blik-generate-card">
                    {accounts.length === 0 ? (
                        <p className="db-empty">You need an account to use BLIK.</p>
                    ) : (
                        <>
                            <div className="db-form-field blik-account-field">
                                <span className="db-label">Account</span>
                                <select className="db-input" value={accountId} onChange={e => setAccountId(e.target.value)}>
                                    {accounts.map(a => (
                                        <option key={a.id} value={a.id}>
                                            {a.accountNumber} ({a.type}) — ${a.balance.toFixed(2)}
                                        </option>
                                    ))}
                                </select>
                            </div>

                            {code ? (
                                <div className="blik-code-block">
                                    <div className="blik-ring-wrap">
                                        <svg className="blik-ring" viewBox="0 0 100 100">
                                            <circle className="blik-ring-bg" cx="50" cy="50" r={RING_RADIUS} />
                                            <circle
                                                className="blik-ring-fg"
                                                cx="50" cy="50" r={RING_RADIUS}
                                                strokeDasharray={RING_CIRCUMFERENCE}
                                                strokeDashoffset={ringOffset}
                                            />
                                        </svg>
                                        <span className="blik-ring-label">{secondsLeft}s</span>
                                    </div>
                                    <div className="blik-code">{formatCode(code)}</div>
                                    <p className="db-stat-label">Show this code to the agent terminal</p>
                                </div>
                            ) : expired ? (
                                <div className="blik-code-block">
                                    <p className="db-empty">Code expired</p>
                                    <button className="db-btn-primary" onClick={handleGenerate} disabled={generating}>
                                        {generating ? 'Generating…' : 'Generate again'}
                                    </button>
                                </div>
                            ) : (
                                <button className="db-btn-primary" onClick={handleGenerate} disabled={generating}>
                                    {generating ? 'Generating…' : 'Generate KLIK code'}
                                </button>
                            )}
                        </>
                    )}
                </div>
            </div>

            <div className="db-section">
                <h2 className="db-section-title">BLIK history</h2>
                {historyLoading ? (
                    <div className="db-loading">Loading...</div>
                ) : history.length === 0 ? (
                    <p className="db-empty">No BLIK transactions yet.</p>
                ) : (
                    <div className="db-transfer-list">
                        {history.map(item => (
                            <div className="db-transfer-row" key={item.id}>
                                <div className="db-transfer-row-left">
                                    <span className="db-transfer-channel">{item.merchantName}</span>
                                    <span className="db-tx-date">{new Date(item.decidedAt ?? item.expiryTime).toLocaleString()}</span>
                                </div>
                                <div className="db-transfer-row-right">
                                    <span className="db-tx-amount debit">{item.amount.toFixed(2)} {item.currency}</span>
                                    <span className={`db-tx-status ${item.status}`}>{statusLabel(item.status)}</span>
                                </div>
                            </div>
                        ))}
                    </div>
                )}
            </div>

            {pending && (
                <div className="db-modal-overlay">
                    <div className="db-modal">
                        <h2 className="db-section-title">Approve KLIK payment</h2>
                        <div className="db-form-field">
                            <span className="db-label">Merchant</span>
                            <span className="db-value-static">{pending.merchantName}</span>
                        </div>
                        <div className="db-form-field">
                            <span className="db-label">Amount</span>
                            <span className="db-value-static">{pending.amount.toFixed(2)} {pending.currency}</span>
                        </div>
                        <div className="db-form-field">
                            <span className="db-label">Time left to respond</span>
                            <span className="db-value-static">{decisionSecondsLeft}s</span>
                        </div>
                        {decisionSecondsLeft === 0 ? (
                            <>
                                <p className="db-error">This authorization has expired.</p>
                                <div className="db-form-actions">
                                    <button className="db-btn-secondary" onClick={dismissPending}>Close</button>
                                </div>
                            </>
                        ) : (
                            <div className="db-form-actions">
                                <button className="db-btn-primary" onClick={() => handleDecision(true)} disabled={deciding}>
                                    {deciding ? '…' : 'Approve'}
                                </button>
                                <button className="db-btn-secondary" onClick={() => handleDecision(false)} disabled={deciding}>
                                    Reject
                                </button>
                            </div>
                        )}
                    </div>
                </div>
            )}
        </div>
    );
}
