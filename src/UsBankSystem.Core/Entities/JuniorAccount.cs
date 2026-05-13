namespace UsBankSystem.Core.Entities;

public class JuniorAccount
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }           // konto junior
    public Guid ParentAccountId { get; set; }     // konto rodzica
    public DateOnly DateOfBirth { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Account Account { get; set; } = null!;
    public Account ParentAccount { get; set; } = null!;
}