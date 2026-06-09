namespace UsBankSystem.Core.Entities;

public class BlikCode
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid UserId { get; set; }
    public string Code { get; set; } = null!;
    public string Status { get; set; } = "active";
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Account Account { get; set; } = null!;
    public User User { get; set; } = null!;
}
