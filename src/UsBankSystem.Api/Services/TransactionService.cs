using Microsoft.EntityFrameworkCore;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Infrastructure.Persistence;

namespace UsBankSystem.Api.Services;

public class TransactionService(AppDbContext db)
{
    public async Task<PagedResponse<TransactionResponse>> GetTransactionsAsync(Guid userId, Guid accountId, int page, int pageSize)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId)
            ?? throw new KeyNotFoundException("Account not found");

        if (account.UserId != userId)
        {
            var isJunior = await db.JuniorAccounts.AnyAsync(j => j.AccountId == accountId && j.ParentUserId == userId);
            if (!isJunior)
                throw new UnauthorizedAccessException("Access denied");
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Transactions
            .Where(t => t.AccountId == accountId)
            .OrderByDescending(t => t.CreatedAt);

        var total = await query.CountAsync();
        var rawItems = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new { t.Id, t.Amount, t.Type, t.Status, t.Description, t.ReferenceId, t.CreatedAt })
            .ToListAsync();

        var transferIds = rawItems
            .Where(t => t.ReferenceId != null && Guid.TryParse(t.ReferenceId, out _))
            .Select(t => Guid.Parse(t.ReferenceId!))
            .ToList();

        var transferMap = await db.Transfers
            .Where(tr => transferIds.Contains(tr.Id))
            .Select(tr => new { tr.Id, tr.FromAccountId, tr.ToAccountNumber, tr.Channel })
            .ToDictionaryAsync(tr => tr.Id);

        var fromAccountNumbers = await db.Accounts
            .Where(a => transferMap.Values.Select(tr => (Guid?)tr.FromAccountId).Contains(a.Id))
            .Select(a => new { a.Id, a.AccountNumber })
            .ToDictionaryAsync(a => a.Id, a => a.AccountNumber);

        var items = rawItems.Select(t =>
        {
            string? counterparty = null;
            if (t.ReferenceId != null && Guid.TryParse(t.ReferenceId, out var transferId)
                && transferMap.TryGetValue(transferId, out var transfer))
            {
                counterparty = t.Type == "debit"
                    ? transfer.ToAccountNumber
                    : transfer.FromAccountId.HasValue ? fromAccountNumbers.GetValueOrDefault(transfer.FromAccountId.Value) : null;
            }
            string? channel = null;
            if (t.ReferenceId != null && Guid.TryParse(t.ReferenceId, out var tid2)
                && transferMap.TryGetValue(tid2, out var tr2))
                channel = tr2.Channel;

            return new TransactionResponse
            {
                Id = t.Id,
                Amount = t.Amount,
                Type = t.Type,
                Status = t.Status,
                Description = t.Description,
                ReferenceId = t.ReferenceId,
                Channel = channel,
                CreatedAt = t.CreatedAt,
                CounterpartyAccountNumber = counterparty
            };
        }).ToList();

        return new PagedResponse<TransactionResponse>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            Total = total,
            TotalPages = (int)Math.Ceiling((double)total / pageSize)
        };
    }
}
