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
using UsBankSystem.Api.Models.Auth;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Services;
using UsBankSystem.Api.Services.Payments;
using UsBankSystem.Core.Domain.Transfers;
using UsBankSystem.Infrastructure.Persistence;
using UsBankSystem.Tests.Helpers;

namespace UsBankSystem.Tests.Transfers;

public class CreateSwiftTransferTests
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

    private IOptions<PaymentSessionConfig> CreatePaymentConfig(decimal swiftDailyLimit = 50_000m) =>
        Options.Create(new PaymentSessionConfig
        {
            Ach = new AchConfig { BatchWindowMinutes = 1, CutoffHour = 23 },
            Rtp = new TimeoutConfig { TimeoutSeconds = 10 },
            FedNow = new TimeoutConfig { TimeoutSeconds = 10 },
            Swift = new SwiftConfig { TimeoutSeconds = 10, DailyLimitPerAccount = swiftDailyLimit }
        });

    private static AchGateway CreateAchGateway() =>
        AchTestHelpers.CreateGateway();

    private static RtpGateway CreateRtpGateway() =>
        new(new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "{}"))
            { BaseAddress = new Uri("http://localhost:6002") },
            NullLogger<RtpGateway>.Instance);

    private static FedNowGateway CreateFedNowGateway() =>
        new(new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "{}"))
            { BaseAddress = new Uri("http://localhost:6003") },
            NullLogger<FedNowGateway>.Instance);

    private static SwiftGateway CreateSwiftGateway(HttpStatusCode swiftStatusCode) =>
        new(new HttpClient(new RoutingMockHttpMessageHandler(
        [
            ("/auth/token",  HttpStatusCode.OK,       """{"access_token":"test-token","token_type":"Bearer","expires_in":3600}"""),
            ("/swift/message", swiftStatusCode,        """{"uetr":"SWIFT-REF-001","status":"accepted","route":[]}""")
        ])) { BaseAddress = new Uri("http://localhost:6004") },
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new SwiftOptions()),
            NullLogger<SwiftGateway>.Instance);

    private TransfersController CreateController(AppDbContext db, Guid userId, HttpStatusCode swiftStatus = HttpStatusCode.OK, decimal swiftDailyLimit = 50_000m)
    {
        var internalPayment = new InternalPaymentService(db);
        var achPayment = new AchPaymentService(db, CreateAchGateway(), CreatePaymentConfig(swiftDailyLimit));
        var rtpPayment = new RtpPaymentService(db, CreateRtpGateway(), CreatePaymentConfig(swiftDailyLimit));
        var fedNowPayment = new FedNowPaymentService(db, CreateFedNowGateway(), CreatePaymentConfig(swiftDailyLimit));
        var swiftPayment = new SwiftPaymentService(db, CreateSwiftGateway(swiftStatus), CreatePaymentConfig(swiftDailyLimit));
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

    private async Task<(AppDbContext db, Guid userId, Guid accountId)> Setup()
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
        var account = await db.Accounts.FirstAsync();
        account.Balance = 1000m;
        await db.SaveChangesAsync();
        return (db, user.Id, account.Id);
    }

    private CreateSwiftTransferRequest ValidRequest(Guid accountId) => new()
    {
        FromAccountId = accountId,
        Iban = "DE89370400440532013000",
        Bic = "DEUTDEDB",
        BeneficiaryName = "Max Mustermann",
        BeneficiaryAddress = "Teststrasse 1, 10115 Berlin",
        Amount = 100m,
        Currency = "USD",
        ChargeBearer = "SHA"
    };

    [Fact]
    public async Task CreateSwift_ValidRequest_Returns201()
    {
        var (db, userId, accountId) = await Setup();
        var controller = CreateController(db, userId);
        var result = await controller.CreateSwift(ValidRequest(accountId));
        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
    }

    [Fact]
    public async Task CreateSwift_StatusPending()
    {
        var (db, userId, accountId) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateSwift(ValidRequest(accountId));
        var transfer = await db.Transfers.FirstAsync();
        Assert.Equal(TransferStatus.Pending, transfer.Status);
    }

    [Fact]
    public async Task CreateSwift_ReservesBalance()
    {
        var (db, userId, accountId) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateSwift(ValidRequest(accountId));
        var account = await db.Accounts.FindAsync(accountId);
        Assert.Equal(100m, account!.ReservedBalance);
        Assert.Equal(1000m, account.Balance);
    }

    [Fact]
    public async Task CreateSwift_OneTransactionCreated()
    {
        var (db, userId, accountId) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateSwift(ValidRequest(accountId));
        Assert.Equal(1, await db.Transactions.CountAsync());
    }

    [Fact]
    public async Task CreateSwift_ExternalReferenceIdSet()
    {
        var (db, userId, accountId) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateSwift(ValidRequest(accountId));
        var transfer = await db.Transfers.FirstAsync();
        Assert.Equal("SWIFT-REF-001", transfer.ExternalReferenceId);
    }

    [Fact]
    public async Task CreateSwift_BalanceNotDebitedUntilWebhook()
    {
        var (db, userId, accountId) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateSwift(ValidRequest(accountId));
        var account = await db.Accounts.FindAsync(accountId);
        Assert.Equal(1000m, account!.Balance);
        Assert.Equal(100m, account.ReservedBalance);
    }

    [Fact]
    public async Task CreateSwift_InsufficientFunds_Throws()
    {
        var (db, userId, accountId) = await Setup();
        var controller = CreateController(db, userId);
        await Assert.ThrowsAsync<ArgumentException>(() => controller.CreateSwift(new CreateSwiftTransferRequest
        {
            FromAccountId = accountId,
            Iban = "DE89370400440532013000",
            Bic = "DEUTDEDB",
            BeneficiaryName = "Max Mustermann",
            Amount = 9999m
        }));
    }

    [Fact]
    public async Task CreateSwift_InvalidIban_Throws()
    {
        var (db, userId, accountId) = await Setup();
        var controller = CreateController(db, userId);
        var req = ValidRequest(accountId);
        req.Iban = "INVALID_IBAN";
        await Assert.ThrowsAsync<ArgumentException>(() => controller.CreateSwift(req));
    }

    [Fact]
    public async Task CreateSwift_InvalidBic_Throws()
    {
        var (db, userId, accountId) = await Setup();
        var controller = CreateController(db, userId);
        var req = ValidRequest(accountId);
        req.Bic = "BADBIC";
        await Assert.ThrowsAsync<ArgumentException>(() => controller.CreateSwift(req));
    }

    [Fact]
    public async Task CreateSwift_InvalidChargeBearer_Throws()
    {
        var (db, userId, accountId) = await Setup();
        var controller = CreateController(db, userId);
        var req = ValidRequest(accountId);
        req.ChargeBearer = "INVALID";
        await Assert.ThrowsAsync<ArgumentException>(() => controller.CreateSwift(req));
    }

    [Fact]
    public async Task CreateSwift_PastValueDate_Throws()
    {
        var (db, userId, accountId) = await Setup();
        var controller = CreateController(db, userId);
        var req = ValidRequest(accountId);
        req.ValueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        await Assert.ThrowsAsync<ArgumentException>(() => controller.CreateSwift(req));
    }

    [Fact]
    public async Task CreateSwift_NoValueDate_DefaultsToNextBusinessDay()
    {
        var (db, userId, accountId) = await Setup();
        var controller = CreateController(db, userId);
        var req = ValidRequest(accountId);
        req.ValueDate = null;
        var result = await controller.CreateSwift(req);
        Assert.IsType<ObjectResult>(result);
    }

    [Fact]
    public async Task CreateSwift_UnsupportedCurrency_Throws()
    {
        var (db, userId, accountId) = await Setup();
        var controller = CreateController(db, userId);
        var req = ValidRequest(accountId);
        req.Currency = "XYZ";
        await Assert.ThrowsAsync<ArgumentException>(() => controller.CreateSwift(req));
    }

    [Fact]
    public async Task CreateSwift_DailyLimitExceeded_Throws()
    {
        var (db, userId, accountId) = await Setup();
        var controller = CreateController(db, userId, swiftDailyLimit: 50m);
        var req = ValidRequest(accountId);
        req.Amount = 100m;
        await Assert.ThrowsAsync<ArgumentException>(() => controller.CreateSwift(req));
    }

    [Fact]
    public async Task CreateSwift_GatewayFailure_ThrowsAndReleasesReservation()
    {
        var (db, userId, accountId) = await Setup();
        var controller = CreateController(db, userId, HttpStatusCode.BadRequest);
        await Assert.ThrowsAsync<ArgumentException>(() => controller.CreateSwift(ValidRequest(accountId)));
        var account = await db.Accounts.FindAsync(accountId);
        Assert.Equal(0m, account!.ReservedBalance);
    }
}


