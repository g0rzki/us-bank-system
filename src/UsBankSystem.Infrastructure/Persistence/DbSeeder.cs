using Microsoft.EntityFrameworkCore;
using UsBankSystem.Core.Entities;

namespace UsBankSystem.Infrastructure.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext context)
    {
        if (await context.Users.AnyAsync())
            return;

        // Users
        var user1 = new User
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Email = "john.doe@example.com",
            PasswordHash = BCryptHash("Test123!"),
            FirstName = "John",
            LastName = "Doe",
            Status = "active",
            CreatedAt = DateTime.UtcNow.AddMonths(-6)
        };

        var user2 = new User
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Email = "jane.smith@example.com",
            PasswordHash = BCryptHash("Test123!"),
            FirstName = "Jane",
            LastName = "Smith",
            Status = "active",
            CreatedAt = DateTime.UtcNow.AddMonths(-3)
        };

        var user3 = new User
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Email = "bob.wilson@example.com",
            PasswordHash = BCryptHash("Test123!"),
            FirstName = "Bob",
            LastName = "Wilson",
            Status = "active",
            CreatedAt = DateTime.UtcNow.AddMonths(-1)
        };

        context.Users.AddRange(user1, user2, user3);

        // Accounts
        var account1Checking = new Account
        {
            Id = Guid.Parse("aaaa1111-1111-1111-1111-111111111111"),
            UserId = user1.Id,
            AccountNumber = "1000000001",
            Type = "checking",
            Balance = 5420.50m,
            ReservedBalance = 0m,
            Currency = "USD",
            Status = "active",
            CreatedAt = user1.CreatedAt
        };

        var account1Savings = new Account
        {
            Id = Guid.Parse("aaaa1111-2222-2222-2222-222222222222"),
            UserId = user1.Id,
            AccountNumber = "1000000002",
            Type = "savings",
            Balance = 15000.00m,
            ReservedBalance = 0m,
            Currency = "USD",
            Status = "active",
            CreatedAt = user1.CreatedAt.AddDays(7)
        };

        var account2Checking = new Account
        {
            Id = Guid.Parse("bbbb2222-1111-1111-1111-111111111111"),
            UserId = user2.Id,
            AccountNumber = "2000000001",
            Type = "checking",
            Balance = 2150.75m,
            ReservedBalance = 100.00m,
            Currency = "USD",
            Status = "active",
            CreatedAt = user2.CreatedAt
        };

        var account3Checking = new Account
        {
            Id = Guid.Parse("cccc3333-1111-1111-1111-111111111111"),
            UserId = user3.Id,
            AccountNumber = "3000000001",
            Type = "checking",
            Balance = 890.25m,
            ReservedBalance = 0m,
            Currency = "USD",
            Status = "active",
            CreatedAt = user3.CreatedAt
        };

        context.Accounts.AddRange(account1Checking, account1Savings, account2Checking, account3Checking);

        // Transactions
        var transactions = new List<Transaction>
        {
            new()
            {
                Id = Guid.NewGuid(),
                AccountId = account1Checking.Id,
                Amount = 3500.00m,
                Type = "credit",
                Status = "completed",
                Description = "Payroll deposit",
                CreatedAt = DateTime.UtcNow.AddDays(-14)
            },
            new()
            {
                Id = Guid.NewGuid(),
                AccountId = account1Checking.Id,
                Amount = 150.00m,
                Type = "debit",
                Status = "completed",
                Description = "Grocery store",
                CreatedAt = DateTime.UtcNow.AddDays(-10)
            },
            new()
            {
                Id = Guid.NewGuid(),
                AccountId = account1Checking.Id,
                Amount = 89.99m,
                Type = "debit",
                Status = "completed",
                Description = "Online subscription",
                CreatedAt = DateTime.UtcNow.AddDays(-5)
            },
            new()
            {
                Id = Guid.NewGuid(),
                AccountId = account2Checking.Id,
                Amount = 2500.00m,
                Type = "credit",
                Status = "completed",
                Description = "Payroll deposit",
                CreatedAt = DateTime.UtcNow.AddDays(-7)
            },
            new()
            {
                Id = Guid.NewGuid(),
                AccountId = account2Checking.Id,
                Amount = 45.50m,
                Type = "debit",
                Status = "completed",
                Description = "Gas station",
                CreatedAt = DateTime.UtcNow.AddDays(-3)
            }
        };

        context.Transactions.AddRange(transactions);

        // Transfers
        var transfers = new List<Transfer>
        {
            new()
            {
                Id = Guid.NewGuid(),
                FromAccountId = account1Checking.Id,
                ToAccountId = account1Savings.Id,
                Amount = 500.00m,
                Currency = "USD",
                Channel = "internal",
                Status = "completed",
                Description = "Monthly savings",
                CreatedAt = DateTime.UtcNow.AddDays(-12),
                CompletedAt = DateTime.UtcNow.AddDays(-12)
            },
            new()
            {
                Id = Guid.NewGuid(),
                FromAccountId = account1Checking.Id,
                ToAccountId = account2Checking.Id,
                Amount = 200.00m,
                Currency = "USD",
                Channel = "internal",
                Status = "completed",
                Description = "Rent split",
                CreatedAt = DateTime.UtcNow.AddDays(-8),
                CompletedAt = DateTime.UtcNow.AddDays(-8)
            },
            new()
            {
                Id = Guid.NewGuid(),
                FromAccountId = account2Checking.Id,
                ToAccountId = null,
                Amount = 1000.00m,
                Currency = "USD",
                Channel = "ach",
                Status = "pending",
                ExternalReferenceId = "ACH-2024-001234",
                Description = "External transfer",
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            }
        };

        context.Transfers.AddRange(transfers);

        // Cards
        var cards = new List<Card>
        {
            new()
            {
                Id = Guid.NewGuid(),
                AccountId = account1Checking.Id,
                Last4 = "4532",
                Type = "debit",
                Status = "active",
                ExternalCardToken = "tok_visa_debit_001",
                ExpiresAt = DateTime.UtcNow.AddYears(3),
                CreatedAt = account1Checking.CreatedAt.AddDays(1)
            },
            new()
            {
                Id = Guid.NewGuid(),
                AccountId = account2Checking.Id,
                Last4 = "8901",
                Type = "debit",
                Status = "active",
                ExternalCardToken = "tok_visa_debit_002",
                ExpiresAt = DateTime.UtcNow.AddYears(2),
                CreatedAt = account2Checking.CreatedAt.AddDays(1)
            },
            new()
            {
                Id = Guid.NewGuid(),
                AccountId = account1Checking.Id,
                Last4 = "1234",
                Type = "credit",
                Status = "blocked",
                ExternalCardToken = "tok_mc_credit_001",
                ExpiresAt = DateTime.UtcNow.AddMonths(-2),
                CreatedAt = account1Checking.CreatedAt.AddMonths(1)
            }
        };

        context.Cards.AddRange(cards);

        // BlikCodes
        var blikCodes = new List<BlikCode>
        {
            new()
            {
                Id = Guid.NewGuid(),
                AccountId = account1Checking.Id,
                CodeHash = BCryptHash("123456"),
                Used = false,
                ExpiresAt = DateTime.UtcNow.AddMinutes(2),
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                Id = Guid.NewGuid(),
                AccountId = account2Checking.Id,
                CodeHash = BCryptHash("654321"),
                Used = true,
                ExpiresAt = DateTime.UtcNow.AddMinutes(-5),
                CreatedAt = DateTime.UtcNow.AddMinutes(-7)
            }
        };

        context.BlikCodes.AddRange(blikCodes);

        await context.SaveChangesAsync();
    }

    private static string BCryptHash(string input)
    {
        return BCrypt.Net.BCrypt.HashPassword(input);
    }
}
