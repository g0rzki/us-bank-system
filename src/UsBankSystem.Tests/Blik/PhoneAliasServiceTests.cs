using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using UsBankSystem.Api.Configuration;
using UsBankSystem.Api.Integrations;
using UsBankSystem.Api.Integrations.FedNow;
using UsBankSystem.Api.Services;
using UsBankSystem.Api.Services.Payments;
using UsBankSystem.Core.Domain.Blik;
using UsBankSystem.Core.Domain.Transfers;
using UsBankSystem.Core.Entities;
using UsBankSystem.Infrastructure.Persistence;
using UsBankSystem.Tests.Helpers;

namespace UsBankSystem.Tests.Blik;

public class PhoneAliasServiceTests
{
    private const string OwnRoutingNumber = "021000021";
    private const string OtherRoutingNumber = "999999999";

    // ── DB & service factory ─────────────────────────────────────────────────

    private AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private IConfiguration CreateConfig(string routingNumber = OwnRoutingNumber) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bank:RoutingNumber"] = routingNumber
            })
            .Build();

    private static IOptions<PaymentSessionConfig> CreatePaymentConfig() =>
        Options.Create(new PaymentSessionConfig
        {
            FedNow = new FedNowConfig { TimeoutSeconds = 10, PollIntervalSeconds = 1, BankRtn = "040104018", BankLegalName = "Baguette Bank" }
        });

    private static FedNowMqGateway CreateMqGateway(HttpStatusCode status = HttpStatusCode.OK) =>
        new(new HttpClient(new MockHttpMessageHandler(status,
                status == HttpStatusCode.OK ? """{"status":"sent"}""" : "{}"))
            { BaseAddress = new Uri("http://localhost:8770") },
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FedNowMqGateway>.Instance);

    private PhoneAliasService CreateService(
        AppDbContext db,
        FakeKlikP2pClient? klik = null,
        FedNowMqGateway? mqGateway = null) =>
        new(db,
            klik ?? new FakeKlikP2pClient(),
            new InternalPaymentService(db),
            mqGateway ?? CreateMqGateway(),
            new Pacs008Builder(),
            CreatePaymentConfig(),
            CreateConfig());

    // ── Data helpers ─────────────────────────────────────────────────────────

    private async Task<(AppDbContext db, Guid userId, Guid accountId)> SetupUserWithAccount(
        decimal balance = 500m, string accountNumber = "1000000001")
    {
        var db = CreateDb();
        var user = new User
        {
            Id = Guid.NewGuid(), Email = "test@example.com", PasswordHash = "hash",
            FirstName = "Test", LastName = "User", Status = "active", CreatedAt = DateTime.UtcNow
        };
        var account = new Account
        {
            Id = Guid.NewGuid(), UserId = user.Id, AccountNumber = accountNumber,
            Type = "checking", Balance = balance, ReservedBalance = 0,
            Currency = "USD", Status = "active", CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return (db, user.Id, account.Id);
    }

    // ── Test 1: RegisterAlias happy path ─────────────────────────────────────

    [Fact]
    public async Task RegisterAlias_HappyPath_PersistsAlias()
    {
        var (db, userId, accountId) = await SetupUserWithAccount();
        var fake = new FakeKlikP2pClient();
        var svc = CreateService(db, fake);

        var result = await svc.RegisterAliasAsync(userId, accountId, "+15551234567");

        Assert.Equal(1, await db.PhoneAliases.CountAsync());
        var alias = await db.PhoneAliases.FirstAsync();
        Assert.Equal("+15551234567", alias.Phone);
        Assert.Equal(accountId, alias.AccountId);
        Assert.Equal("alias-001", alias.KlikAliasId);
        Assert.Equal(PhoneAliasStatus.Active, alias.Status);

        Assert.Equal("alias-001", result.KlikAliasId);
        Assert.Equal("+15551234567", result.Phone);
    }

    // ── Test 2: RegisterAlias duplicate throws 409 ───────────────────────────

    [Fact]
    public async Task RegisterAlias_Duplicate_ThrowsConflict()
    {
        var (db, userId, accountId) = await SetupUserWithAccount();
        db.PhoneAliases.Add(new PhoneAlias
        {
            Id = Guid.NewGuid(), AccountId = accountId, Phone = "+15551234567",
            KlikAliasId = "old-alias", Status = PhoneAliasStatus.Active,
            RegisteredAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var fake = new FakeKlikP2pClient();
        var svc = CreateService(db, fake);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RegisterAliasAsync(userId, accountId, "+15559876543"));

        Assert.False(fake.RegisterCalled);
    }

    // ── Test 3: RegisterAlias junior → 401 ──────────────────────────────────

    [Fact]
    public async Task RegisterAlias_JuniorAccount_ThrowsUnauthorized()
    {
        var (db, userId, accountId) = await SetupUserWithAccount();
        db.JuniorAccounts.Add(new JuniorAccount
        {
            Id = Guid.NewGuid(), AccountId = accountId, ParentUserId = Guid.NewGuid(),
            DateOfBirth = new DateOnly(2015, 1, 1), CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            svc.RegisterAliasAsync(userId, accountId, "+15551234567"));
    }

    // ── Test 4: DeleteAlias happy path → soft-delete ─────────────────────────

    [Fact]
    public async Task DeleteAlias_HappyPath_SoftDeletesRecord()
    {
        var (db, userId, accountId) = await SetupUserWithAccount();
        db.PhoneAliases.Add(new PhoneAlias
        {
            Id = Guid.NewGuid(), AccountId = accountId, Phone = "+15551234567",
            KlikAliasId = "alias-001", Status = PhoneAliasStatus.Active,
            RegisteredAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var fake = new FakeKlikP2pClient();
        var svc = CreateService(db, fake);

        await svc.DeleteAliasAsync(userId, accountId);

        var alias = await db.PhoneAliases.FirstAsync();
        Assert.Equal(PhoneAliasStatus.Deleted, alias.Status);
        Assert.Equal("+15551234567", fake.LastDeletedPhone);
    }

    // ── Test 5: DeleteAlias other bank → 401 ─────────────────────────────────

    [Fact]
    public async Task DeleteAlias_OtherBank_ThrowsUnauthorized()
    {
        var (db, userId, accountId) = await SetupUserWithAccount();
        db.PhoneAliases.Add(new PhoneAlias
        {
            Id = Guid.NewGuid(), AccountId = accountId, Phone = "+15551234567",
            KlikAliasId = "alias-other-bank", Status = PhoneAliasStatus.Active,
            RegisteredAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var fake = new FakeKlikP2pClient(deleteThrows: new UnauthorizedAccessException("403_INSUFFICIENT_PERMISSIONS: Cannot delete alias of another bank"));
        var svc = CreateService(db, fake);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            svc.DeleteAliasAsync(userId, accountId));
    }

    // ── Test 6: SendToPhone lookup 200 → on-us → InternalPaymentService ──────

    [Fact]
    public async Task SendToPhone_OnUs_InternalTransferAndBalanceDebited()
    {
        var (db, userId, fromAccountId) = await SetupUserWithAccount(balance: 500m, accountNumber: "1000000001");
        var receiver = new Account
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), AccountNumber = "2000000002",
            Type = "checking", Balance = 0m, ReservedBalance = 0,
            Currency = "USD", Status = "active", CreatedAt = DateTime.UtcNow
        };
        db.Accounts.Add(receiver);
        await db.SaveChangesAsync();

        var fake = new FakeKlikP2pClient(lookupRoutingNumber: OwnRoutingNumber, lookupAccountNumber: "2000000002");
        var svc = CreateService(db, fake);

        var result = await svc.SendToPhoneAsync(userId, fromAccountId, "+15559999999", 100m, "USD", null);

        var sender = await db.Accounts.FindAsync(fromAccountId);
        var receiverReloaded = await db.Accounts.FindAsync(receiver.Id);

        Assert.Equal(400m, sender!.Balance);
        Assert.Equal(100m, receiverReloaded!.Balance);
        Assert.Equal(TransferChannel.Internal, result.Channel);
        Assert.Equal("completed", result.Status);
    }

    // ── Test 7: SendToPhone lookup 200 → off-us → FedNow ────────────────────

    [Fact]
    public async Task SendToPhone_External_FedNowTransferAndBalanceDebited()
    {
        var (db, userId, fromAccountId) = await SetupUserWithAccount(balance: 500m);
        var fake = new FakeKlikP2pClient(lookupRoutingNumber: OtherRoutingNumber, lookupAccountNumber: "EXT-ACCT-9999");
        var svc = CreateService(db, fake, CreateMqGateway(HttpStatusCode.OK));

        var result = await svc.SendToPhoneAsync(userId, fromAccountId, "+15550000001", 150m, "USD", null);

        var sender = await db.Accounts.FindAsync(fromAccountId);
        Assert.Equal(500m, sender!.Balance);
        Assert.Equal(150m, sender.ReservedBalance);

        var transfer = await db.Transfers.FirstAsync();
        Assert.Equal(TransferChannel.FedNow, transfer.Channel);
        Assert.Equal("pending", transfer.Status);
        Assert.StartsWith("MSG-", transfer.ExternalReferenceId);
        Assert.Equal(0, await db.Transactions.CountAsync());
    }

    // ── Test 8: SendToPhone lookup 404 → error, balance unchanged ────────────

    [Fact]
    public async Task SendToPhone_Lookup404_ThrowsAndNoBalanceChange()
    {
        var (db, userId, fromAccountId) = await SetupUserWithAccount(balance: 500m);
        var fake = new FakeKlikP2pClient(lookupThrows: new KeyNotFoundException("404_ALIAS_NOT_FOUND: not found"));
        var svc = CreateService(db, fake);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            svc.SendToPhoneAsync(userId, fromAccountId, "+15551111111", 100m, "USD", null));

        var sender = await db.Accounts.FindAsync(fromAccountId);
        Assert.Equal(500m, sender!.Balance);
        Assert.Equal(0, await db.Transfers.CountAsync());
    }

    // ── Test 9: SendToPhone insufficient funds → error before lookup ──────────

    [Fact]
    public async Task SendToPhone_InsufficientFunds_ThrowsBeforeLookup()
    {
        var (db, userId, fromAccountId) = await SetupUserWithAccount(balance: 10m);
        var fake = new FakeKlikP2pClient();
        var svc = CreateService(db, fake);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.SendToPhoneAsync(userId, fromAccountId, "+15552222222", 500m, "USD", null));

        Assert.False(fake.LookupCalled);
    }

    // ── Fake client ──────────────────────────────────────────────────────────

    private class FakeKlikP2pClient(
        string lookupRoutingNumber = OwnRoutingNumber,
        string lookupAccountNumber = "9999999999",
        Exception? lookupThrows = null,
        Exception? deleteThrows = null) : IKlikP2pClient
    {
        public bool RegisterCalled { get; private set; }
        public bool LookupCalled  { get; private set; }
        public string? LastDeletedPhone { get; private set; }

        public Task<KlikP2pRegisterResult> RegisterAliasAsync(string phone, string routingNumber, string accountNumber, CancellationToken ct = default)
        {
            RegisterCalled = true;
            return Task.FromResult(new KlikP2pRegisterResult("alias-001", DateTime.UtcNow));
        }

        public Task<KlikP2pLookupResult> LookupAliasAsync(string phone, CancellationToken ct = default)
        {
            LookupCalled = true;
            if (lookupThrows is not null) throw lookupThrows;
            return Task.FromResult(new KlikP2pLookupResult("bank-xyz", lookupRoutingNumber, lookupAccountNumber));
        }

        public Task DeleteAliasAsync(string phone, CancellationToken ct = default)
        {
            LastDeletedPhone = phone;
            if (deleteThrows is not null) throw deleteThrows;
            return Task.CompletedTask;
        }
    }
}
