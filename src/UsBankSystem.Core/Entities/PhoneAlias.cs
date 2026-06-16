namespace UsBankSystem.Core.Entities;

public class PhoneAlias
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Phone { get; set; } = null!;
    public string KlikAliasId { get; set; } = null!;
    public string Status { get; set; } = "active";
    public DateTime RegisteredAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Account Account { get; set; } = null!;
}
