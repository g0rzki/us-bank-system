import type { Account, JuniorAccount } from '../../../api/accounts';

export type TabEntry =
    | { kind: 'own'; account: Account }
    | { kind: 'junior'; junior: JuniorAccount };

function tabId(t: TabEntry) {
    return t.kind === 'own' ? t.account.id : t.junior.accountId;
}

function tabLabel(t: TabEntry) {
    return t.kind === 'own'
        ? `${t.account.type} ••••${t.account.accountNumber.slice(-4)}`
        : `${t.junior.firstName} ••••${t.junior.accountNumber.slice(-4)}`;
}

export function buildTabs(accounts: Account[], juniors: JuniorAccount[]): TabEntry[] {
    return [
        ...accounts.map(a => ({ kind: 'own' as const, account: a })),
        ...juniors.map(j => ({ kind: 'junior' as const, junior: j })),
    ];
}

export default function AccountTabs({ tabs, selectedId, onSelect }: {
    tabs: TabEntry[];
    selectedId: string;
    onSelect: (id: string) => void;
}) {
    return (
        <div className="db-account-tabs">
            {tabs.map(tab => (
                <button
                    key={tabId(tab)}
                    className={`db-account-tab${selectedId === tabId(tab) ? ' active' : ''}${tab.kind === 'junior' ? ' junior' : ''}`}
                    onClick={() => onSelect(tabId(tab))}
                >
                    {tabLabel(tab)}
                </button>
            ))}
        </div>
    );
}
