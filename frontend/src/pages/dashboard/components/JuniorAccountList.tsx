import { useState } from 'react';
import type { JuniorAccount } from '../../../api/accounts';

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

export default function JuniorAccountList({ accounts }: { accounts: JuniorAccount[] }) {
    const [selected, setSelected] = useState<JuniorAccount | null>(null);
    const close = () => setSelected(null);

    return (
        <>
            <div className="db-transfer-list">
                {accounts.map(acc => (
                    <button key={acc.juniorAccountId} className="db-transfer-row" onClick={() => setSelected(acc)}>
                        <div className="db-transfer-row-left">
                            <span className="db-transfer-channel">JUNIOR</span>
                            <span className="db-stat-label">{acc.firstName} {acc.lastName}</span>
                            <span className="db-tx-date">••••{acc.accountNumber.slice(-4)} · Age {calcAge(acc.dateOfBirth)} · {acc.status}</span>
                        </div>
                        <div className="db-transfer-row-right">
                            <span className="db-junior-balance">${acc.balance.toFixed(2)} {acc.currency}</span>
                            <span className="db-tx-date">Daily {formatLimit(acc.cardDailyLimit)} · Monthly {formatLimit(acc.cardMonthlyLimit)}</span>
                        </div>
                    </button>
                ))}
            </div>

            {selected && (
                <div className="db-modal-overlay" onClick={close}>
                    <div className="db-modal" onClick={e => e.stopPropagation()}>
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
                        <div className="db-form-field">
                            <span className="db-label">Daily limit</span>
                            <span className="db-value-static">{formatLimit(selected.cardDailyLimit)}</span>
                        </div>
                        <div className="db-form-field">
                            <span className="db-label">Monthly limit</span>
                            <span className="db-value-static">{formatLimit(selected.cardMonthlyLimit)}</span>
                        </div>
                        <div className="db-form-actions">
                            <button className="db-btn-secondary" onClick={close}>Close</button>
                        </div>
                    </div>
                </div>
            )}
        </>
    );
}
