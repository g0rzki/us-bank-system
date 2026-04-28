namespace UsBankSystem.Api.Models.Responses;

public class BalanceResponse
{
    public Guid AccountId { get; set; }
    public decimal Balance { get; set; }
    public decimal ReservedBalance { get; set; }
    public decimal AvailableBalance { get; set; }
    public string Currency { get; set; } = null!;
}