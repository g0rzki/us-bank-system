using UsBankSystem.Core.Domain.Common;
using UsBankSystem.Core.Domain.Transfers.Events;
using UsBankSystem.Core.Entities;

namespace UsBankSystem.Core.Domain.Transfers;

public class Transfer : AggregateRoot
{
    public Guid Id { get; private set; }
    public Guid FromAccountId { get; private set; }
    public Guid? ToAccountId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "USD";
    public string Channel { get; private set; } = null!;
    public string Status { get; private set; } = "pending";
    public string? ExternalReferenceId { get; private set; }
    public string? Description { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; private set; }
    public bool RequiresApproval { get; private set; } = false;
    public Guid? ApprovedBy { get; private set; }
    public DateTime? ApprovedAt { get; private set; }
    public DateTime? RejectedAt { get; private set; }

    public Account FromAccount { get; private set; } = null!;
    public Account? ToAccount { get; private set; }

    private Transfer() { }

    public static Transfer Create(Guid fromAccountId, Guid? toAccountId, decimal amount, string channel, string currency = "USD", string? description = null, bool requiresApproval = false)
    {
        var transfer = new Transfer
        {
            Id = Guid.NewGuid(),
            FromAccountId = fromAccountId,
            ToAccountId = toAccountId,
            Amount = amount,
            Channel = channel,
            Currency = currency,
            Description = description,
            RequiresApproval = requiresApproval,
            Status = requiresApproval ? "pending_approval" : "pending",
            CreatedAt = DateTime.UtcNow
        };

        transfer.RaiseDomainEvent(new TransferCreatedEvent(transfer.Id, fromAccountId, amount));
        return transfer;
    }

    public void Approve(Guid approvedBy)
    {
        if (Status != "pending_approval")
            throw new InvalidOperationException("Transfer is not awaiting approval");

        ApprovedBy = approvedBy;
        ApprovedAt = DateTime.UtcNow;
        Status = "pending";
        RaiseDomainEvent(new TransferApprovedEvent(Id, approvedBy));
    }

    public void Reject()
    {
        if (Status != "pending_approval")
            throw new InvalidOperationException("Transfer is not awaiting approval");

        RejectedAt = DateTime.UtcNow;
        Status = "rejected";
        RaiseDomainEvent(new TransferRejectedEvent(Id));
    }

    public void Complete()
    {
        Status = "completed";
        CompletedAt = DateTime.UtcNow;
    }

    public void Fail()
    {
        Status = "failed";
    }
}
