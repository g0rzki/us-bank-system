using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UsBankSystem.Api.Configuration;
using UsBankSystem.Api.Integrations.FedNow;
using UsBankSystem.Api.Integrations.Rtp;
using UsBankSystem.Api.Services;
using UsBankSystem.Core.Domain.Transfers;
using UsBankSystem.Core.Entities;
using UsBankSystem.Infrastructure.Persistence;
using UsBankSystem.Tests.Helpers;
using Transfer = UsBankSystem.Core.Entities.Transfer;

namespace UsBankSystem.Tests.Transfers;

public class ProcessWebhookRtpTests
{
    private AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private IOptions<PaymentSessionConfig> CreatePaymentConfig() =>
        Options.Create(new PaymentSessionConfig
        {
            Ach = new AchConfig { BatchWindowMinutes = 1, CutoffHour = 23 },
            Rtp = new RtpConfig { TimeoutSeconds = 10 },
            FedNow = new FedNowConfig { TimeoutSeconds = 10, PollIntervalSeconds = 1, BankRtn = "040104018", BankLegalName = "Baguette Bank" }
        });

    private TransferService CreateTransferService(AppDbContext db)
    {
        var mqGateway = new FedNowMqGateway(
            new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, """{"status":"sent"}"""))
                { BaseAddress = new Uri("http://localhost:8770") },
            NullLogger<FedNowMqGateway>.Instance);
        var rtpTchGateway = new RtpTchGateway(
            new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "<xml/>"))
                { BaseAddress = new Uri("http://localhost:8200") },
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Rtp:ApiKey"] = "test" }).Build(),
            NullLogger<RtpTchGateway>.Instance);
        return new TransferService(db, mqGateway, rtpTchGateway, new Pacs008Builder(), CreatePaymentConfig(), NullLogger<TransferService>.Instance);
    }

    private async Task<(AppDbContext db, Guid accountId, Guid transferId)> Setup()
    {
        var db = CreateDb();

        var user = new User { Id = Guid.NewGuid(), Email = "test@example.com", PasswordHash = "hash", FirstName = "Jan", LastName = "Kowalski", Status = "active", CreatedAt = DateTime.UtcNow };
        db.Users.Add(user);

        var account = new Account { Id = Guid.NewGuid(), UserId = user.Id, AccountNumber = "1000000001", Type = "checking", Balance = 1000m, ReservedBalance = 100m, Currency = "USD", Status = "active", CreatedAt = DateTime.UtcNow };
        db.Accounts.Add(account);

        var transfer = new Transfer
        {
            Id = Guid.NewGuid(),
            FromAccountId = account.Id,
            ToAccountId = null,
            ToAccountNumber = "999888777666",
            ToRoutingNumber = "010101012",
            RecipientName = "Miku",
            Amount = 100m,
            Currency = "USD",
            Channel = TransferChannel.Rtp,
            Status = TransferStatus.Pending,
            ExternalReferenceId = $"E2E-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow
        };
        db.Transfers.Add(transfer);

        await db.SaveChangesAsync();
        return (db, account.Id, transfer.Id);
    }

    [Fact]
    public async Task ProcessWebhook_Completed_DeductsBalanceAndCreatesTransaction()
    {
        var (db, accountId, transferId) = await Setup();
        var service = CreateTransferService(db);

        await service.ProcessWebhookAsync(transferId, TransferStatus.Completed, "REF-001");

        var account = await db.Accounts.FindAsync(accountId);
        Assert.Equal(900m, account!.Balance);
        Assert.Equal(0m, account.ReservedBalance);

        var transfer = await db.Transfers.FindAsync(transferId);
        Assert.Equal(TransferStatus.Completed, transfer!.Status);
        Assert.NotNull(transfer.CompletedAt);

        Assert.Equal(1, await db.Transactions.CountAsync());
    }

    [Fact]
    public async Task ProcessWebhook_Rejected_ReleasesReserveNoBalanceChange()
    {
        var (db, accountId, transferId) = await Setup();
        var service = CreateTransferService(db);

        await service.ProcessWebhookAsync(transferId, TransferStatus.Rejected, null);

        var account = await db.Accounts.FindAsync(accountId);
        Assert.Equal(1000m, account!.Balance);
        Assert.Equal(0m, account.ReservedBalance);

        var transfer = await db.Transfers.FindAsync(transferId);
        Assert.Equal(TransferStatus.Rejected, transfer!.Status);
        Assert.NotNull(transfer.RejectedAt);

        Assert.Equal(0, await db.Transactions.CountAsync());
    }
}
