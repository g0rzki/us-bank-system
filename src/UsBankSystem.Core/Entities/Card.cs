namespace UsBankSystem.Core.Entities;

public class Card
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Last4 { get; set; } = null!;
    public string Type { get; set; } = null!;         // debit | credit
    public string Status { get; set; } = "active";    // active | blocked | expired
    public string? ExternalCardToken { get; set; }    // token z modułu kart
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Account Account { get; set; } = null!;
}