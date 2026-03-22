namespace UsBankSystem.Core.Entities;

public class BlikCode
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string CodeHash { get; set; } = null!;     // hash kodu 6-cyfrowego
    public bool Used { get; set; } = false;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Account Account { get; set; } = null!;
}