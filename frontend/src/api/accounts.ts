import client from './client';

export interface Account {
    id: string;
    accountNumber: string;
    type: string;
    balance: number;
    currency: string;
    status: string;
    createdAt: string;
}

export interface Transaction {
    id: string;
    amount: number;
    type: string;
    status: string;
    description: string | null;
    referenceId: string | null;
    createdAt: string;
}

export interface PagedResponse<T> {
    items: T[];
    page: number;
    pageSize: number;
    total: number;
    totalPages: number;
}

export async function getAccounts(): Promise<Account[]> {
    const res = await client.get<Account[]>('/accounts');
    return res.data;
}

export async function getTransactions(accountId: string, page = 1, pageSize = 10): Promise<PagedResponse<Transaction>> {
    const res = await client.get<PagedResponse<Transaction>>(`/accounts/${accountId}/transactions`, {
        params: { page, pageSize },
    });
    return res.data;
}

export async function createAccount(type: string): Promise<Account> {
    const res = await client.post<Account>('/accounts', { type, currency: 'USD' });
    return res.data;
}
