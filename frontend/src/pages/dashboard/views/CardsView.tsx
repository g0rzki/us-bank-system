import { useState } from 'react';
import type { Account } from '../../../api/accounts';
import AccountTabs from '../components/AccountTabs';
import CardsSection from '../components/CardsSection';

export default function CardsView({ accounts }: { accounts: Account[] }) {
    const [selectedAccountId, setSelectedAccountId] = useState(accounts[0]?.id ?? '');

    return (
        <div className="db-view">
            <h1 className="db-view-title">Cards</h1>

            {accounts.length === 0 ? (
                <p className="db-empty">No accounts yet. Create an account first.</p>
            ) : (
                <>
                    <AccountTabs
                        accounts={accounts}
                        selectedId={selectedAccountId}
                        onSelect={setSelectedAccountId}
                    />
                    <CardsSection key={selectedAccountId} accountId={selectedAccountId} />
                </>
            )}
        </div>
    );
}
