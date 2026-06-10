import client from './client';

export interface BlikCodeResponse {
    code: string;
    expiresAt: string;
    expiresIn: number;
}

export type BlikAuthorizationStatus = 'pending' | 'accepted' | 'rejected' | 'timeout';

export interface BlikAuthorization {
    id: string;
    klikTransactionId: string;
    amount: number;
    currency: string;
    merchantName: string;
    isOnUs: boolean;
    expiryTime: string;
    status: BlikAuthorizationStatus;
    createdAt: string;
    decidedAt: string | null;
    localTransactionId: string | null;
}

export async function generateBlikCode(accountId: string): Promise<BlikCodeResponse> {
    const res = await client.post<BlikCodeResponse>('/blik/generate', { accountId });
    return res.data;
}

export async function getPendingBlikAuthorizations(): Promise<BlikAuthorization[]> {
    const res = await client.get<BlikAuthorization[]>('/blik/pending');
    return res.data;
}

export async function approveBlikAuthorization(authorizationId: string): Promise<BlikAuthorization> {
    const res = await client.post<BlikAuthorization>(`/blik/${authorizationId}/approve`);
    return res.data;
}

export async function rejectBlikAuthorization(authorizationId: string): Promise<BlikAuthorization> {
    const res = await client.post<BlikAuthorization>(`/blik/${authorizationId}/reject`);
    return res.data;
}

export async function getBlikTransactions(): Promise<BlikAuthorization[]> {
    const res = await client.get<BlikAuthorization[]>('/blik/transactions');
    return res.data;
}
