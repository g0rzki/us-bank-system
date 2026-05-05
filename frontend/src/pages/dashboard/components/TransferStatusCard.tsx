import type { TransferStatus } from '../../../api/transfers';

export default function TransferStatusCard({ status }: { status: TransferStatus }) {
    return (
        <div className="db-transfer-status-card">
            <div className="db-transfer-status-row">
                <span className="db-stat-label">Transfer ID</span>
                <span className="db-transfer-id">{status.transferId}</span>
            </div>
            <div className="db-transfer-status-row">
                <span className="db-stat-label">Channel</span>
                <span>{status.channel.toUpperCase()}</span>
            </div>
            <div className="db-transfer-status-row">
                <span className="db-stat-label">Status</span>
                <span className={`db-tx-status ${status.status.toLowerCase()}`}>{status.status}</span>
            </div>
            <div className="db-transfer-status-row">
                <span className="db-stat-label">Created</span>
                <span>{new Date(status.createdAt).toLocaleString()}</span>
            </div>
            {status.completedAt && (
                <div className="db-transfer-status-row">
                    <span className="db-stat-label">Completed</span>
                    <span>{new Date(status.completedAt).toLocaleString()}</span>
                </div>
            )}
            {status.externalReferenceId && (
                <div className="db-transfer-status-row">
                    <span className="db-stat-label">Reference</span>
                    <span className="db-transfer-id">{status.externalReferenceId}</span>
                </div>
            )}
        </div>
    );
}
