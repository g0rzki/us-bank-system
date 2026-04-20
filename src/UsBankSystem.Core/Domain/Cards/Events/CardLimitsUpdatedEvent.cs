using UsBankSystem.Core.Domain.Common;

namespace UsBankSystem.Core.Domain.Cards.Events;

public sealed record CardLimitsUpdatedEvent(Guid CardId, decimal? DailyLimit, decimal? MonthlyLimit) : IDomainEvent
{
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
