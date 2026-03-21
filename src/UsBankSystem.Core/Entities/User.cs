namespace UsBankSystem.Core.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<Account> Accounts { get; set; } = [];
}