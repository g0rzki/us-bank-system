namespace UsBankSystem.Core.Entities;

public class JuniorAccount
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid ParentUserId { get; set; }
    public DateOnly DateOfBirth { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Account Account { get; set; } = null!;
    public User ParentUser { get; set; } = null!;
}
