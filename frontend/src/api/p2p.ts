import client from './client';

export interface PhoneAliasResponse {
    id: string;
    accountId: string;
    phone: string;
    klikAliasId: string;
    status: string;
    registeredAt: string;
    createdAt: string;
}

export interface P2pTransferResponse {
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
}

export async function getPhoneAlias(accountId: string): Promise<PhoneAliasResponse | null> {
    try {
        const res = await client.get<PhoneAliasResponse>(`/accounts/${accountId}/phone-alias`);
        return res.data;
    } catch (e: unknown) {
        if ((e as { response?: { status?: number } })?.response?.status === 404) return null;
        throw e;
    }
}

export async function registerPhoneAlias(accountId: string, phone: string): Promise<PhoneAliasResponse> {
    const res = await client.post<PhoneAliasResponse>(`/accounts/${accountId}/phone-alias`, { phone });
    return res.data;
}

export async function deletePhoneAlias(accountId: string): Promise<void> {
    await client.delete(`/accounts/${accountId}/phone-alias`);
}

export async function sendToPhone(fromAccountId: string, phone: string, amount: number): Promise<P2pTransferResponse> {
    const res = await client.post<P2pTransferResponse>('/transfers/p2p', { fromAccountId, phone, amount });
    return res.data;
}
