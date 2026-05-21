import { useState } from 'react';
import type { Account } from '../../../api/accounts';

export default function AccountCard({ account, detailed, clickable = true }: { account: Account; detailed?: boolean; clickable?: boolean }) {
    const [flipped, setFlipped] = useState(false);

    return (
        <div
            className={`db-account-card${clickable ? ' db-account-card-flip' : ''}`}
            onClick={clickable ? () => setFlipped(f => !f) : undefined}
        >
            <div
                className="db-account-card-inner"
                style={{ transform: flipped ? 'rotateY(180deg)' : 'rotateY(0deg)' }}
            >
                <div className="db-account-card-front">
                    <div className="db-account-card-top">
                        <span className="db-account-type">{account.type}</span>
                        <span className={`db-account-status ${account.status}`}>{account.status}</span>
                    </div>
                    <span className="db-account-balance">${account.balance.toFixed(2)}</span>
                    <span className="db-account-number">{'•'.repeat(account.accountNumber.length - 4)} {account.accountNumber.slice(-4)}</span>
                    {detailed && <span className="db-account-currency">{account.currency}</span>}
                </div>

                <div className="db-account-card-back">
                    {[
                        { label: 'Account number', value: account.accountNumber },
                        { label: 'Balance', value: `$${account.balance.toFixed(2)} ${account.currency}` },
                        { label: 'Type', value: account.type },
                        { label: 'Status', value: account.status },
                        { label: 'Opened', value: new Date(account.createdAt).toLocaleDateString('en-US', { year: 'numeric', month: 'long', day: 'numeric' }) },
                    ].map(({ label, value }) => (
                        <div key={label} className="db-account-popup-row">
                            <span className="db-account-popup-label">{label}</span>
                            <span className="db-account-popup-value">{value}</span>
                        </div>
                    ))}
                </div>
            </div>
        </div>
    );
}
