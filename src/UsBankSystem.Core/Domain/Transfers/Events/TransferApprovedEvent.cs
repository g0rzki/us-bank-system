using UsBankSystem.Core.Domain.Common;

namespace UsBankSystem.Core.Domain.Transfers.Events;

public sealed record TransferApprovedEvent(Guid TransferId, Guid ApprovedBy) : IDomainEvent
{
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
