using Microsoft.EntityFrameworkCore;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Core.Domain.Accounts;
using UsBankSystem.Core.Domain.Cards;
using UsBankSystem.Core.Domain.Common;
using Account = UsBankSystem.Core.Entities.Account;
using UsBankSystem.Core.Entities;
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

    public async Task<List<AccountResponse>> GetAllAsync(Guid userId)
    {
        var otherJuniorAccountIds = await db.JuniorAccounts
            .Where(j => j.Account.UserId != userId)
            .Select(j => j.AccountId)
            .ToListAsync();

        return await db.Accounts
            .Where(a => a.UserId == userId && !otherJuniorAccountIds.Contains(a.Id))
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

    public async Task<List<JuniorAccountResponse>> GetJuniorAccountsAsync(Guid userId, Guid parentAccountId)
    {
        var parentAccount = await db.Accounts.FirstOrDefaultAsync(a => a.Id == parentAccountId)
            ?? throw new KeyNotFoundException("Account not found");

        if (parentAccount.UserId != userId)
            throw new UnauthorizedAccessException("Access denied");

        return await db.JuniorAccounts
            .Include(j => j.Account)
            .ThenInclude(a => a.User)
            .Include(j => j.Account)
            .ThenInclude(a => a.Cards)
            .Where(j => j.ParentUserId == userId)
            .OrderBy(j => j.CreatedAt)
            .Select(j => new JuniorAccountResponse
            {
                JuniorAccountId = j.Id,
                AccountId = j.AccountId,
                AccountNumber = j.Account.AccountNumber,
                FirstName = j.Account.User.FirstName,
                LastName = j.Account.User.LastName,
                Balance = j.Account.Balance,
                Currency = j.Account.Currency,
                Status = j.Account.Status,
                DateOfBirth = j.DateOfBirth,
                CardDailyLimit = j.Account.Cards
                    .Where(c => c.Type == CardType.Prepaid && c.Status == CardStatus.Active)
                    .Select(c => c.DailyLimit)
                    .FirstOrDefault(),
                CardMonthlyLimit = j.Account.Cards
                    .Where(c => c.Type == CardType.Prepaid && c.Status == CardStatus.Active)
                    .Select(c => c.MonthlyLimit)
                    .FirstOrDefault(),
                CreatedAt = j.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<JuniorAccountResponse> CreateJuniorAsync(Guid userId, CreateJuniorAccountRequest request)
    {
        JuniorAccountValidator.ValidateDateOfBirth(request.DateOfBirth);

        var parentAccount = await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.ParentAccountId && a.UserId == userId && a.Status == AccountStatus.Active)
                            ?? throw new KeyNotFoundException("Parent account not found or inactive");

        var emailTaken = await db.Users.AnyAsync(u => u.Email == request.Email.ToLowerInvariant());
        if (emailTaken)
            throw new InvalidOperationException("Email is already taken");

        var juniorUser = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email.ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            FirstName = request.FirstName,
            LastName = request.LastName,
            Status = AccountStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        db.Users.Add(juniorUser);

        var juniorAccount = new Account
        {
            Id = Guid.NewGuid(),
            UserId = juniorUser.Id,
            AccountNumber = await GenerateAccountNumberAsync(db),
            Type = AccountType.Checking,
            Currency = CurrencyCode.USD,
            Balance = 0,
            ReservedBalance = 0,
            Status = AccountStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        db.Accounts.Add(juniorAccount);

        var juniorLink = new UsBankSystem.Core.Entities.JuniorAccount
        {
            Id = Guid.NewGuid(),
            AccountId = juniorAccount.Id,
            ParentUserId = userId,
            DateOfBirth = request.DateOfBirth,
            CreatedAt = DateTime.UtcNow
        };

        db.JuniorAccounts.Add(juniorLink);
        await db.SaveChangesAsync();

        return new JuniorAccountResponse
        {
            JuniorAccountId = juniorLink.Id,
            AccountId = juniorAccount.Id,
            AccountNumber = juniorAccount.AccountNumber,
            FirstName = juniorUser.FirstName,
            LastName = juniorUser.LastName,
            Balance = juniorAccount.Balance,
            Currency = juniorAccount.Currency,
            Status = juniorAccount.Status,
            DateOfBirth = juniorLink.DateOfBirth,
            CardDailyLimit = null,
            CardMonthlyLimit = null,
            CreatedAt = juniorLink.CreatedAt
        };
    }

	public async Task<CardResponse> UpdateJuniorLimitAsync(Guid userId, Guid juniorAccountId, UpdateJuniorLimitRequest request)
	{
    	if (request.DailyLimit is null && request.MonthlyLimit is null)
        	throw new ArgumentException("At least one limit (DailyLimit or MonthlyLimit) must be provided");

    	var juniorLink = await db.JuniorAccounts
        	.FirstOrDefaultAsync(j => j.AccountId == juniorAccountId)
        	?? throw new KeyNotFoundException("Junior account not found");

    	if (juniorLink.ParentUserId != userId)
        	throw new UnauthorizedAccessException("Access denied");

    	var card = await db.Cards
        	.FirstOrDefaultAsync(c => c.AccountId == juniorAccountId && c.Type == CardType.Prepaid && c.Status == CardStatus.Active)
        	?? throw new KeyNotFoundException("No active prepaid card found for this junior account");

    	if (request.DailyLimit.HasValue)
        	card.DailyLimit = request.DailyLimit.Value;

    	if (request.MonthlyLimit.HasValue)
        	card.MonthlyLimit = request.MonthlyLimit.Value;

    	await db.SaveChangesAsync();

    	return new CardResponse
    	{
        	Id = card.Id,
        	AccountId = card.AccountId,
        	Last4 = card.Last4,
        	Type = card.Type,
        	Status = card.Status,
        	DailyLimit = card.DailyLimit,
        	MonthlyLimit = card.MonthlyLimit,
        	ExpiresAt = card.ExpiresAt,
        	CreatedAt = card.CreatedAt
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
