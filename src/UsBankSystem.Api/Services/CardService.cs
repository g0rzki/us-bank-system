using Microsoft.EntityFrameworkCore;
using UsBankSystem.Api.Integrations;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Core.Domain.Cards;
using UsBankSystem.Core.Entities;
using UsBankSystem.Infrastructure.Persistence;
using Card = UsBankSystem.Core.Entities.Card;

namespace UsBankSystem.Api.Services;

public class CardService(AppDbContext db, CardsGateway cardsGateway, ILogger<CardService> logger)
{
    public async Task<List<CardResponse>> GetCardsAsync(Guid userId, Guid accountId)
    {
        await VerifyAccountOwnershipAsync(userId, accountId);

        return await db.Cards
            .Where(c => c.AccountId == accountId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => MapToResponse(c))
            .ToListAsync();
    }

    public async Task<CardResponse> RegisterCardAsync(Guid userId, Guid accountId, RegisterCardRequest request)
    {
        if (!CardType.IsValid(request.Type))
            throw new ArgumentException($"Invalid card type '{request.Type}'. Allowed: {CardType.Debit}, {CardType.Prepaid}");

        ValidateLimits(request.DailyLimit, request.MonthlyLimit);
        await VerifyAccountOwnershipAsync(userId, accountId);

        var isJuniorAccount = await db.JuniorAccounts.AnyAsync(j => j.AccountId == accountId);
        if (isJuniorAccount)
        {
            if (request.Type != CardType.Prepaid)
                throw new InvalidOperationException("Junior accounts can only have prepaid cards");
            var alreadyHasCard = await db.Cards.AnyAsync(c => c.AccountId == accountId && c.Status != CardStatus.Expired);
            if (alreadyHasCard)
                throw new InvalidOperationException("Junior account already has a card");
        }
        else
        {
            await EnsureNoDuplicateActiveCardAsync(accountId, request.Type);
        }

        var last4 = Random.Shared.Next(0, 10000).ToString("D4");
        var expiresAt = DateTime.UtcNow.AddYears(5);

        var gatewayResult = await RegisterWithGatewayAsync(accountId, request, last4, expiresAt);

        var card = CreateCardEntity(accountId, request, last4, expiresAt, gatewayResult.CardToken);
        db.Cards.Add(card);
        await db.SaveChangesAsync();

        return MapToResponse(card);
    }

    public async Task<CardResponse> GetCardAsync(Guid userId, Guid accountId, Guid cardId)
    {
        await VerifyAccountOwnershipAsync(userId, accountId);

        var card = await db.Cards.FirstOrDefaultAsync(c => c.Id == cardId && c.AccountId == accountId)
            ?? throw new KeyNotFoundException("Card not found");

        return MapToResponse(card);
    }

    public async Task<CardResponse> UpdateCardStatusAsync(Guid userId, Guid accountId, Guid cardId, UpdateCardStatusRequest request)
    {
        if (!CardStatus.IsUserSettable(request.Status))
            throw new ArgumentException($"Invalid status '{request.Status}'. Allowed: {CardStatus.Active}, {CardStatus.Blocked}");

        await VerifyAccountOwnershipAsync(userId, accountId);

        var card = await db.Cards.FirstOrDefaultAsync(c => c.Id == cardId && c.AccountId == accountId)
            ?? throw new KeyNotFoundException("Card not found");

        if (card.Status == CardStatus.Expired)
            throw new InvalidOperationException("Cannot change status of an expired card");

        if (request.Status == CardStatus.Active && card.BlockedAt.HasValue)
        {
            var unblockAvailableAt = card.BlockedAt.Value.AddHours(24);
            if (DateTime.UtcNow < unblockAvailableAt)
                throw new InvalidOperationException($"Card cannot be unblocked until {unblockAvailableAt:O}");
        }

        card.Status = request.Status;
        card.BlockedAt = request.Status == CardStatus.Blocked ? DateTime.UtcNow : null;
        await db.SaveChangesAsync();

        return MapToResponse(card);
    }

