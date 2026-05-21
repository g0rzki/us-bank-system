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
            throw new UnauthorizedAccessException("Access denied");

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Transactions
            .Where(t => t.AccountId == accountId)
            .OrderByDescending(t => t.CreatedAt);

        var total = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TransactionResponse
            {
                Id = t.Id,
                Amount = t.Amount,
                Type = t.Type,
                Status = t.Status,
                Description = t.Description,
                ReferenceId = t.ReferenceId,
                CreatedAt = t.CreatedAt
            })
            .ToListAsync();

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
