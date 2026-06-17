namespace UsBankSystem.Api.Models.Responses;

public class BlikAuthorizationResponse
{
    public Guid Id { get; set; }
    public string KlikTransactionId { get; set; } = null!;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = null!;
    public string MerchantName { get; set; } = null!;
    public bool IsOnUs { get; set; }
    public DateTime ExpiryTime { get; set; }
    public string Status { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public Guid? LocalTransactionId { get; set; }
}
