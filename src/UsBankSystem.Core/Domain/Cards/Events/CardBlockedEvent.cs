using UsBankSystem.Core.Domain.Common;

namespace UsBankSystem.Core.Domain.Cards.Events;

public sealed record CardBlockedEvent(Guid CardId) : IDomainEvent
{
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
