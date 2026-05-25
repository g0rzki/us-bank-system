import type { Transaction } from '../../../api/accounts';

const CHANNEL_LABELS: Record<string, string> = {
    internal: 'Internal',
    ach: 'ACH',
    rtp: 'RTP',
    fednow: 'FedNow',
    swift: 'SWIFT',
};

export default function TransactionDetails({ tx }: { tx: Transaction }) {
    const isDebit = tx.type === 'debit';
    return (
        <div className="db-tx-details">
            <div className="db-tx-detail-row">
                <span className="db-tx-detail-label">Date & time</span>
                <span>{new Date(tx.createdAt).toLocaleString('en-US', { dateStyle: 'medium', timeStyle: 'short' })}</span>
            </div>
            {tx.channel && (
                <div className="db-tx-detail-row">
                    <span className="db-tx-detail-label">Transfer type</span>
                    <span>{CHANNEL_LABELS[tx.channel] ?? tx.channel.toUpperCase()}</span>
                </div>
            )}
            {tx.counterpartyAccountNumber && (
                <div className="db-tx-detail-row">
                    <span className="db-tx-detail-label">{isDebit ? 'To account' : 'From account'}</span>
                    <span style={{ fontFamily: 'monospace' }}>{tx.counterpartyAccountNumber}</span>
                </div>
            )}
        </div>
    );
}
