using Microsoft.EntityFrameworkCore;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Core.Domain.Accounts;
using UsBankSystem.Core.Domain.Cards;
using UsBankSystem.Core.Domain.Common;
using UsBankSystem.Core.Entities;
using Account = UsBankSystem.Core.Entities.Account;
using Card = UsBankSystem.Core.Entities.Card;
using UsBankSystem.Infrastructure.Persistence;

namespace UsBankSystem.Api.Services;

public class JuniorService(AppDbContext db)
{
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
            AccountNumber = await AccountService.GenerateAccountNumberAsync(db),
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

    public async Task<CardResponse> AddCardAsync(Guid userId, Guid juniorAccountId, AddJuniorCardRequest request)
    {
        var juniorLink = await db.JuniorAccounts
            .Include(j => j.Account)
            .FirstOrDefaultAsync(j => j.AccountId == juniorAccountId)
            ?? throw new KeyNotFoundException("Junior account not found");

        if (juniorLink.ParentUserId != userId)
            throw new UnauthorizedAccessException("Access denied");

        var exists = await db.Cards.AnyAsync(c => c.AccountId == juniorAccountId && c.Type == CardType.Prepaid && c.Status == CardStatus.Active);
        if (exists)
            throw new InvalidOperationException("Junior account already has an active prepaid card");

        var card = new Card
        {
            Id = Guid.NewGuid(),
            AccountId = juniorAccountId,
            Last4 = request.Last4,
            Type = CardType.Prepaid,
            Status = CardStatus.Active,
            ExternalCardToken = request.ExternalCardToken,
            DailyLimit = request.DailyLimit,
            MonthlyLimit = request.MonthlyLimit,
            ExpiresAt = request.ExpiresAt,
            CreatedAt = DateTime.UtcNow
        };

        db.Cards.Add(card);
        await db.SaveChangesAsync();

        return MapCardToResponse(card);
    }

    public async Task<CardResponse> UpdateLimitAsync(Guid userId, Guid juniorAccountId, UpdateJuniorLimitRequest request)
    {
        if (request.DailyLimit is null && request.MonthlyLimit is null)
            throw new ArgumentException("At least one limit (DailyLimit or MonthlyLimit) must be provided");

        var juniorLink = await db.JuniorAccounts.FirstOrDefaultAsync(j => j.AccountId == juniorAccountId)
            ?? throw new KeyNotFoundException("Junior account not found");

        if (juniorLink.ParentUserId != userId)
            throw new UnauthorizedAccessException("Access denied");

        var card = await db.Cards.FirstOrDefaultAsync(c => c.AccountId == juniorAccountId && c.Type == CardType.Prepaid && c.Status == CardStatus.Active)
            ?? throw new KeyNotFoundException("No active prepaid card found for this junior account");

        if (request.DailyLimit.HasValue)
            card.DailyLimit = request.DailyLimit.Value;

        if (request.MonthlyLimit.HasValue)
            card.MonthlyLimit = request.MonthlyLimit.Value;

        await db.SaveChangesAsync();

        return MapCardToResponse(card);
    }

    private static CardResponse MapCardToResponse(Card card) => new()
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
