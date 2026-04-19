namespace UsBankSystem.Core.Domain.Common;

public interface IDomainEvent
{
    DateTime OccurredAt { get; }
}
