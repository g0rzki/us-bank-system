using UsBankSystem.Core.Domain.Common;

namespace UsBankSystem.Core.Domain.Transfers.Events;

public sealed record TransferCreatedEvent(Guid TransferId, Guid FromAccountId, decimal Amount) : IDomainEvent
{
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
