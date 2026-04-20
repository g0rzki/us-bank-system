using UsBankSystem.Core.Domain.Cards.Events;
using UsBankSystem.Core.Domain.Common;

namespace UsBankSystem.Core.Domain.Cards;

public class Card : AggregateRoot
{
    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public string Last4 { get; private set; } = null!;
    public string Type { get; private set; } = null!;
    public string Status { get; private set; } = "active";
    public string? ExternalCardToken { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public decimal? DailyLimit { get; private set; }
    public decimal? MonthlyLimit { get; private set; }

    private Card() { }

    public static Card Create(Guid accountId, string last4, string type, string externalCardToken, DateTime expiresAt)
    {
        if (type != "debit" && type != "prepaid")
            throw new ArgumentException("Card type must be 'debit' or 'prepaid'");

        return new Card
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Last4 = last4,
            Type = type,
            ExternalCardToken = externalCardToken,
            ExpiresAt = expiresAt,
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };
    }

    public void UpdateLimits(decimal? dailyLimit, decimal? monthlyLimit)
    {
        if (Type != "prepaid")
            throw new InvalidOperationException("Limits can only be set on prepaid cards");
        if (dailyLimit is < 0 || monthlyLimit is < 0)
            throw new ArgumentException("Limits must be non-negative");

        DailyLimit = dailyLimit;
        MonthlyLimit = monthlyLimit;
        RaiseDomainEvent(new CardLimitsUpdatedEvent(Id, dailyLimit, monthlyLimit));
    }

    public void Block()
    {
        if (Status == "blocked")
            throw new InvalidOperationException("Card is already blocked");

        Status = "blocked";
        RaiseDomainEvent(new CardBlockedEvent(Id));
    }
}
