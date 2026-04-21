using UsBankSystem.Core.Domain.Common;

namespace UsBankSystem.Core.Domain.Transfers.Events;

public sealed record TransferRejectedEvent(Guid TransferId) : IDomainEvent
{
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
