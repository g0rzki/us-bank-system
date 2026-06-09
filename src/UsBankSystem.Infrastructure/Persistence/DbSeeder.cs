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

        // Transfer GUIDs — stałe żeby transakcje mogły się do nich odwoływać
        var trMonthlySavings   = Guid.Parse("bbbb0001-0000-0000-0000-000000000001");
        var trRentSplit        = Guid.Parse("bbbb0001-0000-0000-0000-000000000002");
        var trFreelance        = Guid.Parse("bbbb0001-0000-0000-0000-000000000003");
        var trDinnerSplit      = Guid.Parse("bbbb0001-0000-0000-0000-000000000004");
        var trExternal         = Guid.Parse("bbbb0001-0000-0000-0000-000000000005");
        var trInvoiceFailed    = Guid.Parse("bbbb0001-0000-0000-0000-000000000006");
        var trRentPending      = Guid.Parse("bbbb0001-0000-0000-0000-000000000007");
        var trSwiftPending     = Guid.Parse("bbbb0001-0000-0000-0000-000000000008");
        var trRtpPending       = Guid.Parse("bbbb0001-0000-0000-0000-000000000009");
        var trElectricity      = Guid.Parse("bbbb0001-0000-0000-0000-000000000010");
        var trGrocery1         = Guid.Parse("bbbb0001-0000-0000-0000-000000000011");
        var trRestaurant       = Guid.Parse("bbbb0001-0000-0000-0000-000000000012");
        var trSubscription     = Guid.Parse("bbbb0001-0000-0000-0000-000000000013");
        var trPharmacy         = Guid.Parse("bbbb0001-0000-0000-0000-000000000014");
        var trSpotify          = Guid.Parse("bbbb0001-0000-0000-0000-000000000015");
        var trPayroll1         = Guid.Parse("bbbb0001-0000-0000-0000-000000000016");
        var trRent             = Guid.Parse("bbbb0001-0000-0000-0000-000000000017");
        var trGrocery2         = Guid.Parse("bbbb0001-0000-0000-0000-000000000018");
        var trNetflix          = Guid.Parse("bbbb0001-0000-0000-0000-000000000019");
        var trPayroll2         = Guid.Parse("bbbb0001-0000-0000-0000-000000000020");
        var trGasStation1      = Guid.Parse("bbbb0001-0000-0000-0000-000000000021");
        var trSavingsOld       = Guid.Parse("bbbb0001-0000-0000-0000-000000000022");
        var trSavingsContrib   = Guid.Parse("bbbb0001-0000-0000-0000-000000000023");
        var trPayroll3         = Guid.Parse("bbbb0001-0000-0000-0000-000000000024");
        var trGasStation2      = Guid.Parse("bbbb0001-0000-0000-0000-000000000025");

        // Transactions
        var transactions = new List<Transaction>
        {
            // account1Checking (john.doe)
            new() { Id = Guid.NewGuid(), AccountId = account1Checking.Id, Amount = 3500.00m, Type = "credit",  Status = "completed", Description = "Payroll deposit",        ReferenceId = trPayroll1.ToString(),      CreatedAt = DateTime.UtcNow.AddDays(-30) },
            new() { Id = Guid.NewGuid(), AccountId = account1Checking.Id, Amount = 1200.00m, Type = "debit",   Status = "completed", Description = "Rent",                   ReferenceId = trRent.ToString(),          CreatedAt = DateTime.UtcNow.AddDays(-29) },
            new() { Id = Guid.NewGuid(), AccountId = account1Checking.Id, Amount = 85.40m,   Type = "debit",   Status = "completed", Description = "Grocery store",          ReferenceId = trGrocery2.ToString(),      CreatedAt = DateTime.UtcNow.AddDays(-27) },
            new() { Id = Guid.NewGuid(), AccountId = account1Checking.Id, Amount = 9.99m,    Type = "debit",   Status = "completed", Description = "Netflix",                ReferenceId = trNetflix.ToString(),       CreatedAt = DateTime.UtcNow.AddDays(-25) },
            new() { Id = Guid.NewGuid(), AccountId = account1Checking.Id, Amount = 3500.00m, Type = "credit",  Status = "completed", Description = "Payroll deposit",        ReferenceId = trPayroll2.ToString(),      CreatedAt = DateTime.UtcNow.AddDays(-16) },
            new() { Id = Guid.NewGuid(), AccountId = account1Checking.Id, Amount = 62.30m,   Type = "debit",   Status = "completed", Description = "Gas station",            ReferenceId = trGasStation1.ToString(),   CreatedAt = DateTime.UtcNow.AddDays(-15) },
            new() { Id = Guid.NewGuid(), AccountId = account1Checking.Id, Amount = 340.00m,  Type = "debit",   Status = "completed", Description = "Electricity bill",       ReferenceId = trElectricity.ToString(),   CreatedAt = DateTime.UtcNow.AddDays(-14) },
            new() { Id = Guid.NewGuid(), AccountId = account1Checking.Id, Amount = 120.50m,  Type = "debit",   Status = "completed", Description = "Grocery store",          ReferenceId = trGrocery1.ToString(),      CreatedAt = DateTime.UtcNow.AddDays(-12) },
            new() { Id = Guid.NewGuid(), AccountId = account1Checking.Id, Amount = 500.00m,  Type = "debit",   Status = "completed", Description = "Monthly savings",        ReferenceId = trMonthlySavings.ToString(), CreatedAt = DateTime.UtcNow.AddDays(-12) },
            new() { Id = Guid.NewGuid(), AccountId = account1Checking.Id, Amount = 47.20m,   Type = "debit",   Status = "completed", Description = "Restaurant",             ReferenceId = trRestaurant.ToString(),    CreatedAt = DateTime.UtcNow.AddDays(-10) },
            new() { Id = Guid.NewGuid(), AccountId = account1Checking.Id, Amount = 200.00m,  Type = "debit",   Status = "completed", Description = "Rent split",             ReferenceId = trRentSplit.ToString(),     CreatedAt = DateTime.UtcNow.AddDays(-8) },
            new() { Id = Guid.NewGuid(), AccountId = account1Checking.Id, Amount = 89.99m,   Type = "debit",   Status = "completed", Description = "Online subscription",    ReferenceId = trSubscription.ToString(),  CreatedAt = DateTime.UtcNow.AddDays(-5) },
            new() { Id = Guid.NewGuid(), AccountId = account1Checking.Id, Amount = 350.00m,  Type = "debit",   Status = "completed", Description = "Freelance payment",      ReferenceId = trFreelance.ToString(),     CreatedAt = DateTime.UtcNow.AddDays(-6) },
            new() { Id = Guid.NewGuid(), AccountId = account1Checking.Id, Amount = 75.50m,   Type = "debit",   Status = "completed", Description = "Dinner split",           ReferenceId = trDinnerSplit.ToString(),   CreatedAt = DateTime.UtcNow.AddDays(-3) },
            new() { Id = Guid.NewGuid(), AccountId = account1Checking.Id, Amount = 120.00m,  Type = "debit",   Status = "failed",    Description = "Invoice payment",        ReferenceId = trInvoiceFailed.ToString(), CreatedAt = DateTime.UtcNow.AddDays(-2) },
            new() { Id = Guid.NewGuid(), AccountId = account1Checking.Id, Amount = 32.00m,   Type = "debit",   Status = "completed", Description = "Pharmacy",               ReferenceId = trPharmacy.ToString(),      CreatedAt = DateTime.UtcNow.AddDays(-2) },
            new() { Id = Guid.NewGuid(), AccountId = account1Checking.Id, Amount = 14.99m,   Type = "debit",   Status = "completed", Description = "Spotify",                ReferenceId = trSpotify.ToString(),       CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new() { Id = Guid.NewGuid(), AccountId = account1Checking.Id, Amount = 850.00m,  Type = "debit",   Status = "pending",   Description = "Rent payment",           ReferenceId = trRentPending.ToString(),   CreatedAt = DateTime.UtcNow.AddHours(-3) },
            new() { Id = Guid.NewGuid(), AccountId = account1Checking.Id, Amount = 2400.00m, Type = "debit",   Status = "pending",   Description = "International invoice",  ReferenceId = trSwiftPending.ToString(),  CreatedAt = DateTime.UtcNow.AddHours(-1) },
            new() { Id = Guid.NewGuid(), AccountId = account1Checking.Id, Amount = 45.00m,   Type = "debit",   Status = "pending",   Description = "Dinner split",           ReferenceId = trRtpPending.ToString(),    CreatedAt = DateTime.UtcNow.AddMinutes(-20) },

            // account1Savings (john.doe)
            new() { Id = Guid.NewGuid(), AccountId = account1Savings.Id,  Amount = 500.00m,  Type = "credit",  Status = "completed", Description = "Transfer from checking", ReferenceId = trMonthlySavings.ToString(), CreatedAt = DateTime.UtcNow.AddDays(-12) },
            new() { Id = Guid.NewGuid(), AccountId = account1Savings.Id,  Amount = 500.00m,  Type = "credit",  Status = "completed", Description = "Transfer from checking", ReferenceId = trSavingsOld.ToString(),     CreatedAt = DateTime.UtcNow.AddDays(-30) },
            new() { Id = Guid.NewGuid(), AccountId = account1Savings.Id,  Amount = 15.00m,   Type = "credit",  Status = "pending",   Description = "Savings contribution",   ReferenceId = trSavingsContrib.ToString(), CreatedAt = DateTime.UtcNow.AddHours(-1) },

            // account2Checking (jane.smith)
            new() { Id = Guid.NewGuid(), AccountId = account2Checking.Id, Amount = 2500.00m, Type = "credit",  Status = "completed", Description = "Payroll deposit",        ReferenceId = trPayroll3.ToString(),      CreatedAt = DateTime.UtcNow.AddDays(-7) },
            new() { Id = Guid.NewGuid(), AccountId = account2Checking.Id, Amount = 45.50m,   Type = "debit",   Status = "completed", Description = "Gas station",            ReferenceId = trGasStation2.ToString(),   CreatedAt = DateTime.UtcNow.AddDays(-3) },
            new() { Id = Guid.NewGuid(), AccountId = account2Checking.Id, Amount = 200.00m,  Type = "credit",  Status = "completed", Description = "Rent split received",    ReferenceId = trRentSplit.ToString(),     CreatedAt = DateTime.UtcNow.AddDays(-8) },
            new() { Id = Guid.NewGuid(), AccountId = account2Checking.Id, Amount = 350.00m,  Type = "credit",  Status = "completed", Description = "Freelance payment",      ReferenceId = trFreelance.ToString(),     CreatedAt = DateTime.UtcNow.AddDays(-6) },
            new() { Id = Guid.NewGuid(), AccountId = account2Checking.Id, Amount = 75.50m,   Type = "credit",  Status = "completed", Description = "Dinner split",           ReferenceId = trDinnerSplit.ToString(),   CreatedAt = DateTime.UtcNow.AddDays(-3) },
        };

        context.Transactions.AddRange(transactions);

        // Transfers
        var transfers = new List<Transfer>
        {
            new()
            {
                Id = trMonthlySavings,
                FromAccountId = account1Checking.Id,
                ToAccountId = account1Savings.Id,
                ToAccountNumber = account1Savings.AccountNumber,
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
                Id = trRentSplit,
                FromAccountId = account1Checking.Id,
                ToAccountId = account2Checking.Id,
                ToAccountNumber = account2Checking.AccountNumber,
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
                Id = trFreelance,
                FromAccountId = account1Checking.Id,
                ToAccountId = account2Checking.Id,
                ToAccountNumber = account2Checking.AccountNumber,
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
                Id = trDinnerSplit,
                FromAccountId = account1Checking.Id,
                ToAccountId = account2Checking.Id,
                ToAccountNumber = account2Checking.AccountNumber,
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
                Id = trExternal,
                FromAccountId = account2Checking.Id,
                ToAccountId = null,
                ToAccountNumber = "062100018-9876543210",
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
                Id = trInvoiceFailed,
                FromAccountId = account1Checking.Id,
                ToAccountId = account3Checking.Id,
                ToAccountNumber = account3Checking.AccountNumber,
                Amount = 120.00m,
                Currency = "USD",
                Channel = "ach",
                Status = "failed",
                ExternalReferenceId = "ACH-2024-001235",
                Description = "Invoice payment",
                CreatedAt = DateTime.UtcNow.AddDays(-2)
            },
            new()
            {
                Id = trRentPending,
                FromAccountId = account1Checking.Id,
                ToAccountId = null,
                ToAccountNumber = "021000021-1234567890",
                Amount = 850.00m,
                Currency = "USD",
                Channel = "ach",
                Status = TransferStatus.Pending,
                ExternalReferenceId = "ACH-2024-002001",
                Description = "Rent payment",
                RequiresApproval = false,
                CreatedAt = DateTime.UtcNow.AddHours(-3)
            },
            new()
            {
                Id = trSwiftPending,
                FromAccountId = account1Checking.Id,
                ToAccountId = null,
                ToAccountNumber = "DE89370400440532013000",
                Amount = 2400.00m,
                Currency = "USD",
                Channel = "swift",
                Status = TransferStatus.Pending,
                ExternalReferenceId = "SWIFT-2024-000088",
                Description = "International invoice",
                RequiresApproval = false,
                CreatedAt = DateTime.UtcNow.AddHours(-1)
            },
            new()
            {
                Id = trRtpPending,
                FromAccountId = account1Checking.Id,
                ToAccountId = account2Checking.Id,
                ToAccountNumber = account2Checking.AccountNumber,
                Amount = 45.00m,
                Currency = "USD",
                Channel = "rtp",
                Status = TransferStatus.Pending,
                ExternalReferenceId = "RTP-2024-009912",
                Description = "Dinner split",
                RequiresApproval = false,
                CreatedAt = DateTime.UtcNow.AddMinutes(-20)
            },
            new()
            {
                Id = trPayroll1,
                FromAccountId = account1Checking.Id,
                ToAccountId = null,
                ToAccountNumber = "PAYROLL-EMPLOYER-001",
                Amount = 3500.00m,
                Currency = "USD",
                Channel = "ach",
                Status = "completed",
                ExternalReferenceId = "ACH-PAY-001",
                Description = "Payroll deposit",
                CreatedAt = DateTime.UtcNow.AddDays(-30),
                CompletedAt = DateTime.UtcNow.AddDays(-30)
            },
            new()
            {
                Id = trRent,
                FromAccountId = account1Checking.Id,
                ToAccountId = null,
                ToAccountNumber = "LANDLORD-ACC-00987",
                Amount = 1200.00m,
                Currency = "USD",
                Channel = "ach",
                Status = "completed",
                ExternalReferenceId = "ACH-RENT-001",
                Description = "Rent",
                CreatedAt = DateTime.UtcNow.AddDays(-29),
                CompletedAt = DateTime.UtcNow.AddDays(-29)
            },
            new()
            {
                Id = trGrocery2,
                FromAccountId = account1Checking.Id,
                ToAccountId = null,
                ToAccountNumber = "CARD-POS-GROCERY-007",
                Amount = 85.40m,
                Currency = "USD",
                Channel = "internal",
                Status = "completed",
                Description = "Grocery store",
                CreatedAt = DateTime.UtcNow.AddDays(-27),
                CompletedAt = DateTime.UtcNow.AddDays(-27)
            },
            new()
            {
                Id = trNetflix,
                FromAccountId = account1Checking.Id,
                ToAccountId = null,
                ToAccountNumber = "CARD-POS-NETFLIX-008",
                Amount = 9.99m,
                Currency = "USD",
                Channel = "ach",
                Status = "completed",
                ExternalReferenceId = "ACH-2024-NFLX-001",
                Description = "Netflix",
                CreatedAt = DateTime.UtcNow.AddDays(-25),
                CompletedAt = DateTime.UtcNow.AddDays(-25)
            },
            new()
            {
                Id = trPayroll2,
                FromAccountId = account1Checking.Id,
                ToAccountId = null,
                ToAccountNumber = "PAYROLL-EMPLOYER-001",
                Amount = 3500.00m,
                Currency = "USD",
                Channel = "ach",
                Status = "completed",
                ExternalReferenceId = "ACH-PAY-002",
                Description = "Payroll deposit",
                CreatedAt = DateTime.UtcNow.AddDays(-16),
                CompletedAt = DateTime.UtcNow.AddDays(-16)
            },
            new()
            {
                Id = trGasStation1,
                FromAccountId = account1Checking.Id,
                ToAccountId = null,
                ToAccountNumber = "CARD-POS-GAS-009",
                Amount = 62.30m,
                Currency = "USD",
                Channel = "internal",
                Status = "completed",
                Description = "Gas station",
                CreatedAt = DateTime.UtcNow.AddDays(-15),
                CompletedAt = DateTime.UtcNow.AddDays(-15)
            },
            new()
            {
                Id = trSavingsOld,
                FromAccountId = account1Checking.Id,
                ToAccountId = account1Savings.Id,
                ToAccountNumber = account1Savings.AccountNumber,
                Amount = 500.00m,
                Currency = "USD",
                Channel = "internal",
                Status = "completed",
                Description = "Transfer from checking",
                CreatedAt = DateTime.UtcNow.AddDays(-30),
                CompletedAt = DateTime.UtcNow.AddDays(-30)
            },
            new()
            {
                Id = trSavingsContrib,
                FromAccountId = account1Checking.Id,
                ToAccountId = account1Savings.Id,
                ToAccountNumber = account1Savings.AccountNumber,
                Amount = 15.00m,
                Currency = "USD",
                Channel = "internal",
                Status = "pending",
                Description = "Savings contribution",
                CreatedAt = DateTime.UtcNow.AddHours(-1)
            },
            new()
            {
                Id = trPayroll3,
                FromAccountId = account2Checking.Id,
                ToAccountId = null,
                ToAccountNumber = "PAYROLL-EMPLOYER-002",
                Amount = 2500.00m,
                Currency = "USD",
                Channel = "ach",
                Status = "completed",
                ExternalReferenceId = "ACH-PAY-003",
                Description = "Payroll deposit",
                CreatedAt = DateTime.UtcNow.AddDays(-7),
                CompletedAt = DateTime.UtcNow.AddDays(-7)
            },
            new()
            {
                Id = trGasStation2,
                FromAccountId = account2Checking.Id,
                ToAccountId = null,
                ToAccountNumber = "CARD-POS-GAS-010",
                Amount = 45.50m,
                Currency = "USD",
                Channel = "internal",
                Status = "completed",
                Description = "Gas station",
                CreatedAt = DateTime.UtcNow.AddDays(-3),
                CompletedAt = DateTime.UtcNow.AddDays(-3)
            },
            new()
            {
                Id = trElectricity,
                FromAccountId = account1Checking.Id,
                ToAccountId = null,
                ToAccountNumber = "UTIL-ELECTRIC-00123",
                Amount = 340.00m,
                Currency = "USD",
                Channel = "ach",
                Status = "completed",
                ExternalReferenceId = "ACH-2024-ELEC-001",
                Description = "Electricity bill",
                CreatedAt = DateTime.UtcNow.AddDays(-14),
                CompletedAt = DateTime.UtcNow.AddDays(-14)
            },
            new()
            {
                Id = trGrocery1,
                FromAccountId = account1Checking.Id,
                ToAccountId = null,
                ToAccountNumber = "CARD-POS-GROCERY-002",
                Amount = 120.50m,
                Currency = "USD",
                Channel = "internal",
                Status = "completed",
                Description = "Grocery store",
                CreatedAt = DateTime.UtcNow.AddDays(-12),
                CompletedAt = DateTime.UtcNow.AddDays(-12)
            },
            new()
            {
                Id = trRestaurant,
                FromAccountId = account1Checking.Id,
                ToAccountId = null,
                ToAccountNumber = "CARD-POS-RESTAURANT-003",
                Amount = 47.20m,
                Currency = "USD",
                Channel = "internal",
                Status = "completed",
                Description = "Restaurant",
                CreatedAt = DateTime.UtcNow.AddDays(-10),
                CompletedAt = DateTime.UtcNow.AddDays(-10)
            },
            new()
            {
                Id = trSubscription,
                FromAccountId = account1Checking.Id,
                ToAccountId = null,
                ToAccountNumber = "CARD-POS-SUBSCRIPTION-004",
                Amount = 89.99m,
                Currency = "USD",
                Channel = "ach",
                Status = "completed",
                ExternalReferenceId = "ACH-2024-SUB-001",
                Description = "Online subscription",
                CreatedAt = DateTime.UtcNow.AddDays(-5),
                CompletedAt = DateTime.UtcNow.AddDays(-5)
            },
            new()
            {
                Id = trPharmacy,
                FromAccountId = account1Checking.Id,
                ToAccountId = null,
                ToAccountNumber = "CARD-POS-PHARMACY-005",
                Amount = 32.00m,
                Currency = "USD",
                Channel = "internal",
                Status = "completed",
                Description = "Pharmacy",
                CreatedAt = DateTime.UtcNow.AddDays(-2),
                CompletedAt = DateTime.UtcNow.AddDays(-2)
            },
            new()
            {
                Id = trSpotify,
                FromAccountId = account1Checking.Id,
                ToAccountId = null,
                ToAccountNumber = "CARD-POS-SPOTIFY-006",
                Amount = 14.99m,
                Currency = "USD",
                Channel = "ach",
                Status = "completed",
                ExternalReferenceId = "ACH-2024-SPOT-001",
                Description = "Spotify",
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                CompletedAt = DateTime.UtcNow.AddDays(-1)
            }
        };

        context.Transfers.AddRange(transfers);

        // Cards
        var cards = new List<Card>
        {
            new()
            {
                Id = new Guid("cccc0001-0000-0000-0000-000000000001"),
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
                Id = new Guid("cccc0002-0000-0000-0000-000000000002"),
                AccountId = account1Savings.Id,
                Last4 = "7741",
                Type = "debit",
                Status = "active",
                ExternalCardToken = "tok_visa_debit_002",
                ExpiresAt = DateTime.UtcNow.AddYears(4),
                CreatedAt = account1Savings.CreatedAt.AddDays(2)
            },
            new()
            {
                Id = new Guid("cccc0003-0000-0000-0000-000000000003"),
                AccountId = account2Checking.Id,
                Last4 = "8901",
                Type = "prepaid",
                Status = "active",
                ExternalCardToken = "tok_visa_prepaid_001",
                ExpiresAt = DateTime.UtcNow.AddYears(2),
                CreatedAt = account2Checking.CreatedAt.AddDays(1)
            },
            new()
            {
                Id = new Guid("cccc0004-0000-0000-0000-000000000004"),
                AccountId = account1Checking.Id,
                Last4 = "1234",
                Type = "prepaid",
                Status = "blocked",
                BlockedAt = DateTime.UtcNow.AddHours(-25),
                ExternalCardToken = "tok_mc_prepaid_001",
                ExpiresAt = DateTime.UtcNow.AddYears(1),
                CreatedAt = account1Checking.CreatedAt.AddDays(3)
            },
            new()
            {
                Id = new Guid("cccc0005-0000-0000-0000-000000000005"),
                AccountId = account1Checking.Id,
                Last4 = "9999",
                Type = "prepaid",
                Status = "expired",
                ExternalCardToken = "tok_mc_prepaid_old",
                ExpiresAt = DateTime.UtcNow.AddMonths(-3),
                CreatedAt = account1Checking.CreatedAt.AddMonths(-24)
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
                UserId = user1.Id,
                Code = "123456",
                Status = "active",
                ExpiresAt = DateTime.UtcNow.AddMinutes(2),
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                Id = Guid.NewGuid(),
                AccountId = account2Checking.Id,
                UserId = user2.Id,
                Code = "654321",
                Status = "used",
                ExpiresAt = DateTime.UtcNow.AddMinutes(-5),
                CreatedAt = DateTime.UtcNow.AddMinutes(-7)
            }
        };

        context.BlikCodes.AddRange(blikCodes);

        // Junior users
        var juniorUser1 = new User { Id = Guid.Parse("ffff1111-1111-1111-1111-111111111111"), Email = "emma.doe@example.com",     PasswordHash = BCryptHash("Test123!"), FirstName = "Emma",   LastName = "Doe",   Status = "active", CreatedAt = DateTime.UtcNow.AddMonths(-2) };
        var juniorUser2 = new User { Id = Guid.Parse("ffff2222-2222-2222-2222-222222222222"), Email = "liam.doe@example.com",     PasswordHash = BCryptHash("Test123!"), FirstName = "Liam",   LastName = "Doe",   Status = "active", CreatedAt = DateTime.UtcNow.AddMonths(-1) };
        var juniorUser3 = new User { Id = Guid.Parse("ffff3333-3333-3333-3333-333333333333"), Email = "sophie.doe@example.com",   PasswordHash = BCryptHash("Test123!"), FirstName = "Sophie", LastName = "Doe",   Status = "active", CreatedAt = DateTime.UtcNow.AddDays(-15) };
        var juniorUser4 = new User { Id = Guid.Parse("ffff4444-4444-4444-4444-444444444444"), Email = "oliver.smith@example.com", PasswordHash = BCryptHash("Test123!"), FirstName = "Oliver", LastName = "Smith", Status = "active", CreatedAt = DateTime.UtcNow.AddMonths(-2) };
        var juniorUser5 = new User { Id = Guid.Parse("ffff5555-5555-5555-5555-555555555555"), Email = "mia.smith@example.com",    PasswordHash = BCryptHash("Test123!"), FirstName = "Mia",    LastName = "Smith", Status = "active", CreatedAt = DateTime.UtcNow.AddMonths(-1) };
        var juniorUser6 = new User { Id = Guid.Parse("ffff6666-6666-6666-6666-666666666666"), Email = "noah.smith@example.com",   PasswordHash = BCryptHash("Test123!"), FirstName = "Noah",   LastName = "Smith", Status = "active", CreatedAt = DateTime.UtcNow.AddDays(-10) };

        context.Users.AddRange(juniorUser1, juniorUser2, juniorUser3, juniorUser4, juniorUser5, juniorUser6);

        // Junior accounts — john.doe ma 3, jane.smith ma 3, bob.wilson ma 0
        var juniorAccounts = new List<Account>
        {
            new() { Id = Guid.Parse("dddd4444-1111-1111-1111-111111111111"), UserId = juniorUser1.Id, AccountNumber = "4000000001", Type = "checking", Balance = 250.00m, ReservedBalance = 30.00m, Currency = "USD", Status = "active", CreatedAt = DateTime.UtcNow.AddMonths(-2) },
            new() { Id = Guid.Parse("dddd4444-2222-2222-2222-222222222222"), UserId = juniorUser2.Id, AccountNumber = "4000000002", Type = "checking", Balance = 80.50m,  ReservedBalance = 0m,     Currency = "USD", Status = "active", CreatedAt = DateTime.UtcNow.AddMonths(-1) },
            new() { Id = Guid.Parse("dddd4444-3333-3333-3333-333333333333"), UserId = juniorUser3.Id, AccountNumber = "4000000003", Type = "checking", Balance = 510.00m, ReservedBalance = 0m,     Currency = "USD", Status = "active", CreatedAt = DateTime.UtcNow.AddDays(-15) },
            new() { Id = Guid.Parse("dddd4444-4444-4444-4444-444444444444"), UserId = juniorUser4.Id, AccountNumber = "4000000004", Type = "checking", Balance = 120.00m, ReservedBalance = 0m,     Currency = "USD", Status = "active", CreatedAt = DateTime.UtcNow.AddMonths(-2) },
            new() { Id = Guid.Parse("dddd4444-5555-5555-5555-555555555555"), UserId = juniorUser5.Id, AccountNumber = "4000000005", Type = "checking", Balance = 340.75m, ReservedBalance = 0m,     Currency = "USD", Status = "active", CreatedAt = DateTime.UtcNow.AddMonths(-1) },
            new() { Id = Guid.Parse("dddd4444-6666-6666-6666-666666666666"), UserId = juniorUser6.Id, AccountNumber = "4000000006", Type = "checking", Balance = 60.00m,  ReservedBalance = 0m,     Currency = "USD", Status = "active", CreatedAt = DateTime.UtcNow.AddDays(-10) },
        };

        context.Accounts.AddRange(juniorAccounts);

        var juniorLinks = new List<JuniorAccount>
        {
            new() { Id = Guid.Parse("eeee5555-1111-1111-1111-111111111111"), AccountId = Guid.Parse("dddd4444-1111-1111-1111-111111111111"), ParentUserId = user1.Id, DateOfBirth = new DateOnly(2015, 6, 15),  CreatedAt = DateTime.UtcNow.AddMonths(-2) },
            new() { Id = Guid.Parse("eeee5555-2222-2222-2222-222222222222"), AccountId = Guid.Parse("dddd4444-2222-2222-2222-222222222222"), ParentUserId = user1.Id, DateOfBirth = new DateOnly(2013, 3, 22),  CreatedAt = DateTime.UtcNow.AddMonths(-1) },
            new() { Id = Guid.Parse("eeee5555-3333-3333-3333-333333333333"), AccountId = Guid.Parse("dddd4444-3333-3333-3333-333333333333"), ParentUserId = user1.Id, DateOfBirth = new DateOnly(2016, 11, 5),  CreatedAt = DateTime.UtcNow.AddDays(-15) },
            new() { Id = Guid.Parse("eeee5555-4444-4444-4444-444444444444"), AccountId = Guid.Parse("dddd4444-4444-4444-4444-444444444444"), ParentUserId = user2.Id, DateOfBirth = new DateOnly(2014, 8, 30),  CreatedAt = DateTime.UtcNow.AddMonths(-2) },
            new() { Id = Guid.Parse("eeee5555-5555-5555-5555-555555555555"), AccountId = Guid.Parse("dddd4444-5555-5555-5555-555555555555"), ParentUserId = user2.Id, DateOfBirth = new DateOnly(2017, 1, 12),  CreatedAt = DateTime.UtcNow.AddMonths(-1) },
            new() { Id = Guid.Parse("eeee5555-6666-6666-6666-666666666666"), AccountId = Guid.Parse("dddd4444-6666-6666-6666-666666666666"), ParentUserId = user2.Id, DateOfBirth = new DateOnly(2014, 5, 19),  CreatedAt = DateTime.UtcNow.AddDays(-10) },
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

        // Junior transactions
        var juniorTransactions = new List<Transaction>
        {
            // dddd4444-1111 (balance 250, reserved 30)
            new() { Id = Guid.NewGuid(), AccountId = Guid.Parse("dddd4444-1111-1111-1111-111111111111"), Amount = 100.00m, Type = "credit", Status = "completed", Description = "Allowance from parent", CreatedAt = DateTime.UtcNow.AddDays(-55) },
            new() { Id = Guid.NewGuid(), AccountId = Guid.Parse("dddd4444-1111-1111-1111-111111111111"), Amount = 25.00m,  Type = "debit",  Status = "completed", Description = "Toy store",            CreatedAt = DateTime.UtcNow.AddDays(-40) },
            new() { Id = Guid.NewGuid(), AccountId = Guid.Parse("dddd4444-1111-1111-1111-111111111111"), Amount = 200.00m, Type = "credit", Status = "completed", Description = "Birthday gift",         CreatedAt = DateTime.UtcNow.AddDays(-30) },
            new() { Id = Guid.NewGuid(), AccountId = Guid.Parse("dddd4444-1111-1111-1111-111111111111"), Amount = 15.00m,  Type = "debit",  Status = "completed", Description = "Ice cream",             CreatedAt = DateTime.UtcNow.AddDays(-20) },
            new() { Id = Guid.NewGuid(), AccountId = Guid.Parse("dddd4444-1111-1111-1111-111111111111"), Amount = 30.00m,  Type = "debit",  Status = "pending_approval", Description = "Pocket money",   CreatedAt = DateTime.UtcNow.AddHours(-2) },

            // dddd4444-2222 (balance 80.50)
            new() { Id = Guid.NewGuid(), AccountId = Guid.Parse("dddd4444-2222-2222-2222-222222222222"), Amount = 50.00m,  Type = "credit", Status = "completed", Description = "Allowance from parent", CreatedAt = DateTime.UtcNow.AddDays(-25) },
            new() { Id = Guid.NewGuid(), AccountId = Guid.Parse("dddd4444-2222-2222-2222-222222222222"), Amount = 12.50m,  Type = "debit",  Status = "completed", Description = "School supplies",       CreatedAt = DateTime.UtcNow.AddDays(-15) },
            new() { Id = Guid.NewGuid(), AccountId = Guid.Parse("dddd4444-2222-2222-2222-222222222222"), Amount = 58.00m,  Type = "credit", Status = "completed", Description = "Chores reward",         CreatedAt = DateTime.UtcNow.AddDays(-10) },
            new() { Id = Guid.NewGuid(), AccountId = Guid.Parse("dddd4444-2222-2222-2222-222222222222"), Amount = 15.00m,  Type = "debit",  Status = "pending_approval", Description = "Savings contribution", CreatedAt = DateTime.UtcNow.AddHours(-1) },

            // dddd4444-3333 (balance 510)
            new() { Id = Guid.NewGuid(), AccountId = Guid.Parse("dddd4444-3333-3333-3333-333333333333"), Amount = 500.00m, Type = "credit", Status = "completed", Description = "Graduation gift",       CreatedAt = DateTime.UtcNow.AddDays(-14) },
            new() { Id = Guid.NewGuid(), AccountId = Guid.Parse("dddd4444-3333-3333-3333-333333333333"), Amount = 10.00m,  Type = "debit",  Status = "completed", Description = "Snacks",                CreatedAt = DateTime.UtcNow.AddDays(-7) },
            new() { Id = Guid.NewGuid(), AccountId = Guid.Parse("dddd4444-3333-3333-3333-333333333333"), Amount = 20.00m,  Type = "credit", Status = "completed", Description = "Allowance from parent", CreatedAt = DateTime.UtcNow.AddDays(-3) },

            // dddd4444-4444 (balance 120)
            new() { Id = Guid.NewGuid(), AccountId = Guid.Parse("dddd4444-4444-4444-4444-444444444444"), Amount = 80.00m,  Type = "credit", Status = "completed", Description = "Allowance from parent", CreatedAt = DateTime.UtcNow.AddDays(-50) },
            new() { Id = Guid.NewGuid(), AccountId = Guid.Parse("dddd4444-4444-4444-4444-444444444444"), Amount = 20.00m,  Type = "debit",  Status = "completed", Description = "Book store",            CreatedAt = DateTime.UtcNow.AddDays(-35) },
            new() { Id = Guid.NewGuid(), AccountId = Guid.Parse("dddd4444-4444-4444-4444-444444444444"), Amount = 60.00m,  Type = "credit", Status = "completed", Description = "Christmas gift",        CreatedAt = DateTime.UtcNow.AddDays(-20) },

            // dddd4444-5555 (balance 340.75)
            new() { Id = Guid.NewGuid(), AccountId = Guid.Parse("dddd4444-5555-5555-5555-555555555555"), Amount = 150.00m, Type = "credit", Status = "completed", Description = "Allowance from parent", CreatedAt = DateTime.UtcNow.AddDays(-28) },
            new() { Id = Guid.NewGuid(), AccountId = Guid.Parse("dddd4444-5555-5555-5555-555555555555"), Amount = 9.25m,   Type = "debit",  Status = "completed", Description = "Cinema ticket",         CreatedAt = DateTime.UtcNow.AddDays(-18) },
            new() { Id = Guid.NewGuid(), AccountId = Guid.Parse("dddd4444-5555-5555-5555-555555555555"), Amount = 200.00m, Type = "credit", Status = "completed", Description = "Birthday money",        CreatedAt = DateTime.UtcNow.AddDays(-10) },

            // dddd4444-6666 (balance 60)
            new() { Id = Guid.NewGuid(), AccountId = Guid.Parse("dddd4444-6666-6666-6666-666666666666"), Amount = 60.00m,  Type = "credit", Status = "completed", Description = "Allowance from parent", CreatedAt = DateTime.UtcNow.AddDays(-9) },
        };

        context.Transactions.AddRange(juniorTransactions);

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
