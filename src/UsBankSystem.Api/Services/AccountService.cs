using Microsoft.EntityFrameworkCore;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Core.Domain.Accounts;
using UsBankSystem.Core.Domain.Common;
using Account = UsBankSystem.Core.Entities.Account;
using UsBankSystem.Infrastructure.Persistence;

namespace UsBankSystem.Api.Services;

public class AccountService(AppDbContext db)
{
    public async Task<(bool Success, string? Error, AccountResponse? Result)> CreateAsync(Guid userId, CreateAccountRequest request)
    {
        if (!AccountType.IsValid(request.Type))
            return (false, $"Invalid account type. Allowed values: '{AccountType.Checking}', '{AccountType.Savings}'", null);

        if (!CurrencyCode.IsValid(request.Currency))
            return (false, $"Unsupported currency '{request.Currency}'. Allowed values: '{CurrencyCode.USD}'", null);

        var userExists = await db.Users.AnyAsync(u => u.Id == userId);
        if (!userExists)
            return (false, "User not found", null);

        var account = new Account
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AccountNumber = await GenerateAccountNumberAsync(db),
            Type = request.Type,
            Currency = request.Currency.ToUpperInvariant(),
            Balance = 0,
            ReservedBalance = 0,
            Status = AccountStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        return (true, null, new AccountResponse
        {
            Id = account.Id,
            AccountNumber = account.AccountNumber,
            Type = account.Type,
            Balance = account.Balance,
            Currency = account.Currency,
            Status = account.Status,
            CreatedAt = account.CreatedAt
        });
    }
    
    public async Task<(bool Success, string? Error, int StatusCode, AccountResponse? Result)> GetByIdAsync(Guid userId, Guid accountId)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId);

        if (account is null)
            return (false, "Account not found", 404, null);

        if (account.UserId != userId)
            return (false, "Access denied", 403, null);

        return (true, null, 200, new AccountResponse
        {
            Id = account.Id,
            AccountNumber = account.AccountNumber,
            Type = account.Type,
            Balance = account.Balance,
            Currency = account.Currency,
            Status = account.Status,
            CreatedAt = account.CreatedAt
        });
    }
    
    public async Task<(bool Success, string? Error, int StatusCode, BalanceResponse? Result)> GetBalanceAsync(Guid userId, Guid accountId)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId);

        if (account is null)
            return (false, "Account not found", 404, null);

        if (account.UserId != userId)
            return (false, "Access denied", 403, null);

        return (true, null, 200, new BalanceResponse
        {
            AccountId = account.Id,
            Balance = account.Balance,
            ReservedBalance = account.ReservedBalance,
            AvailableBalance = account.Balance - account.ReservedBalance,
            Currency = account.Currency
        });
    }
    
    public async Task<(bool Success, string? Error, int StatusCode, PagedResponse<TransactionResponse>? Result)> GetTransactionsAsync(Guid userId, Guid accountId, int page, int pageSize)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId);

        if (account is null)
            return (false, "Account not found", 404, null);

        if (account.UserId != userId)
            return (false, "Access denied", 403, null);

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

        return (true, null, 200, new PagedResponse<TransactionResponse>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            Total = total,
            TotalPages = (int)Math.Ceiling((double)total / pageSize)
        });
    }

    private static async Task<string> GenerateAccountNumberAsync(AppDbContext db)
    {
        string accountNumber;
        do
        {
            var digits = new char[16];
            for (var i = 0; i < digits.Length; i++)
                digits[i] = (char)('0' + Random.Shared.Next(0, 10));
            accountNumber = new string(digits);
        }
        while (await db.Accounts.AnyAsync(a => a.AccountNumber == accountNumber));

        return accountNumber;
    }
}
