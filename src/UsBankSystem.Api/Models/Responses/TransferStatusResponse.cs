namespace UsBankSystem.Api.Models.Responses;

public class TransferStatusResponse
{
    public Guid TransferId { get; set; }
    public string Status { get; set; } = null!;
    public string Channel { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ExternalReferenceId { get; set; }
}
