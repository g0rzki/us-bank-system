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
    return (
        <ul className="db-tx-list">
            {accounts.map(acc => (
                <li key={acc.juniorAccountId} className="db-junior-item">
                    <div className="db-junior-left">
                        <span className="db-tx-desc">
                            ••••{acc.accountNumber.slice(-4)}
                        </span>
                        <span className="db-tx-date">
                            Age {calcAge(acc.dateOfBirth)} · {acc.status}
                        </span>
                    </div>
                    <div className="db-junior-right">
                        <span className="db-junior-balance">
                            ${acc.balance.toFixed(2)} {acc.currency}
                        </span>
                        <span className="db-tx-date">
                            Daily {formatLimit(acc.cardDailyLimit)} · Monthly {formatLimit(acc.cardMonthlyLimit)}
                        </span>
                    </div>
                </li>
            ))}
        </ul>
    );
}
