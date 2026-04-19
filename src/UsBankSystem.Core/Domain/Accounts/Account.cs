using UsBankSystem.Core.Domain.Accounts.Events;
using UsBankSystem.Core.Domain.Common;
using UsBankSystem.Core.Entities;

namespace UsBankSystem.Core.Domain.Accounts;

public class Account : AggregateRoot
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string AccountNumber { get; private set; } = null!;
    public string Type { get; private set; } = "checking";
    public decimal Balance { get; private set; }
    public decimal ReservedBalance { get; private set; }
    public string Currency { get; private set; } = "USD";
    public string Status { get; private set; } = "active";
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public User User { get; private set; } = null!;
    public List<Transaction> Transactions { get; private set; } = [];
    public List<Transfer> Transfers { get; private set; } = [];
    public List<Card> Cards { get; private set; } = [];
    public List<BlikCode> BlikCodes { get; private set; } = [];

    private Account() { }

    public static Account Create(Guid userId, string accountNumber, string type, string currency = "USD")
    {
        return new Account
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AccountNumber = accountNumber,
            Type = type,
            Currency = currency,
            Balance = 0,
            ReservedBalance = 0,
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Debit(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be positive");
        if (Balance - ReservedBalance < amount)
            throw new InvalidOperationException("Insufficient funds");

        Balance -= amount;
        RaiseDomainEvent(new AccountDebitedEvent(Id, amount));
    }

    public void Credit(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be positive");

        Balance += amount;
        RaiseDomainEvent(new AccountCreditedEvent(Id, amount));
    }

    public void Reserve(decimal amount)
    {
        if (Balance - ReservedBalance < amount)
            throw new InvalidOperationException("Insufficient funds to reserve");

        ReservedBalance += amount;
    }

    public void ReleaseReservation(decimal amount)
    {
        ReservedBalance = Math.Max(0, ReservedBalance - amount);
    }
}
