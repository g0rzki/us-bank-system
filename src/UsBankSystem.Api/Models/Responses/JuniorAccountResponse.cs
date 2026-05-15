namespace UsBankSystem.Api.Models.Responses;

public class JuniorAccountResponse
{
    public Guid JuniorAccountId { get; set; }
    public Guid AccountId { get; set; }
    public string AccountNumber { get; set; } = null!;
    public decimal Balance { get; set; }
    public string Currency { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateOnly DateOfBirth { get; set; }
    public decimal? CardDailyLimit { get; set; }
    public decimal? CardMonthlyLimit { get; set; }
    public DateTime CreatedAt { get; set; }
}
