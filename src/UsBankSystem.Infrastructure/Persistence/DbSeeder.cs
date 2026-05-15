using Microsoft.EntityFrameworkCore;
using UsBankSystem.Core.Domain.Transfers;
using UsBankSystem.Core.Entities;
using Transfer = UsBankSystem.Core.Entities.Transfer;

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
                FromAccountId = account1Checking.Id,
                ToAccountId = account2Checking.Id,
                Amount = 350.00m,
                Currency = "USD",
                Channel = "rtp",
                Status = "completed",
                ExternalReferenceId = "RTP-REF-20240418-001",
                Description = "Freelance payment",
                CreatedAt = DateTime.UtcNow.AddDays(-6),
                CompletedAt = DateTime.UtcNow.AddDays(-6)
            },
            new()
            {
                Id = Guid.NewGuid(),
                FromAccountId = account1Checking.Id,
                ToAccountId = account2Checking.Id,
                Amount = 75.50m,
                Currency = "USD",
                Channel = "fednow",
                Status = "completed",
                ExternalReferenceId = "FEDNOW-REF-20240420-001",
                Description = "Dinner split",
                CreatedAt = DateTime.UtcNow.AddDays(-3),
                CompletedAt = DateTime.UtcNow.AddDays(-3)
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
            },
            new()
            {
                Id = Guid.NewGuid(),
                FromAccountId = account1Checking.Id,
                ToAccountId = account3Checking.Id,
                Amount = 120.00m,
                Currency = "USD",
                Channel = "ach",
                Status = "failed",
                ExternalReferenceId = "ACH-2024-001235",
                Description = "Invoice payment",
                CreatedAt = DateTime.UtcNow.AddDays(-2)
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
                Type = "debit",
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

        // Junior accounts — john.doe ma 3, jane.smith ma 3, bob.wilson ma 0
        var juniorAccounts = new List<Account>
        {
            new() { Id = Guid.Parse("dddd4444-1111-1111-1111-111111111111"), UserId = user1.Id, AccountNumber = "4000000001", Type = "checking", Balance = 250.00m, ReservedBalance = 30.00m, Currency = "USD", Status = "active", CreatedAt = DateTime.UtcNow.AddMonths(-2) },
            new() { Id = Guid.Parse("dddd4444-2222-2222-2222-222222222222"), UserId = user1.Id, AccountNumber = "4000000002", Type = "checking", Balance = 80.50m,  ReservedBalance = 0m,     Currency = "USD", Status = "active", CreatedAt = DateTime.UtcNow.AddMonths(-1) },
            new() { Id = Guid.Parse("dddd4444-3333-3333-3333-333333333333"), UserId = user1.Id, AccountNumber = "4000000003", Type = "checking", Balance = 510.00m, ReservedBalance = 0m,     Currency = "USD", Status = "active", CreatedAt = DateTime.UtcNow.AddDays(-15) },
            new() { Id = Guid.Parse("dddd4444-4444-4444-4444-444444444444"), UserId = user2.Id, AccountNumber = "4000000004", Type = "checking", Balance = 120.00m, ReservedBalance = 0m,     Currency = "USD", Status = "active", CreatedAt = DateTime.UtcNow.AddMonths(-2) },
            new() { Id = Guid.Parse("dddd4444-5555-5555-5555-555555555555"), UserId = user2.Id, AccountNumber = "4000000005", Type = "checking", Balance = 340.75m, ReservedBalance = 0m,     Currency = "USD", Status = "active", CreatedAt = DateTime.UtcNow.AddMonths(-1) },
            new() { Id = Guid.Parse("dddd4444-6666-6666-6666-666666666666"), UserId = user2.Id, AccountNumber = "4000000006", Type = "checking", Balance = 60.00m,  ReservedBalance = 0m,     Currency = "USD", Status = "active", CreatedAt = DateTime.UtcNow.AddDays(-10) },
        };

        context.Accounts.AddRange(juniorAccounts);

        var juniorLinks = new List<JuniorAccount>
        {
            new() { Id = Guid.Parse("eeee5555-1111-1111-1111-111111111111"), AccountId = Guid.Parse("dddd4444-1111-1111-1111-111111111111"), ParentAccountId = account1Checking.Id, DateOfBirth = new DateOnly(2015, 6, 15),  CreatedAt = DateTime.UtcNow.AddMonths(-2) },
            new() { Id = Guid.Parse("eeee5555-2222-2222-2222-222222222222"), AccountId = Guid.Parse("dddd4444-2222-2222-2222-222222222222"), ParentAccountId = account1Checking.Id, DateOfBirth = new DateOnly(2013, 3, 22),  CreatedAt = DateTime.UtcNow.AddMonths(-1) },
            new() { Id = Guid.Parse("eeee5555-3333-3333-3333-333333333333"), AccountId = Guid.Parse("dddd4444-3333-3333-3333-333333333333"), ParentAccountId = account1Checking.Id, DateOfBirth = new DateOnly(2016, 11, 5),  CreatedAt = DateTime.UtcNow.AddDays(-15) },
            new() { Id = Guid.Parse("eeee5555-4444-4444-4444-444444444444"), AccountId = Guid.Parse("dddd4444-4444-4444-4444-444444444444"), ParentAccountId = account2Checking.Id, DateOfBirth = new DateOnly(2014, 8, 30),  CreatedAt = DateTime.UtcNow.AddMonths(-2) },
            new() { Id = Guid.Parse("eeee5555-5555-5555-5555-555555555555"), AccountId = Guid.Parse("dddd4444-5555-5555-5555-555555555555"), ParentAccountId = account2Checking.Id, DateOfBirth = new DateOnly(2017, 1, 12),  CreatedAt = DateTime.UtcNow.AddMonths(-1) },
            new() { Id = Guid.Parse("eeee5555-6666-6666-6666-666666666666"), AccountId = Guid.Parse("dddd4444-6666-6666-6666-666666666666"), ParentAccountId = account2Checking.Id, DateOfBirth = new DateOnly(2012, 5, 19),  CreatedAt = DateTime.UtcNow.AddDays(-10) },
        };

        context.JuniorAccounts.AddRange(juniorLinks);

        var juniorCards = new List<Card>
        {
            new() { Id = Guid.NewGuid(), AccountId = Guid.Parse("dddd4444-1111-1111-1111-111111111111"), Last4 = "9001", Type = "prepaid", Status = "active", DailyLimit = 50.00m,  MonthlyLimit = 300.00m, ExpiresAt = DateTime.UtcNow.AddYears(3), CreatedAt = DateTime.UtcNow.AddMonths(-2).AddDays(1) },
            new() { Id = Guid.NewGuid(), AccountId = Guid.Parse("dddd4444-2222-2222-2222-222222222222"), Last4 = "9002", Type = "prepaid", Status = "active", DailyLimit = 30.00m,  MonthlyLimit = 150.00m, ExpiresAt = DateTime.UtcNow.AddYears(3), CreatedAt = DateTime.UtcNow.AddMonths(-1).AddDays(1) },
            new() { Id = Guid.NewGuid(), AccountId = Guid.Parse("dddd4444-3333-3333-3333-333333333333"), Last4 = "9003", Type = "prepaid", Status = "active", DailyLimit = 100.00m, MonthlyLimit = 500.00m, ExpiresAt = DateTime.UtcNow.AddYears(3), CreatedAt = DateTime.UtcNow.AddDays(-14) },
            new() { Id = Guid.NewGuid(), AccountId = Guid.Parse("dddd4444-4444-4444-4444-444444444444"), Last4 = "9004", Type = "prepaid", Status = "active", DailyLimit = 40.00m,  MonthlyLimit = 200.00m, ExpiresAt = DateTime.UtcNow.AddYears(3), CreatedAt = DateTime.UtcNow.AddMonths(-2).AddDays(1) },
            new() { Id = Guid.NewGuid(), AccountId = Guid.Parse("dddd4444-5555-5555-5555-555555555555"), Last4 = "9005", Type = "prepaid", Status = "active", DailyLimit = 25.00m,  MonthlyLimit = 100.00m, ExpiresAt = DateTime.UtcNow.AddYears(3), CreatedAt = DateTime.UtcNow.AddMonths(-1).AddDays(1) },
            new() { Id = Guid.NewGuid(), AccountId = Guid.Parse("dddd4444-6666-6666-6666-666666666666"), Last4 = "9006", Type = "prepaid", Status = "active", DailyLimit = null,    MonthlyLimit = null,    ExpiresAt = DateTime.UtcNow.AddYears(3), CreatedAt = DateTime.UtcNow.AddDays(-9) },
        };

        context.Cards.AddRange(juniorCards);

        // Pending approval transfers (from junior accounts, awaiting parent approval)
        var pendingApprovalTransfers = new List<Transfer>
        {
            new()
            {
                Id = Guid.Parse("aaaa9999-1111-1111-1111-111111111111"),
                FromAccountId = Guid.Parse("dddd4444-1111-1111-1111-111111111111"),
                ToAccountId = account2Checking.Id,
                Amount = 30.00m,
                Currency = "USD",
                Channel = "internal",
                Status = TransferStatus.PendingApproval,
                Description = "Pocket money",
                RequiresApproval = true,
                CreatedAt = DateTime.UtcNow.AddHours(-2)
            },
            new()
            {
                Id = Guid.Parse("aaaa9999-2222-2222-2222-222222222222"),
                FromAccountId = Guid.Parse("dddd4444-2222-2222-2222-222222222222"),
                ToAccountId = account1Savings.Id,
                Amount = 15.00m,
                Currency = "USD",
                Channel = "internal",
                Status = TransferStatus.PendingApproval,
                Description = "Savings contribution",
                RequiresApproval = true,
                CreatedAt = DateTime.UtcNow.AddHours(-1)
            }
        };

        context.Transfers.AddRange(pendingApprovalTransfers);

        await context.SaveChangesAsync();
    }

    private static string BCryptHash(string input)
    {
        return BCrypt.Net.BCrypt.HashPassword(input);
    }
}
