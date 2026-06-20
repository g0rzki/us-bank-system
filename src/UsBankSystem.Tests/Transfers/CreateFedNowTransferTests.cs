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
using UsBankSystem.Api.Integrations.Rtp;
using UsBankSystem.Api.Models.Auth;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Services;
using UsBankSystem.Api.Services.Payments;
using UsBankSystem.Core.Domain.Transfers;
using UsBankSystem.Infrastructure.Persistence;
using UsBankSystem.Tests.Helpers;

namespace UsBankSystem.Tests.Transfers;

public class CreateFedNowTransferTests
{
    private AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private IConfiguration CreateConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "test_secret_minimum_32_characters_required!"
            })
            .Build();

    private IOptions<PaymentSessionConfig> CreatePaymentConfig() =>
        Options.Create(new PaymentSessionConfig
        {
            Ach = new AchConfig { BatchWindowMinutes = 1, CutoffHour = 23 },
            Rtp = new RtpConfig { TimeoutSeconds = 10 },
            FedNow = new FedNowConfig
            {
                TimeoutSeconds = 10,
                PollIntervalSeconds = 1,
                BankRtn = "040104018",
                BankLegalName = "Baguette Bank"
            }
        });

    private static AchGateway CreateAchGateway() =>
        AchTestHelpers.CreateGateway();

    private static RtpGateway CreateRtpGateway() =>
        new(new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "{}"))
            { BaseAddress = new Uri("http://localhost:6002") },
            NullLogger<RtpGateway>.Instance);

    private static FedNowMqGateway CreateMqGateway(HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(new HttpClient(new MockHttpMessageHandler(statusCode, """{"status":"sent"}"""))
            { BaseAddress = new Uri("http://localhost:8770") },
            NullLogger<FedNowMqGateway>.Instance);

    private static SwiftGateway CreateSwiftGateway() =>
        new(new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "{}"))
            { BaseAddress = new Uri("http://localhost:6004") },
            NullLogger<SwiftGateway>.Instance);

    private static RtpTchGateway CreateRtpTchGateway() =>
        new(new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "<xml/>"))
            { BaseAddress = new Uri("http://localhost:8200") },
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Rtp:ApiKey"] = "test" }).Build(),
        NullLogger<RtpTchGateway>.Instance);

    private TransfersController CreateController(AppDbContext db, Guid userId, HttpStatusCode mqStatus = HttpStatusCode.OK)
    {
        var rtpTchGateway = CreateRtpTchGateway();
        var internalPayment = new InternalPaymentService(db);
        var achPayment = new AchPaymentService(db, CreateAchGateway(), CreatePaymentConfig());
        var rtpPayment = new RtpPaymentService(db, CreateRtpGateway(), rtpTchGateway, new Pacs008Builder(), CreatePaymentConfig());
        var fedNowPayment = new FedNowPaymentService(db, CreateMqGateway(mqStatus), new Pacs008Builder(), CreatePaymentConfig());
        var swiftPayment = new SwiftPaymentService(db, CreateSwiftGateway(), CreatePaymentConfig());
        var transferService = new TransferService(db, CreateMqGateway(mqStatus), rtpTchGateway, new Pacs008Builder(), CreatePaymentConfig());
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

    private async Task<(AppDbContext db, Guid userId, Guid fromAccountId)> Setup()
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
        var accounts = await db.Accounts.ToListAsync();
        accounts[0].Balance = 1000m;
        await db.SaveChangesAsync();
        return (db, user.Id, accounts[0].Id);
    }

    [Fact]
    public async Task CreateFedNow_ValidRequest_Returns201()
    {
        var (db, userId, fromAccountId) = await Setup();
        var controller = CreateController(db, userId);
        var result = await controller.CreateFedNow(new CreateFedNowTransferRequest
        {
            FromAccountId = fromAccountId,
            ToAccountNumber = "999888777666",
            ToRoutingNumber = "010101012",
            Amount = 100m,
            RecipientName = "Miku"
        });
        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
    }

    [Fact]
    public async Task CreateFedNow_StatusPending_NotCompleted()
    {
        var (db, userId, fromAccountId) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateFedNow(new CreateFedNowTransferRequest
        {
            FromAccountId = fromAccountId,
            ToAccountNumber = "999888777666",
            ToRoutingNumber = "010101012",
            Amount = 100m
        });
        var transfer = await db.Transfers.FirstAsync();
        Assert.Equal(TransferStatus.Pending, transfer.Status);
    }

    [Fact]
    public async Task CreateFedNow_BalanceReservedNotDeducted()
    {
        var (db, userId, fromAccountId) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateFedNow(new CreateFedNowTransferRequest
        {
            FromAccountId = fromAccountId,
            ToAccountNumber = "999888777666",
            ToRoutingNumber = "010101012",
            Amount = 100m
        });
        var fromAccount = await db.Accounts.FindAsync(fromAccountId);
        Assert.Equal(1000m, fromAccount!.Balance);
        Assert.Equal(100m, fromAccount.ReservedBalance);
    }

    [Fact]
    public async Task CreateFedNow_NoTransactionsCreatedYet()
    {
        var (db, userId, fromAccountId) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateFedNow(new CreateFedNowTransferRequest
        {
            FromAccountId = fromAccountId,
            ToAccountNumber = "999888777666",
            ToRoutingNumber = "010101012",
            Amount = 100m
        });
        Assert.Equal(0, await db.Transactions.CountAsync());
    }

    [Fact]
    public async Task CreateFedNow_InsufficientFunds_Throws()
    {
        var (db, userId, fromAccountId) = await Setup();
        var controller = CreateController(db, userId);
        await Assert.ThrowsAsync<ArgumentException>(() => controller.CreateFedNow(new CreateFedNowTransferRequest
        {
            FromAccountId = fromAccountId,
            ToAccountNumber = "999888777666",
            ToRoutingNumber = "010101012",
            Amount = 9999m
        }));
    }

    [Fact]
    public async Task CreateFedNow_GatewayFailure_ThrowsAndReleasesReservation()
    {
        var (db, userId, fromAccountId) = await Setup();
        var controller = CreateController(db, userId, HttpStatusCode.BadRequest);
        await Assert.ThrowsAsync<ArgumentException>(() => controller.CreateFedNow(new CreateFedNowTransferRequest
        {
            FromAccountId = fromAccountId,
            ToAccountNumber = "999888777666",
            ToRoutingNumber = "010101012",
            Amount = 100m
        }));
        var account = await db.Accounts.FindAsync(fromAccountId);
        Assert.Equal(0m, account!.ReservedBalance);
    }

    [Fact]
    public async Task CreateFedNow_ExternalReferenceIdSetToMsgId()
    {
        var (db, userId, fromAccountId) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateFedNow(new CreateFedNowTransferRequest
        {
            FromAccountId = fromAccountId,
            ToAccountNumber = "999888777666",
            ToRoutingNumber = "010101012",
            Amount = 100m
        });
        var transfer = await db.Transfers.FirstAsync();
        Assert.NotNull(transfer.ExternalReferenceId);
        Assert.StartsWith("MSG-", transfer.ExternalReferenceId);
    }

    [Fact]
    public async Task CreateFedNow_ToAccountIdIsNull_ExternalTransfer()
    {
        var (db, userId, fromAccountId) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateFedNow(new CreateFedNowTransferRequest
        {
            FromAccountId = fromAccountId,
            ToAccountNumber = "999888777666",
            ToRoutingNumber = "010101012",
            Amount = 100m
        });
        var transfer = await db.Transfers.FirstAsync();
        Assert.Null(transfer.ToAccountId);
    }
}
