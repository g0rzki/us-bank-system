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
    channel: string | null;
    counterpartyAccountNumber: string | null;
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

export interface JuniorAccount {
    juniorAccountId: string;
    accountId: string;
    accountNumber: string;
    firstName: string;
    lastName: string;
    balance: number;
    currency: string;
    status: string;
    dateOfBirth: string;
    cardDailyLimit: number | null;
    cardMonthlyLimit: number | null;
    createdAt: string;
}

export async function getJuniorAccounts(parentAccountId: string): Promise<JuniorAccount[]> {
    const res = await client.get<JuniorAccount[]>(`/accounts/${parentAccountId}/junior-accounts`);
    return res.data;
}

export async function createJuniorAccount(parentAccountId: string, email: string, password: string, firstName: string, lastName: string, dateOfBirth: string): Promise<JuniorAccount> {
    const res = await client.post<JuniorAccount>('/accounts/junior', { parentAccountId, email, password, firstName, lastName, dateOfBirth });
    return res.data;
}

export async function addJuniorCard(juniorAccountId: string, dailyLimit?: number, monthlyLimit?: number): Promise<Card> {
    const res = await client.post<Card>(`/accounts/junior/${juniorAccountId}/card`, { dailyLimit, monthlyLimit });
    return res.data;
}

export async function updateJuniorLimits(juniorAccountId: string, dailyLimit?: number, monthlyLimit?: number): Promise<Card> {
    const res = await client.patch<Card>(`/accounts/${juniorAccountId}/junior-limit`, { dailyLimit, monthlyLimit });
    return res.data;
}

export interface Card {
    id: string;
    accountId: string;
    last4: string;
    type: string;
    status: string;
    dailyLimit: number | null;
    monthlyLimit: number | null;
    expiresAt: string;
    blockedAt: string | null;
    createdAt: string;
}

export interface RegisterCardRequest {
    type: string;
    dailyLimit?: number;
    monthlyLimit?: number;
}

export async function getCards(accountId: string): Promise<Card[]> {
    const res = await client.get<Card[]>(`/accounts/${accountId}/cards`);
    return res.data;
}

export async function registerCard(accountId: string, data: RegisterCardRequest): Promise<Card> {
    const res = await client.post<Card>(`/accounts/${accountId}/cards`, data);
    return res.data;
}

export async function getCard(accountId: string, cardId: string): Promise<Card> {
    const res = await client.get<Card>(`/accounts/${accountId}/cards/${cardId}`);
    return res.data;
}

export async function updateCardStatus(accountId: string, cardId: string, status: string): Promise<Card> {
    const res = await client.patch<Card>(`/accounts/${accountId}/cards/${cardId}/status`, { status });
    return res.data;
}

export async function updateCardLimits(accountId: string, cardId: string, dailyLimit?: number, monthlyLimit?: number): Promise<Card> {
    const res = await client.patch<Card>(`/accounts/${accountId}/cards/${cardId}/limits`, { dailyLimit, monthlyLimit });
    return res.data;
}
