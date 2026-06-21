import client from './client';

export interface Transfer {
    id: string;
    fromAccountId: string;
    toAccountId: string | null;
    amount: number;
    currency: string;
    channel: string;
    status: string;
    description: string | null;
    createdAt: string;
    completedAt: string | null;
    requiresApproval: boolean;
    estimatedSettlement: string | null;
}

export interface TransferStatus {
    transferId: string;
    status: string;
    channel: string;
    createdAt: string;
    completedAt: string | null;
    externalReferenceId: string | null;
}

export interface PendingApprovalTransfer {
    id: string;
    fromAccountId: string;
    fromAccountNumber: string;
    toAccountId: string | null;
    amount: number;
    currency: string;
    channel: string;
    description: string | null;
    createdAt: string;
}

export async function getPendingApprovalTransfers(): Promise<PendingApprovalTransfer[]> {
    const res = await client.get<PendingApprovalTransfer[]>('/transfers/pending-approval');
    return res.data;
}

export async function approveTransfer(transferId: string): Promise<Transfer> {
    const res = await client.post<Transfer>(`/transfers/${transferId}/approve`);
    return res.data;
}

export async function rejectTransfer(transferId: string): Promise<Transfer> {
    const res = await client.post<Transfer>(`/transfers/${transferId}/reject`);
    return res.data;
}

export async function getTransfers(): Promise<Transfer[]> {
    const res = await client.get<Transfer[]>('/transfers');
    return res.data;
}

export async function getTransferStatus(transferId: string): Promise<TransferStatus> {
    const res = await client.get<TransferStatus>(`/transfers/${transferId}/status`);
    return res.data;
}

export interface CreateInternalTransferRequest {
    fromAccountId: string;
    toAccountNumber: string;
    amount: number;
    currency: string;
    description?: string;
}

export interface CreateAchTransferRequest {
    fromAccountId: string;
    toRoutingNumber: string;
    toAccountNumber: string;
    recipientName: string;
    amount: number;
    currency: string;
    description?: string;
}

export interface CreateRtpTransferRequest {
    fromAccountId: string;
    toAccountNumber: string;
    toRoutingNumber?: string;
    amount: number;
    currency: string;
    description?: string;
}

export interface CreateFedNowTransferRequest {
    fromAccountId: string;
    toAccountNumber: string;
    toRoutingNumber: string;
    amount: number;
    currency: string;
    description?: string;
}

export interface CreateSwiftTransferRequest {
    fromAccountId: string;
    iban: string;
    bic: string;
    beneficiaryName: string;
    beneficiaryAddress?: string;
    amount: number;
    currency: string;
    chargeBearer: string;
    remittanceInfo?: string;
    description?: string;
}

export async function createInternalTransfer(req: CreateInternalTransferRequest): Promise<Transfer> {
    const res = await client.post<Transfer>('/transfers/internal', req);
    return res.data;
}

export async function createAchTransfer(req: CreateAchTransferRequest): Promise<Transfer> {
    const res = await client.post<Transfer>('/transfers/ach', req);
    return res.data;
}

export async function createRtpTransfer(req: CreateRtpTransferRequest): Promise<Transfer> {
    const res = await client.post<Transfer>('/transfers/rtp', req);
    return res.data;
}

export async function createFedNowTransfer(req: CreateFedNowTransferRequest): Promise<Transfer> {
    const res = await client.post<Transfer>('/transfers/fednow', req);
    return res.data;
}

export async function createSwiftTransfer(req: CreateSwiftTransferRequest): Promise<Transfer> {
    const res = await client.post<Transfer>('/transfers/swift', req);
    return res.data;
}