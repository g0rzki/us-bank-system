namespace UsBankSystem.Api.Models.Responses;

public class TransactionResponse
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string Type { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string? Description { get; set; }
    public string? ReferenceId { get; set; }
    public DateTime CreatedAt { get; set; }
}