namespace UsBankSystem.Core.Entities;

public class Transaction
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public decimal Amount { get; set; }
    public string Type { get; set; } = null!;      // credit | debit
    public string Status { get; set; } = "completed";
    public string? Description { get; set; }
    public string? ReferenceId { get; set; }        // external ref (ACH, SWIFT itd.)
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Account Account { get; set; } = null!;
}