using UsBankSystem.Core.Domain.Common;

namespace UsBankSystem.Core.Domain.Accounts.Events;

public sealed record AccountCreditedEvent(Guid AccountId, decimal Amount) : IDomainEvent
{
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
