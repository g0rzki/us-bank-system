using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UsBankSystem.Api.Integrations;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Services;
using UsBankSystem.Core.Domain.Blik;
using UsBankSystem.Core.Entities;
using UsBankSystem.Infrastructure.Persistence;

namespace UsBankSystem.Tests.Blik;

public class BlikServiceTests
{
    private AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private BlikService CreateService(AppDbContext db, FakeKlikApiClient? fake = null) =>
        new(db, fake ?? new FakeKlikApiClient(), NullLogger<BlikService>.Instance);

    private async Task<(AppDbContext db, Guid userId, Guid accountId)> SetupUserWithAccount(
        decimal balance = 500m)
    {
        var db = CreateDb();
        var user = new User
        {
            Id = Guid.NewGuid(), Email = "test@example.com", PasswordHash = "hash",
            FirstName = "Test", LastName = "User", Status = "active", CreatedAt = DateTime.UtcNow
        };
        var account = new Account
        {
            Id = Guid.NewGuid(), UserId = user.Id, AccountNumber = "1000000001",
            Type = "checking", Balance = balance, ReservedBalance = 0,
            Currency = "USD", Status = "active", CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return (db, user.Id, account.Id);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 1: GenerateCode
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateCode_SavesBlikCodeToDb()
    {
        var (db, userId, accountId) = await SetupUserWithAccount();
        var fake = new FakeKlikApiClient();
        var svc = CreateService(db, fake);

        var result = await svc.GenerateCodeAsync(userId, accountId);

        Assert.Equal("123456", result.Code);
        Assert.Equal(1, await db.BlikCodes.CountAsync());

        var code = await db.BlikCodes.FirstAsync();
        Assert.Equal(userId, code.UserId);
        Assert.Equal(accountId, code.AccountId);
        Assert.Equal("123456", code.Code);
        Assert.Equal(BlikCodeStatus.Active, code.Status);
    }

    [Fact]
    public async Task GenerateCode_ExpiresOldActiveCodes()
    {
        var (db, userId, accountId) = await SetupUserWithAccount();
        // Pre-existing active code
        db.BlikCodes.Add(new BlikCode
        {
            Id = Guid.NewGuid(), UserId = userId, AccountId = accountId,
            Code = "111111", Status = BlikCodeStatus.Active,
            ExpiresAt = DateTime.UtcNow.AddSeconds(60), CreatedAt = DateTime.UtcNow.AddSeconds(-30)
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db, new FakeKlikApiClient());
        await svc.GenerateCodeAsync(userId, accountId);

        var codes = await db.BlikCodes.ToListAsync();
        Assert.Equal(2, codes.Count);
        // Old code should be expired, new code active
        Assert.Contains(codes, c => c.Code == "111111" && c.Status == BlikCodeStatus.Expired);
        Assert.Contains(codes, c => c.Code == "123456" && c.Status == BlikCodeStatus.Active);
    }

    [Fact]
    public async Task GenerateCode_JuniorAccount_ThrowsUnauthorized()
    {
        var (db, userId, accountId) = await SetupUserWithAccount();
        // Make this user a "junior" by creating JuniorAccount pointing to their account
        db.JuniorAccounts.Add(new JuniorAccount
        {
            Id = Guid.NewGuid(), AccountId = accountId, ParentUserId = userId
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            svc.GenerateCodeAsync(userId, accountId));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 2: WebhookAuthorize — creates pending authorization
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WebhookAuthorize_CreatesPendingAuthorization()
    {
        var (db, userId, accountId) = await SetupUserWithAccount();
        // Active BlikCode so the webhook can resolve the account
        db.BlikCodes.Add(new BlikCode
        {
            Id = Guid.NewGuid(), UserId = userId, AccountId = accountId,
            Code = "123456", Status = BlikCodeStatus.Active,
            ExpiresAt = DateTime.UtcNow.AddSeconds(90), CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var txId = Guid.NewGuid().ToString();
        var payload = new KlikWebhookAuthorizeRequest
        {
            TransactionId = txId,
            UserId = userId.ToString(),
            Amount = 50m,
            Currency = "USD",
            MerchantName = "Coffee Corner",
            IsOnUs = false,
            ExpiryTime = DateTime.UtcNow.AddSeconds(60),
            Zone = "US"
        };

        var svc = CreateService(db);
        await svc.HandleAuthorizeWebhookAsync(payload);

        Assert.Equal(1, await db.BlikAuthorizations.CountAsync());
        var auth = await db.BlikAuthorizations.FirstAsync();
        Assert.Equal(txId, auth.KlikTransactionId);
        Assert.Equal(userId, auth.UserId);
        Assert.Equal(BlikAuthorizationStatus.Pending, auth.Status);
        Assert.Equal(50m, auth.Amount);
        Assert.Equal("Coffee Corner", auth.MerchantName);

        // BlikCode should be marked as used
        var code = await db.BlikCodes.FirstAsync();
        Assert.Equal(BlikCodeStatus.Used, code.Status);
    }

    [Fact]
    public async Task WebhookAuthorize_Idempotent_SecondCallIgnored()
    {
        var (db, userId, accountId) = await SetupUserWithAccount();
        db.BlikCodes.Add(new BlikCode
        {
            Id = Guid.NewGuid(), UserId = userId, AccountId = accountId,
            Code = "123456", Status = BlikCodeStatus.Active,
            ExpiresAt = DateTime.UtcNow.AddSeconds(90), CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var txId = Guid.NewGuid().ToString();
        var payload = new KlikWebhookAuthorizeRequest
        {
            TransactionId = txId, UserId = userId.ToString(), Amount = 25m,
            Currency = "USD", MerchantName = "Shop", IsOnUs = false,
            ExpiryTime = DateTime.UtcNow.AddSeconds(60), Zone = "US"
        };

        var svc = CreateService(db);
        await svc.HandleAuthorizeWebhookAsync(payload);
        await svc.HandleAuthorizeWebhookAsync(payload); // duplicate

        Assert.Equal(1, await db.BlikAuthorizations.CountAsync());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 3: Approve happy path
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Approve_HappyPath_DebitsAccountAndConfirmsAccepted()
    {
        var (db, userId, accountId) = await SetupUserWithAccount(balance: 500m);
        var txId = Guid.NewGuid().ToString();
        var authId = Guid.NewGuid();
        db.BlikAuthorizations.Add(new BlikAuthorization
        {
            Id = authId, KlikTransactionId = txId, UserId = userId, AccountId = accountId,
            Amount = 150m, Currency = "USD", MerchantName = "Store X",
            IsOnUs = false, Status = BlikAuthorizationStatus.Pending,
            ExpiryTime = DateTime.UtcNow.AddSeconds(60), CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var fake = new FakeKlikApiClient();
        var svc = CreateService(db, fake);
        var result = await svc.DecideAsync(userId, authId, accepted: true);

        // Authorization accepted
        Assert.Equal(BlikAuthorizationStatus.Accepted, result.Status);
        Assert.NotNull(result.LocalTransactionId);

        // Transaction created with correct debit
        var tx = await db.Transactions.FirstAsync();
        Assert.Equal(accountId, tx.AccountId);
        Assert.Equal(150m, tx.Amount);
        Assert.Equal("debit", tx.Type);
        Assert.Equal("completed", tx.Status);

        // Account balance reduced
        var account = await db.Accounts.FindAsync(accountId);
        Assert.Equal(350m, account!.Balance);

        // KLIK confirm called with ACCEPTED
        Assert.Equal("ACCEPTED", fake.LastConfirmStatus);
        Assert.Null(fake.LastRejectReason);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 4: Approve with insufficient funds
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Approve_InsufficientFunds_NoTransactionKlikRejectedInsufficientFunds()
    {
        var (db, userId, accountId) = await SetupUserWithAccount(balance: 10m);
        var txId = Guid.NewGuid().ToString();
        var authId = Guid.NewGuid();
        db.BlikAuthorizations.Add(new BlikAuthorization
        {
            Id = authId, KlikTransactionId = txId, UserId = userId, AccountId = accountId,
            Amount = 500m, Currency = "USD", MerchantName = "Big Store",
            IsOnUs = false, Status = BlikAuthorizationStatus.Pending,
            ExpiryTime = DateTime.UtcNow.AddSeconds(60), CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var fake = new FakeKlikApiClient();
        var svc = CreateService(db, fake);
        var result = await svc.DecideAsync(userId, authId, accepted: true);

        // No transaction created
        Assert.Equal(0, await db.Transactions.CountAsync());

        // Authorization rejected
        Assert.Equal(BlikAuthorizationStatus.Rejected, result.Status);
        Assert.Null(result.LocalTransactionId);

        // Balance unchanged
        var account = await db.Accounts.FindAsync(accountId);
        Assert.Equal(10m, account!.Balance);

        // KLIK confirm called with REJECTED + INSUFFICIENT_FUNDS
        Assert.Equal("REJECTED", fake.LastConfirmStatus);
        Assert.Equal("INSUFFICIENT_FUNDS", fake.LastRejectReason);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 5: Reject — USER_DECLINED
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reject_NoTransactionKlikRejectedUserDeclined()
    {
        var (db, userId, accountId) = await SetupUserWithAccount(balance: 500m);
        var txId = Guid.NewGuid().ToString();
        var authId = Guid.NewGuid();
        db.BlikAuthorizations.Add(new BlikAuthorization
        {
            Id = authId, KlikTransactionId = txId, UserId = userId, AccountId = accountId,
            Amount = 100m, Currency = "USD", MerchantName = "Cafe",
            IsOnUs = false, Status = BlikAuthorizationStatus.Pending,
            ExpiryTime = DateTime.UtcNow.AddSeconds(60), CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var fake = new FakeKlikApiClient();
        var svc = CreateService(db, fake);
        var result = await svc.DecideAsync(userId, authId, accepted: false);

        Assert.Equal(BlikAuthorizationStatus.Rejected, result.Status);
        Assert.Equal(0, await db.Transactions.CountAsync());

        // Balance unchanged
        var account = await db.Accounts.FindAsync(accountId);
        Assert.Equal(500m, account!.Balance);

        Assert.Equal("REJECTED", fake.LastConfirmStatus);
        Assert.Equal("USER_DECLINED", fake.LastRejectReason);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test 6: Decide after expiry — throws and sets Timeout
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DecideAfterExpiry_ThrowsInvalidOperationAndSetsTimeout()
    {
        var (db, userId, accountId) = await SetupUserWithAccount();
        var txId = Guid.NewGuid().ToString();
        var authId = Guid.NewGuid();
        db.BlikAuthorizations.Add(new BlikAuthorization
        {
            Id = authId, KlikTransactionId = txId, UserId = userId, AccountId = accountId,
            Amount = 50m, Currency = "USD", MerchantName = "Shop",
            IsOnUs = false, Status = BlikAuthorizationStatus.Pending,
            ExpiryTime = DateTime.UtcNow.AddSeconds(-1),  // already expired
            CreatedAt = DateTime.UtcNow.AddSeconds(-90)
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.DecideAsync(userId, authId, accepted: true));

        var auth = await db.BlikAuthorizations.FindAsync(authId);
        Assert.Equal(BlikAuthorizationStatus.Timeout, auth!.Status);
        Assert.NotNull(auth.DecidedAt);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Fake KLIK client for tests
    // ──────────────────────────────────────────────────────────────────────────

    private class FakeKlikApiClient : IKlikApiClient
    {
        public string? LastConfirmStatus { get; private set; }
        public string? LastRejectReason { get; private set; }

        public Task<KlikGenerateCodeResult> GenerateCodeAsync(string userId, CancellationToken ct = default)
            => Task.FromResult(new KlikGenerateCodeResult(true, "123456", DateTime.UtcNow.AddSeconds(120), null));

        public Task<KlikConfirmResult> ConfirmPaymentAsync(string transactionId, bool accepted, string? rejectReason, CancellationToken ct = default)
        {
            LastConfirmStatus = accepted ? "ACCEPTED" : "REJECTED";
            LastRejectReason = rejectReason;
            return Task.FromResult(new KlikConfirmResult(true, null));
        }
    }
}
