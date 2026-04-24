namespace UsBankSystem.Api.Models.Responses;

public class AccountResponse
{
    public Guid Id { get; set; }
    public string AccountNumber { get; set; } = null!;
    public string Type { get; set; } = null!;
    public decimal Balance { get; set; }
    public string Currency { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}
