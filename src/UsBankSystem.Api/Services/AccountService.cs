using Microsoft.EntityFrameworkCore;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Core.Domain.Accounts;
using UsBankSystem.Core.Domain.Common;
using UsBankSystem.Core.Entities;
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

        var accountCount = await db.Accounts
            .Where(a => a.UserId == userId && !db.JuniorAccounts.Any(j => j.AccountId == a.Id))
            .CountAsync();
        if (accountCount >= 5)
            throw new InvalidOperationException("Account limit reached. Maximum 5 accounts per user.");

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

        return MapToResponse(account);
    }

    public async Task<List<AccountResponse>> GetAllAsync(Guid userId)
    {
        var juniorAccountIds = await db.JuniorAccounts
            .Where(j => j.Account.UserId != userId)
            .Select(j => j.AccountId)
            .ToListAsync();

        return await db.Accounts
            .Where(a => a.UserId == userId && !juniorAccountIds.Contains(a.Id))
            .OrderBy(a => a.CreatedAt)
            .Select(a => new AccountResponse
            {
                Id = a.Id,
                AccountNumber = a.AccountNumber,
                Type = a.Type,
                Balance = a.Balance,
                Currency = a.Currency,
                Status = a.Status,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<AccountResponse> GetByIdAsync(Guid userId, Guid accountId)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId)
            ?? throw new KeyNotFoundException("Account not found");

        if (account.UserId != userId)
            throw new UnauthorizedAccessException("Access denied");

        return MapToResponse(account);
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

    private static AccountResponse MapToResponse(Account account) => new()
    {
        Id = account.Id,
        AccountNumber = account.AccountNumber,
        Type = account.Type,
        Balance = account.Balance,
        Currency = account.Currency,
        Status = account.Status,
        CreatedAt = account.CreatedAt
    };

    internal static async Task<string> GenerateAccountNumberAsync(AppDbContext db)
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
