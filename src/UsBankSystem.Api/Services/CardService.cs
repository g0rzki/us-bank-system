using Microsoft.EntityFrameworkCore;
using UsBankSystem.Api.Integrations;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Core.Domain.Cards;
using UsBankSystem.Core.Entities;
using UsBankSystem.Infrastructure.Persistence;
using Card = UsBankSystem.Core.Entities.Card;
using TopUpCardRequest = UsBankSystem.Api.Models.Requests.TopUpCardRequest;

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

        var gatewayResult = await RegisterWithGatewayAsync(userId, accountId, request);

        if (!gatewayResult.IsSuccess || gatewayResult.CardToken is null)
            throw new GatewayUnavailableException("Card payment gateway is unavailable. Please try again later.");

        var last4 = ExtractLast4(gatewayResult.MaskedPan);
        var expiresAt = BuildExpiryDate(gatewayResult.ExpiryMonth, gatewayResult.ExpiryYear);

        var card = CreateCardEntity(accountId, request, last4, expiresAt, gatewayResult.CardToken, gatewayResult.MaskedPan);
        db.Cards.Add(card);
        await db.SaveChangesAsync();

        // Karty PREPAID wymagają ręcznego przejścia przez lifecycle w card-provider.
        // Robimy to asynchronicznie — nie blokuje odpowiedzi dla klienta.
        if (request.Type == CardType.Prepaid)
            _ = ActivatePrepaidInBackgroundAsync(card.Id, gatewayResult.CardToken);

        return MapToResponse(card);
    }

    public async Task<CardResponse> GetCardAsync(Guid userId, Guid accountId, Guid cardId)
    {
        await VerifyAccountOwnershipAsync(userId, accountId);

        var card = await db.Cards.FirstOrDefaultAsync(c => c.Id == cardId && c.AccountId == accountId)
            ?? throw new KeyNotFoundException("Card not found");

        await SyncStatusFromGatewayAsync(card);

        return MapToResponse(card);
    }

    public async Task<CardResponse> UpdateCardStatusAsync(Guid userId, Guid accountId, Guid cardId, UpdateCardStatusRequest request)
    {
        if (!CardStatus.IsUserSettable(request.Status))
            throw new ArgumentException($"Invalid status '{request.Status}'. Allowed: {CardStatus.Active}, {CardStatus.Blocked}");

        var isJuniorAccount = await db.JuniorAccounts.AnyAsync(j => j.AccountId == accountId);
        if (isJuniorAccount)
        {
            var isParent = await db.JuniorAccounts.AnyAsync(j => j.AccountId == accountId && j.ParentUserId == userId);
            if (!isParent)
                throw new UnauthorizedAccessException("Only a parent can change junior card status");
        }
        else
        {
            await VerifyAccountOwnershipAsync(userId, accountId);
        }

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

        if (card.ExternalCardToken is not null)
        {
            var synced = request.Status == CardStatus.Blocked
                ? await cardsGateway.BlockCardAsync(card.ExternalCardToken)
                : await cardsGateway.UnblockCardAsync(card.ExternalCardToken);

            if (!synced)
                throw new GatewayUnavailableException("Card payment gateway is unavailable. Please try again later.");
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

    public async Task<CardGatewayStatus?> GetExternalCardStatusAsync(Guid userId, Guid accountId, Guid cardId)
    {
        await VerifyAccountOwnershipAsync(userId, accountId);

        var card = await db.Cards.FirstOrDefaultAsync(c => c.Id == cardId && c.AccountId == accountId)
            ?? throw new KeyNotFoundException("Card not found");

        if (card.ExternalCardToken is null)
            return null;

        return await cardsGateway.GetCardStatusAsync(card.ExternalCardToken);
    }

    public async Task<CardResponse> TopUpCardAsync(Guid userId, Guid accountId, Guid cardId, TopUpCardRequest request)
    {
        var isJuniorAccount = await db.JuniorAccounts.AnyAsync(j => j.AccountId == accountId);
        if (isJuniorAccount)
        {
            var isParent = await db.JuniorAccounts.AnyAsync(j => j.AccountId == accountId && j.ParentUserId == userId);
            if (!isParent)
                throw new UnauthorizedAccessException("Only a parent can top up a junior card");
        }
        else
        {
            await VerifyAccountOwnershipAsync(userId, accountId);
        }

        var card = await db.Cards.FirstOrDefaultAsync(c => c.Id == cardId && c.AccountId == accountId)
            ?? throw new KeyNotFoundException("Card not found");

        if (card.Type != CardType.Prepaid)
            throw new InvalidOperationException("Only prepaid cards can be topped up");

        if (card.Status == CardStatus.Expired)
            throw new InvalidOperationException("Cannot top up an expired card");

        if (card.ExternalCardToken is null)
            throw new GatewayUnavailableException("Card is not linked to payment gateway.");

        var ok = await cardsGateway.TopUpAsync(card.ExternalCardToken, request.Amount);
        if (!ok)
            throw new GatewayUnavailableException("Card payment gateway is unavailable. Please try again later.");

        return MapToResponse(card);
    }

    private async Task SyncStatusFromGatewayAsync(Card card)
    {
        if (card.ExternalCardToken is null || card.Status == CardStatus.Expired)
            return;

        var external = await cardsGateway.GetCardStatusAsync(card.ExternalCardToken);
        if (external?.Status is null)
            return;

        var mappedStatus = external.Status.ToUpperInvariant() switch
        {
            "ACTIVE"                                      => CardStatus.Active,
            "BLOCKED"                                     => CardStatus.Blocked,
            "EXPIRED" or "CANCELLED"                      => CardStatus.Expired,
            // REQUESTED/PRODUCING/SHIPPED — karta jeszcze w drodze, traktujemy jako blocked
            "REQUESTED" or "PRODUCING" or "SHIPPED"       => CardStatus.Blocked,
            _                                             => card.Status
        };

        if (mappedStatus == card.Status)
            return;

        logger.LogInformation(
            "Card {CardId} status synced from payment-gateway: {Old} → {New}",
            card.Id, card.Status, mappedStatus);

        card.Status = mappedStatus;
        if (mappedStatus == CardStatus.Blocked && card.BlockedAt is null)
            card.BlockedAt = DateTime.UtcNow;
        else if (mappedStatus == CardStatus.Active)
            card.BlockedAt = null;

        await db.SaveChangesAsync();
    }

    private async Task ActivatePrepaidInBackgroundAsync(Guid cardId, string cardToken)
    {
        // Małe opóźnienie — card-provider potrzebuje chwili po CreateCard zanim przyjmie lifecycle
        await Task.Delay(TimeSpan.FromSeconds(3));

        var ok = await cardsGateway.ActivatePrepaidAsync(cardToken);
        if (ok)
            logger.LogInformation("Prepaid card {CardId} activated in payment-gateway (token {Token})", cardId, cardToken);
        else
            logger.LogWarning("Prepaid card {CardId} activation failed in payment-gateway (token {Token})", cardId, cardToken);
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

    private async Task<CardsGatewayResult> RegisterWithGatewayAsync(Guid userId, Guid accountId, RegisterCardRequest request)
    {
        // Obie karty (debit i prepaid) rejestrujemy jako VIRTUAL — auto-aktywacja po max 1h (60s w dev).
        // Karta "debit" w naszym systemie to karta elektroniczna, nie fizyczna.
        var cardType = request.Type == CardType.Prepaid ? "PREPAID" : "VIRTUAL";
        var initialBalance = 0.0; // saldo ładowane przez topup, nie przy rejestracji

        var gatewayRequest = new IssueCardGatewayRequest(
            UserId: userId.ToString(),
            AccountId: accountId.ToString(),
            CardType: cardType,
            InitialBalance: initialBalance);

        var result = await cardsGateway.IssueCardAsync(gatewayRequest);

        if (!result.IsSuccess)
            logger.LogWarning("Cards gateway issue failed for account {AccountId}: {Error}", accountId, result.Error);

        return result;
    }

    private static Card CreateCardEntity(Guid accountId, RegisterCardRequest request, string last4, DateTime expiresAt, string? externalToken, string? maskedPan) => new()
    {
        Id = Guid.NewGuid(),
        AccountId = accountId,
        Last4 = last4,
        Type = request.Type,
        Status = CardStatus.Active,
        ExternalCardToken = externalToken,
        MaskedPan = maskedPan,
        DailyLimit = request.DailyLimit,
        MonthlyLimit = request.MonthlyLimit,
        ExpiresAt = expiresAt,
        CreatedAt = DateTime.UtcNow
    };

    private static string ExtractLast4(string? maskedPan)
    {
        if (maskedPan is null) return Random.Shared.Next(0, 10000).ToString("D4");
        var digits = maskedPan.Replace(" ", "");
        return digits.Length >= 4 ? digits[^4..] : Random.Shared.Next(0, 10000).ToString("D4");
    }

    private static DateTime BuildExpiryDate(int? month, int? year)
    {
        if (month is null || year is null) return DateTime.UtcNow.AddYears(5);
        var fullYear = year < 100 ? 2000 + year.Value : year.Value;
        return new DateTime(fullYear, month.Value, DateTime.DaysInMonth(fullYear, month.Value), 23, 59, 59, DateTimeKind.Utc);
    }

    private static CardResponse MapToResponse(Card card) => new()
    {
        Id = card.Id,
        AccountId = card.AccountId,
        Last4 = card.Last4,
        MaskedPan = card.MaskedPan,
        ExternalCardToken = card.ExternalCardToken,
        Type = card.Type,
        Status = card.Status,
        DailyLimit = card.DailyLimit,
        MonthlyLimit = card.MonthlyLimit,
        ExpiresAt = card.ExpiresAt,
        BlockedAt = card.BlockedAt,
        CreatedAt = card.CreatedAt
    };
}
