using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UsBankSystem.Api.Configuration;
using UsBankSystem.Api.Controllers;
using UsBankSystem.Api.Integrations;
using UsBankSystem.Api.Integrations.FedNow;
using UsBankSystem.Api.Integrations.Rtp;
using UsBankSystem.Api.Models.Auth;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Api.Services;
using UsBankSystem.Api.Services.Payments;
using UsBankSystem.Core.Domain.Transactions;
using UsBankSystem.Core.Domain.Transfers;
using UsBankSystem.Infrastructure.Persistence;
using UsBankSystem.Tests.Helpers;

namespace UsBankSystem.Tests.Transfers;

public class TransferStatusTests
{
    private const string WebhookSecret = "test_webhook_secret";

    private AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private IConfiguration CreateConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "test_secret_minimum_32_characters_required!",
                ["Webhook:Secret"] = WebhookSecret
            })
            .Build();

    private IOptions<PaymentSessionConfig> CreatePaymentConfig() =>
        Options.Create(new PaymentSessionConfig
        {
            Ach = new AchConfig { BatchWindowMinutes = 1, CutoffHour = 23 },
            Rtp = new RtpConfig { TimeoutSeconds = 10 },
            FedNow = new FedNowConfig { TimeoutSeconds = 10, PollIntervalSeconds = 1, BankRtn = "040104018", BankLegalName = "Baguette Bank" }
        });

    private TransfersController CreateController(AppDbContext db, Guid userId)
    {
        var achGateway = AchTestHelpers.CreateGateway();
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
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new SwiftOptions()),
            NullLogger<SwiftGateway>.Instance);

        var rtpTchGateway = new RtpTchGateway(
            new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "<xml/>"))
                { BaseAddress = new Uri("http://localhost:8200") },
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Rtp:ApiKey"] = "test" }).Build(),
            NullLogger<RtpTchGateway>.Instance);

        var internalPayment = new InternalPaymentService(db);
        var achPayment = new AchPaymentService(db, achGateway, CreatePaymentConfig());
        var rtpPayment = new RtpPaymentService(db, rtpGateway, rtpTchGateway, new Pacs008Builder(), CreatePaymentConfig());
        var fedNowPayment = new FedNowPaymentService(db, mqGateway, new Pacs008Builder(), CreatePaymentConfig());
        var swiftPayment = new SwiftPaymentService(db, swiftGateway, CreatePaymentConfig());
        var transferService = new TransferService(db, mqGateway, rtpTchGateway, new Pacs008Builder(), CreatePaymentConfig());
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

    private async Task<(AppDbContext db, Guid userId, Guid fromAccountId, Guid toAccountId)> Setup()
    {
        var db = CreateDb();
        var authService = new AuthService(db, CreateConfig());
        await authService.RegisterAsync(new RegisterRequest
        {
            Email = "test@example.com",
            Password = "Password123!",
            FirstName = "Jan",
            LastName = "Kowalski"
        });
        var user = await db.Users.FirstAsync();
        var accountService = new AccountService(db);
        var accountController = new AccountsController(accountService, new TransactionService(db), new JuniorService(db));
        accountController.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
                }))
            }
        };
        await accountController.Create(new CreateAccountRequest { Type = "checking" });
        await accountController.Create(new CreateAccountRequest { Type = "savings" });
        var accounts = await db.Accounts.ToListAsync();
        accounts[0].Balance = 1000m;
        await db.SaveChangesAsync();
        return (db, user.Id, accounts[0].Id, accounts[1].Id);
    }

    private static CreateAchTransferRequest AchRequest(Guid fromAccountId) => new()
    {
        FromAccountId = fromAccountId,
        ToRoutingNumber = "021000021",
        ToAccountNumber = "9876543210",
        RecipientName = "Test Recipient",
        Amount = 100m
    };

    [Fact]
    public async Task GetStatus_ValidTransfer_Returns200WithStatus()
    {
        var (db, userId, fromAccountId, toAccountId) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateAch(AchRequest(fromAccountId));
        var transfer = await db.Transfers.FirstAsync();

        var result = await controller.GetStatus(transfer.Id);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<TransferStatusResponse>(ok.Value);
        Assert.Equal(transfer.Id, response.TransferId);
        Assert.Equal(TransferStatus.Pending, response.Status);
        Assert.Equal(TransferChannel.Ach, response.Channel);
    }

    [Fact]
    public async Task GetStatus_OtherUsersTransfer_Throws()
    {
        var (db, userId, fromAccountId, _) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateAch(AchRequest(fromAccountId));
        var transfer = await db.Transfers.FirstAsync();

        // IDOR fix: "unauthorized" returns same 404 as "not found" to prevent ownership enumeration
        var otherController = CreateController(db, Guid.NewGuid());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => otherController.GetStatus(transfer.Id));
    }

    [Fact]
    public async Task GetStatus_NotFound_Throws()
    {
        var (db, userId, _, _) = await Setup();
        var controller = CreateController(db, userId);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => controller.GetStatus(Guid.NewGuid()));
    }

    [Fact]
    public async Task Webhook_Completed_UpdatesBalanceAndStatus()
    {
        var (db, userId, fromAccountId, _) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateAch(AchRequest(fromAccountId));
        var transfer = await db.Transfers.FirstAsync();

        controller.ControllerContext.HttpContext.Request.Headers["X-Webhook-Secret"] = WebhookSecret;
        var result = await controller.Webhook(transfer.Id, new WebhookRequest
        {
            Status = TransferStatus.Completed,
            ReferenceId = "ACH-SETTLED-001"
        });

        Assert.IsType<OkObjectResult>(result);
        var from = await db.Accounts.FindAsync(fromAccountId);
        Assert.Equal(900m, from!.Balance);
        Assert.Equal(0m, from.ReservedBalance);
        var updated = await db.Transfers.FindAsync(transfer.Id);
        Assert.Equal(TransferStatus.Completed, updated!.Status);
        Assert.Equal("ACH-SETTLED-001", updated.ExternalReferenceId);
        var tx = await db.Transactions.FirstAsync(t => t.ReferenceId == transfer.Id.ToString());
        Assert.Equal(TransactionStatus.Completed, tx.Status);
    }

    [Fact]
    public async Task Webhook_Failed_ReleasesReservation()
    {
        var (db, userId, fromAccountId, _) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateAch(AchRequest(fromAccountId));
        var transfer = await db.Transfers.FirstAsync();

        controller.ControllerContext.HttpContext.Request.Headers["X-Webhook-Secret"] = WebhookSecret;
        var result = await controller.Webhook(transfer.Id, new WebhookRequest
        {
            Status = TransferStatus.Failed
        });

        Assert.IsType<OkObjectResult>(result);
        var from = await db.Accounts.FindAsync(fromAccountId);
        Assert.Equal(0m, from!.ReservedBalance);
        Assert.Equal(1000m, from.Balance);
        var updated = await db.Transfers.FindAsync(transfer.Id);
        Assert.Equal(TransferStatus.Failed, updated!.Status);
    }

    [Fact]
    public async Task Webhook_InvalidSecret_Returns401()
    {
        var (db, userId, fromAccountId, _) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateAch(AchRequest(fromAccountId));
        var transfer = await db.Transfers.FirstAsync();

        controller.ControllerContext.HttpContext.Request.Headers["X-Webhook-Secret"] = "wrong_secret";
        var result = await controller.Webhook(transfer.Id, new WebhookRequest
        {
            Status = TransferStatus.Completed
        });

        Assert.IsType<UnauthorizedObjectResult>(result);
    }
}


