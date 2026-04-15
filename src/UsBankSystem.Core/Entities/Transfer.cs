namespace UsBankSystem.Core.Entities;

public class Transfer
{
    public Guid Id { get; set; }
    public Guid FromAccountId { get; set; }
    public Guid? ToAccountId { get; set; }          // null przy transferach zewnętrznych
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string Channel { get; set; } = null!;    // internal | ach | rtp | fednow | swift
    public string Status { get; set; } = "pending"; // pending | pending_approval | completed | failed | rejected
    public string? ExternalReferenceId { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    // Approval flow — używane dla transakcji z konta junior
    public bool RequiresApproval { get; set; } = false;
    public Guid? ApprovedBy { get; set; }           // UserId rodzica który zatwierdził
    public DateTime? ApprovedAt { get; set; }
    public DateTime? RejectedAt { get; set; }

    public Account FromAccount { get; set; } = null!;
    public Account? ToAccount { get; set; }
}