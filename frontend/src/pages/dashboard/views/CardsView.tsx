import { useState, useEffect } from 'react';
import { getJuniorAccounts } from '../../../api/accounts';
import type { Account } from '../../../api/accounts';
import AccountTabs, { buildTabs } from '../components/AccountTabs';
import type { TabEntry } from '../components/AccountTabs';
import CardsSection from '../components/CardsSection';

export default function CardsView({ accounts }: { accounts: Account[] }) {
    const [tabs, setTabs] = useState<TabEntry[]>([]);
    const [selectedId, setSelectedId] = useState(accounts[0]?.id ?? '');

    useEffect(() => {
        if (accounts.length === 0) return;
        Promise.all(accounts.map(a => getJuniorAccounts(a.id)))
            .then(results => {
                const seen = new Set<string>();
                const unique = results.flat().filter(j => seen.has(j.juniorAccountId) ? false : seen.add(j.juniorAccountId) && true);
                setTabs(buildTabs(accounts, unique));
            });
    }, [accounts]);

    return (
        <div className="db-view">
            <h1 className="db-view-title">Cards</h1>

            {accounts.length === 0 ? (
                <p className="db-empty">No accounts yet. Create an account first.</p>
            ) : (
                <>
                    <AccountTabs tabs={tabs} selectedId={selectedId} onSelect={setSelectedId} />
                    <CardsSection
                        key={selectedId}
                        accountId={selectedId}
                        isJunior={tabs.find(t => (t.kind === 'own' ? t.account.id : t.junior.accountId) === selectedId)?.kind === 'junior'}
                    />
                </>
            )}
        </div>
    );
}
