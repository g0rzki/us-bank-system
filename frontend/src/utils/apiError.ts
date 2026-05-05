export function getApiErrorStatus(err: unknown): number | undefined {
    return (err as { response?: { status?: number } }).response?.status;
}

export function isNetworkError(err: unknown): boolean {
    const e = err as { response?: unknown; code?: string; message?: string };
    return !e.response && (e.code === 'ERR_NETWORK' || e.code === 'ECONNREFUSED' || e.message === 'Network Error');
}
