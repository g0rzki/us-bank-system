namespace UsBankSystem.Core.Entities;

public class Account
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string AccountNumber { get; set; } = null!;
    public string Type { get; set; } = "checking";
    public decimal Balance { get; set; }
    public decimal ReservedBalance { get; set; }
    public string Currency { get; set; } = "USD";
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public User User { get; set; } = null!;
    public List<Transaction> Transactions { get; set; } = [];
    public List<Transfer> Transfers { get; set; } = [];
    public List<Card> Cards { get; set; } = [];
    public List<BlikCode> BlikCodes { get; set; } = [];
}