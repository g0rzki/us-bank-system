namespace UsBankSystem.Api.Models.Responses;

public class PendingApprovalTransferResponse
{
    public Guid Id { get; set; }
    public Guid FromAccountId { get; set; }
    public string FromAccountNumber { get; set; } = null!;
    public Guid? ToAccountId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = null!;
    public string Channel { get; set; } = null!;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}
