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

public class CreateRtpTransferTests
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
            Rtp = new TimeoutConfig { TimeoutSeconds = 10 },
            FedNow = new TimeoutConfig { TimeoutSeconds = 10 }
        });

    private static AchGateway CreateAchGateway() =>
        AchTestHelpers.CreateGateway();

    private static RtpGateway CreateRtpGateway(HttpStatusCode statusCode) =>
        new(new HttpClient(new MockHttpMessageHandler(statusCode, """{"referenceId":"RTP-REF-001"}"""))
            { BaseAddress = new Uri("http://localhost:6002") },
            NullLogger<RtpGateway>.Instance);

    private TransfersController CreateController(AppDbContext db, Guid userId, HttpStatusCode rtpStatus = HttpStatusCode.OK)
    {
        var fedNowGateway = new FedNowGateway(
            new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "{}"))
                { BaseAddress = new Uri("http://localhost:6003") },
            NullLogger<FedNowGateway>.Instance);
        var swiftGateway = new SwiftGateway(
            new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "{}"))
                { BaseAddress = new Uri("http://localhost:6004") },
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new SwiftOptions()),
            NullLogger<SwiftGateway>.Instance);
        var internalPayment = new InternalPaymentService(db);
        var achPayment = new AchPaymentService(db, CreateAchGateway(), CreatePaymentConfig());
        var rtpPayment = new RtpPaymentService(db, CreateRtpGateway(rtpStatus), CreatePaymentConfig());
        var fedNowPayment = new FedNowPaymentService(db, fedNowGateway, CreatePaymentConfig());
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

    private async Task<(AppDbContext db, Guid userId, Guid fromAccountId, string toAccountNumber)> Setup()
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
        return (db, user.Id, accounts[0].Id, accounts[1].AccountNumber);
    }

    [Fact]
    public async Task CreateRtp_ValidRequest_Returns201()
    {
        var (db, userId, fromAccountId, toAccountNumber) = await Setup();
        var controller = CreateController(db, userId);
        var result = await controller.CreateRtp(new CreateRtpTransferRequest
        {
            FromAccountId = fromAccountId,
            ToAccountNumber = toAccountNumber,
            Amount = 100m
        });
        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
    }

    [Fact]
    public async Task CreateRtp_BalanceUpdatedImmediately()
    {
        var (db, userId, fromAccountId, toAccountNumber) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateRtp(new CreateRtpTransferRequest
        {
            FromAccountId = fromAccountId,
            ToAccountNumber = toAccountNumber,
            Amount = 100m
        });
        var fromAccount = await db.Accounts.FindAsync(fromAccountId);
        var toAccount = await db.Accounts.FirstOrDefaultAsync(a => a.AccountNumber == toAccountNumber);
        Assert.Equal(900m, fromAccount!.Balance);
        Assert.Equal(100m, toAccount!.Balance);
        Assert.Equal(0m, fromAccount.ReservedBalance);
    }

    [Fact]
    public async Task CreateRtp_StatusCompleted()
    {
        var (db, userId, fromAccountId, toAccountNumber) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateRtp(new CreateRtpTransferRequest
        {
            FromAccountId = fromAccountId,
            ToAccountNumber = toAccountNumber,
            Amount = 100m
        });
        var transfer = await db.Transfers.FirstAsync();
        Assert.Equal(TransferStatus.Completed, transfer.Status);
    }

    [Fact]
    public async Task CreateRtp_TwoTransactionsCreated()
    {
        var (db, userId, fromAccountId, toAccountNumber) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateRtp(new CreateRtpTransferRequest
        {
            FromAccountId = fromAccountId,
            ToAccountNumber = toAccountNumber,
            Amount = 100m
        });
        Assert.Equal(2, await db.Transactions.CountAsync());
    }

    [Fact]
    public async Task CreateRtp_InsufficientFunds_Throws()
    {
        var (db, userId, fromAccountId, toAccountNumber) = await Setup();
        var controller = CreateController(db, userId);
        await Assert.ThrowsAsync<ArgumentException>(() => controller.CreateRtp(new CreateRtpTransferRequest
        {
            FromAccountId = fromAccountId,
            ToAccountNumber = toAccountNumber,
            Amount = 9999m
        }));
    }

    [Fact]
    public async Task CreateRtp_GatewayFailure_ThrowsAndReleasesReservation()
    {
        var (db, userId, fromAccountId, toAccountNumber) = await Setup();
        var controller = CreateController(db, userId, HttpStatusCode.BadRequest);
        await Assert.ThrowsAsync<ArgumentException>(() => controller.CreateRtp(new CreateRtpTransferRequest
        {
            FromAccountId = fromAccountId,
            ToAccountNumber = toAccountNumber,
            Amount = 100m
        }));
        var account = await db.Accounts.FindAsync(fromAccountId);
        Assert.Equal(0m, account!.ReservedBalance);
    }
}

