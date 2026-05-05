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
    public async Task<AccountResponse> CreateAsync(Guid userId, CreateAccountRequest request)
    {
        if (!AccountType.IsValid(request.Type))
            throw new ArgumentException($"Invalid account type. Allowed values: '{AccountType.Checking}', '{AccountType.Savings}'");

        if (!CurrencyCode.IsValid(request.Currency))
            throw new ArgumentException($"Unsupported currency '{request.Currency}'. Allowed values: '{CurrencyCode.USD}'");

        var userExists = await db.Users.AnyAsync(u => u.Id == userId);
        if (!userExists)
            throw new KeyNotFoundException("User not found");

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

        return new AccountResponse
        {
            Id = account.Id,
            AccountNumber = account.AccountNumber,
            Type = account.Type,
            Balance = account.Balance,
            Currency = account.Currency,
            Status = account.Status,
            CreatedAt = account.CreatedAt
        };
    }

    public async Task<AccountResponse> GetByIdAsync(Guid userId, Guid accountId)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId)
            ?? throw new KeyNotFoundException("Account not found");

        if (account.UserId != userId)
            throw new UnauthorizedAccessException("Access denied");

        return new AccountResponse
        {
            Id = account.Id,
            AccountNumber = account.AccountNumber,
            Type = account.Type,
            Balance = account.Balance,
            Currency = account.Currency,
            Status = account.Status,
            CreatedAt = account.CreatedAt
        };
    }

    public async Task<BalanceResponse> GetBalanceAsync(Guid userId, Guid accountId)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId)
            ?? throw new KeyNotFoundException("Account not found");

        if (account.UserId != userId)
            throw new UnauthorizedAccessException("Access denied");

        return new BalanceResponse
        {
            AccountId = account.Id,
            Balance = account.Balance,
            ReservedBalance = account.ReservedBalance,
            AvailableBalance = account.Balance - account.ReservedBalance,
            Currency = account.Currency
        };
    }

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
