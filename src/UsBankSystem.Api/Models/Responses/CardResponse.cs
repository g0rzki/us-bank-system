namespace UsBankSystem.Api.Models.Responses;

public class CardResponse
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Last4 { get; set; } = null!;
    public string? MaskedPan { get; set; }
    public string? ExternalCardToken { get; set; }
    public string Type { get; set; } = null!;
    public string Status { get; set; } = null!;
    public decimal? DailyLimit { get; set; }
    public decimal? MonthlyLimit { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? BlockedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
