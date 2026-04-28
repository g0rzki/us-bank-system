namespace UsBankSystem.Api.Models.Responses;

public class TransferResponse
{
    public Guid Id { get; set; }
    public Guid FromAccountId { get; set; }
    public Guid? ToAccountId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = null!;
    public string Channel { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool RequiresApproval { get; set; }
}