    public async Task<CardResponse> UpdateCardLimitsAsync(Guid userId, Guid accountId, Guid cardId, UpdateCardLimitsRequest request)
    {
        if (request.DailyLimit is null && request.MonthlyLimit is null)
            throw new ArgumentException("At least one limit must be provided");

        var isJuniorAccount = await db.JuniorAccounts.AnyAsync(j => j.AccountId == accountId);
        if (isJuniorAccount)
        {
            var isParent = await db.JuniorAccounts.AnyAsync(j => j.AccountId == accountId && j.ParentUserId == userId);
            if (!isParent)
                throw new UnauthorizedAccessException("Only a parent can edit junior card limits");
        }
        else
        {
            await VerifyAccountOwnershipAsync(userId, accountId);
        }

        var card = await db.Cards.FirstOrDefaultAsync(c => c.Id == cardId && c.AccountId == accountId)
            ?? throw new KeyNotFoundException("Card not found");

        var effectiveDaily = request.DailyLimit ?? card.DailyLimit;
        var effectiveMonthly = request.MonthlyLimit ?? card.MonthlyLimit;
        ValidateLimits(effectiveDaily, effectiveMonthly);

        if (request.DailyLimit.HasValue) card.DailyLimit = request.DailyLimit.Value;
        if (request.MonthlyLimit.HasValue) card.MonthlyLimit = request.MonthlyLimit.Value;
        await db.SaveChangesAsync();

        return MapToResponse(card);
    }

    private static void ValidateLimits(decimal? dailyLimit, decimal? monthlyLimit)
    {
        if (dailyLimit.HasValue && monthlyLimit.HasValue && monthlyLimit.Value < dailyLimit.Value)
            throw new ArgumentException("Monthly limit cannot be less than daily limit");
    }

    private async Task VerifyAccountOwnershipAsync(Guid userId, Guid accountId)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId)
            ?? throw new KeyNotFoundException("Account not found");

        if (account.UserId == userId)
            return;

        var isJunior = await db.JuniorAccounts.AnyAsync(j => j.AccountId == accountId && j.ParentUserId == userId);
        if (!isJunior)
            throw new UnauthorizedAccessException("Access denied");
    }

    private async Task EnsureNoDuplicateActiveCardAsync(Guid accountId, string type)
    {
        var exists = await db.Cards.AnyAsync(c => c.AccountId == accountId && c.Type == type && c.Status == CardStatus.Active);
        if (exists)
            throw new InvalidOperationException($"Account already has an active {type} card");
    }

    private async Task<CardsGatewayResult> RegisterWithGatewayAsync(Guid accountId, RegisterCardRequest request, string last4, DateTime expiresAt)
    {
        var gatewayRequest = new RegisterCardGatewayRequest(
            AccountId: accountId.ToString(),
            Last4: last4,
            Type: request.Type,
            ExpiresAt: expiresAt.ToString("yyyy-MM-dd"),
            DailyLimit: request.DailyLimit,
            MonthlyLimit: request.MonthlyLimit);

        var result = await cardsGateway.RegisterCardAsync(gatewayRequest);

        if (!result.IsSuccess)
            logger.LogWarning("Cards gateway registration failed for account {AccountId}: {Error}", accountId, result.Error);

        return result;
    }

    private static Card CreateCardEntity(Guid accountId, RegisterCardRequest request, string last4, DateTime expiresAt, string? externalToken) => new()
    {
        Id = Guid.NewGuid(),
        AccountId = accountId,
        Last4 = last4,
        Type = request.Type,
        Status = CardStatus.Active,
        ExternalCardToken = externalToken,
        DailyLimit = request.DailyLimit,
        MonthlyLimit = request.MonthlyLimit,
        ExpiresAt = expiresAt,
        CreatedAt = DateTime.UtcNow
    };

    private static CardResponse MapToResponse(Card card) => new()
    {
        Id = card.Id,
        AccountId = card.AccountId,
        Last4 = card.Last4,
        Type = card.Type,
        Status = card.Status,
        DailyLimit = card.DailyLimit,
        MonthlyLimit = card.MonthlyLimit,
        ExpiresAt = card.ExpiresAt,
        BlockedAt = card.BlockedAt,
        CreatedAt = card.CreatedAt
    };
}
