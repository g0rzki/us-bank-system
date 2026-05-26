import type { Account } from '../../../api/accounts';

export default function AccountTabs({ accounts, selectedId, onSelect }: {
    accounts: Account[];
    selectedId: string;
    onSelect: (id: string) => void;
}) {
    return (
        <div className="db-account-tabs">
            {accounts.map(acc => (
                <button
                    key={acc.id}
                    className={`db-account-tab${selectedId === acc.id ? ' active' : ''}`}
                    onClick={() => onSelect(acc.id)}
                >
                    {acc.type} ••••{acc.accountNumber.slice(-4)}
                </button>
            ))}
        </div>
    );
}
