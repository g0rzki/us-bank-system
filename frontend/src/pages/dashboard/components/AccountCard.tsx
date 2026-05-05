import type { Account } from '../../../api/accounts';

export default function AccountCard({ account, detailed }: { account: Account; detailed?: boolean }) {
    return (
        <div className="db-account-card">
            <div className="db-account-card-top">
                <span className="db-account-type">{account.type}</span>
                <span className={`db-account-status ${account.status}`}>{account.status}</span>
            </div>
            <span className="db-account-balance">${account.balance.toFixed(2)}</span>
            <span className="db-account-number">{'•'.repeat(account.accountNumber.length - 4)} {account.accountNumber.slice(-4)}</span>
            {detailed && (
                <span className="db-account-currency">{account.currency}</span>
            )}
        </div>
    );
}
