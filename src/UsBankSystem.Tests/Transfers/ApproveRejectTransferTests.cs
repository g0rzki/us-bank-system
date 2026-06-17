using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UsBankSystem.Api.Configuration;
using UsBankSystem.Api.Controllers;
using UsBankSystem.Api.Integrations;
using UsBankSystem.Api.Integrations.FedNow;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Api.Services;
using UsBankSystem.Api.Services.Payments;
using UsBankSystem.Core.Domain.Transfers;
using UsBankSystem.Core.Entities;
using UsBankSystem.Infrastructure.Persistence;
using UsBankSystem.Tests.Helpers;
using Transfer = UsBankSystem.Core.Entities.Transfer;

namespace UsBankSystem.Tests.Transfers;

public class ApproveRejectTransferTests
{
    private AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private IConfiguration CreateConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "test_secret_minimum_32_characters_required!",
                ["Webhook:Secret"] = "secret"
            })
            .Build();

    private IOptions<PaymentSessionConfig> CreatePaymentConfig() =>
        Options.Create(new PaymentSessionConfig
        {
            Ach = new AchConfig { BatchWindowMinutes = 1, CutoffHour = 23 },
            Rtp = new TimeoutConfig { TimeoutSeconds = 10 },
            FedNow = new FedNowConfig { TimeoutSeconds = 10, PollIntervalSeconds = 1, BankRtn = "040104018", BankLegalName = "Baguette Bank" },
            Swift = new SwiftConfig { TimeoutSeconds = 10 }
        });

    private TransfersController CreateController(AppDbContext db, Guid userId)
    {
        var achGateway = new AchGateway(
            new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, """{"referenceId":"REF-001"}"""))
                { BaseAddress = new Uri("http://localhost:6001") },
            NullLogger<AchGateway>.Instance);
        var rtpGateway = new RtpGateway(
            new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "{}"))
                { BaseAddress = new Uri("http://localhost:6002") },
            NullLogger<RtpGateway>.Instance);
        var mqGateway = new FedNowMqGateway(
            new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, """{"status":"sent"}"""))
                { BaseAddress = new Uri("http://localhost:8770") },
            NullLogger<FedNowMqGateway>.Instance);
        var swiftGateway = new SwiftGateway(
            new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "{}"))
                { BaseAddress = new Uri("http://localhost:6004") },
            NullLogger<SwiftGateway>.Instance);

        var internalPayment = new InternalPaymentService(db);
        var achPayment = new AchPaymentService(db, achGateway, CreatePaymentConfig());
        var rtpPayment = new RtpPaymentService(db, rtpGateway, CreatePaymentConfig());
        var fedNowPayment = new FedNowPaymentService(db, mqGateway, new Pacs008Builder(), CreatePaymentConfig());
        var swiftPayment = new SwiftPaymentService(db, swiftGateway, CreatePaymentConfig());
        var transferService = new TransferService(db);
        var controller = new TransfersController(transferService, internalPayment, achPayment, rtpPayment, fedNowPayment, swiftPayment, CreateConfig());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString())
                }))
            }
        };
        return controller;
    }

    private async Task<(AppDbContext db, Guid parentUserId, Guid parentAccountId, Guid juniorAccountId, Guid toAccountId, Guid transferId)> Setup()
    {
        var db = CreateDb();

        var parentUser = new User { Id = Guid.NewGuid(), Email = "parent@example.com", PasswordHash = "hash", FirstName = "Parent", LastName = "User", Status = "active", CreatedAt = DateTime.UtcNow };
        var otherUser = new User { Id = Guid.NewGuid(), Email = "other@example.com", PasswordHash = "hash", FirstName = "Other", LastName = "User", Status = "active", CreatedAt = DateTime.UtcNow };
        db.Users.AddRange(parentUser, otherUser);

        var parentAccount = new Account { Id = Guid.NewGuid(), UserId = parentUser.Id, AccountNumber = "1000000001", Type = "checking", Balance = 1000m, ReservedBalance = 0m, Currency = "USD", Status = "active", CreatedAt = DateTime.UtcNow };
        var juniorAccount = new Account { Id = Guid.NewGuid(), UserId = parentUser.Id, AccountNumber = "2000000001", Type = "checking", Balance = 100m, ReservedBalance = 30m, Currency = "USD", Status = "active", CreatedAt = DateTime.UtcNow };
        var toAccount = new Account { Id = Guid.NewGuid(), UserId = otherUser.Id, AccountNumber = "3000000001", Type = "checking", Balance = 500m, ReservedBalance = 0m, Currency = "USD", Status = "active", CreatedAt = DateTime.UtcNow };
        db.Accounts.AddRange(parentAccount, juniorAccount, toAccount);

        db.JuniorAccounts.Add(new JuniorAccount { Id = Guid.NewGuid(), AccountId = juniorAccount.Id, ParentUserId = parentUser.Id, DateOfBirth = new DateOnly(2015, 6, 15), CreatedAt = DateTime.UtcNow });

        var transfer = new Transfer { Id = Guid.NewGuid(), FromAccountId = juniorAccount.Id, ToAccountId = toAccount.Id, Amount = 30m, Currency = "USD", Channel = TransferChannel.Internal, Status = TransferStatus.PendingApproval, RequiresApproval = true, CreatedAt = DateTime.UtcNow };
        db.Transfers.Add(transfer);

        await db.SaveChangesAsync();
        return (db, parentUser.Id, parentAccount.Id, juniorAccount.Id, toAccount.Id, transfer.Id);
    }

    [Fact]
    public async Task Approve_ValidRequest_Returns200WithCompletedStatus()
    {
        var (db, parentUserId, _, _, _, transferId) = await Setup();
        var controller = CreateController(db, parentUserId);

        var result = await controller.Approve(transferId);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<TransferResponse>(ok.Value);
        Assert.Equal(TransferStatus.Completed, response.Status);
    }

    [Fact]
    public async Task Approve_UpdatesBalances()
    {
        var (db, parentUserId, _, juniorAccountId, toAccountId, transferId) = await Setup();
        var controller = CreateController(db, parentUserId);

        await controller.Approve(transferId);

        var junior = await db.Accounts.FindAsync(juniorAccountId);
        var to = await db.Accounts.FindAsync(toAccountId);
        Assert.Equal(70m, junior!.Balance);
        Assert.Equal(0m, junior.ReservedBalance);
        Assert.Equal(530m, to!.Balance);
    }

    [Fact]
    public async Task Approve_CreatesTransactions()
    {
        var (db, parentUserId, _, _, _, transferId) = await Setup();
        var controller = CreateController(db, parentUserId);

        await controller.Approve(transferId);

        Assert.Equal(2, await db.Transactions.CountAsync());
    }

    [Fact]
    public async Task Approve_SetsApprovedByAndApprovedAt()
    {
        var (db, parentUserId, _, _, _, transferId) = await Setup();
        var controller = CreateController(db, parentUserId);

        await controller.Approve(transferId);

        var transfer = await db.Transfers.FindAsync(transferId);
        Assert.Equal(parentUserId, transfer!.ApprovedBy);
        Assert.NotNull(transfer.ApprovedAt);
    }

    [Fact]
    public async Task Approve_OtherUser_ThrowsUnauthorized()
    {
        var (db, _, _, _, _, transferId) = await Setup();
        var controller = CreateController(db, Guid.NewGuid());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controller.Approve(transferId));
    }

    [Fact]
    public async Task Approve_AlreadyCompleted_ThrowsInvalidOperation()
    {
        var (db, parentUserId, _, _, _, transferId) = await Setup();
        var controller = CreateController(db, parentUserId);
        await controller.Approve(transferId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.Approve(transferId));
    }

    [Fact]
    public async Task Approve_NotFound_ThrowsKeyNotFound()
    {
        var (db, parentUserId, _, _, _, _) = await Setup();
        var controller = CreateController(db, parentUserId);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => controller.Approve(Guid.NewGuid()));
    }

    [Fact]
    public async Task Reject_ValidRequest_Returns200WithRejectedStatus()
    {
        var (db, parentUserId, _, _, _, transferId) = await Setup();
        var controller = CreateController(db, parentUserId);

        var result = await controller.Reject(transferId);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<TransferResponse>(ok.Value);
        Assert.Equal(TransferStatus.Rejected, response.Status);
    }

    [Fact]
    public async Task Reject_ReleasesReservedBalance()
    {
        var (db, parentUserId, _, juniorAccountId, _, transferId) = await Setup();
        var controller = CreateController(db, parentUserId);

        await controller.Reject(transferId);

        var junior = await db.Accounts.FindAsync(juniorAccountId);
        Assert.Equal(0m, junior!.ReservedBalance);
        Assert.Equal(100m, junior.Balance);
    }

    [Fact]
    public async Task Reject_SetsRejectedAt()
    {
        var (db, parentUserId, _, _, _, transferId) = await Setup();
        var controller = CreateController(db, parentUserId);

        await controller.Reject(transferId);

        var transfer = await db.Transfers.FindAsync(transferId);
        Assert.NotNull(transfer!.RejectedAt);
    }

    [Fact]
    public async Task Reject_OtherUser_ThrowsUnauthorized()
    {
        var (db, _, _, _, _, transferId) = await Setup();
        var controller = CreateController(db, Guid.NewGuid());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controller.Reject(transferId));
    }

    [Fact]
    public async Task Reject_AlreadyRejected_ThrowsInvalidOperation()
    {
        var (db, parentUserId, _, _, _, transferId) = await Setup();
        var controller = CreateController(db, parentUserId);
        await controller.Reject(transferId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.Reject(transferId));
    }
}


