namespace UsBankSystem.Core.Entities;

public class BlikAuthorization
{
    public Guid Id { get; set; }
    public string KlikTransactionId { get; set; } = null!;
    public Guid UserId { get; set; }
    public Guid AccountId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = null!;
    public string MerchantName { get; set; } = null!;
    public bool IsOnUs { get; set; }
    public DateTime ExpiryTime { get; set; }
    public string Status { get; set; } = "pending";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }
    public Guid? LocalTransactionId { get; set; }
    public User User { get; set; } = null!;
}
