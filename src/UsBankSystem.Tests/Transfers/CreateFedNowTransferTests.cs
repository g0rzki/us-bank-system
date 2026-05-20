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
using UsBankSystem.Api.Models.Auth;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Services;
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
            Rtp = new TimeoutConfig { TimeoutSeconds = 10 },
            FedNow = new TimeoutConfig { TimeoutSeconds = 10 }
        });

    private static AchGateway CreateAchGateway() =>
        new(new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "{}"))
            { BaseAddress = new Uri("http://localhost:6001") },
            NullLogger<AchGateway>.Instance);

    private static RtpGateway CreateRtpGateway() =>
        new(new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "{}"))
            { BaseAddress = new Uri("http://localhost:6002") },
            NullLogger<RtpGateway>.Instance);

    private static FedNowGateway CreateFedNowGateway(HttpStatusCode statusCode) =>
        new(new HttpClient(new MockHttpMessageHandler(statusCode, """{"referenceId":"FEDNOW-REF-001"}"""))
            { BaseAddress = new Uri("http://localhost:6003") },
            NullLogger<FedNowGateway>.Instance);

    private static SwiftGateway CreateSwiftGateway() =>
        new(new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "{}"))
            { BaseAddress = new Uri("http://localhost:6004") },
            NullLogger<SwiftGateway>.Instance);

    private TransfersController CreateController(AppDbContext db, Guid userId, HttpStatusCode fedNowStatus = HttpStatusCode.OK)
    {
        var service = new TransferService(db, CreateAchGateway(), CreateRtpGateway(), CreateFedNowGateway(fedNowStatus), CreateSwiftGateway(), CreatePaymentConfig());
        var controller = new TransfersController(service, CreateConfig());
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
        var accountController = new AccountsController(accountService);
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
    public async Task CreateFedNow_ValidRequest_Returns201()
    {
        var (db, userId, fromAccountId, toAccountNumber) = await Setup();
        var controller = CreateController(db, userId);
        var result = await controller.CreateFedNow(new CreateFedNowTransferRequest
        {
            FromAccountId = fromAccountId,
            ToAccountNumber = toAccountNumber,
            Amount = 100m
        });
        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
    }

    [Fact]
    public async Task CreateFedNow_BalanceUpdatedImmediately()
    {
        var (db, userId, fromAccountId, toAccountNumber) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateFedNow(new CreateFedNowTransferRequest
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
    public async Task CreateFedNow_StatusCompleted()
    {
        var (db, userId, fromAccountId, toAccountNumber) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateFedNow(new CreateFedNowTransferRequest
        {
            FromAccountId = fromAccountId,
            ToAccountNumber = toAccountNumber,
            Amount = 100m
        });
        var transfer = await db.Transfers.FirstAsync();
        Assert.Equal(TransferStatus.Completed, transfer.Status);
    }

    [Fact]
    public async Task CreateFedNow_TwoTransactionsCreated()
    {
        var (db, userId, fromAccountId, toAccountNumber) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateFedNow(new CreateFedNowTransferRequest
        {
            FromAccountId = fromAccountId,
            ToAccountNumber = toAccountNumber,
            Amount = 100m
        });
        Assert.Equal(2, await db.Transactions.CountAsync());
    }

    [Fact]
    public async Task CreateFedNow_InsufficientFunds_Throws()
    {
        var (db, userId, fromAccountId, toAccountNumber) = await Setup();
        var controller = CreateController(db, userId);
        await Assert.ThrowsAsync<ArgumentException>(() => controller.CreateFedNow(new CreateFedNowTransferRequest
        {
            FromAccountId = fromAccountId,
            ToAccountNumber = toAccountNumber,
            Amount = 9999m
        }));
    }

    [Fact]
    public async Task CreateFedNow_GatewayFailure_ThrowsAndReleasesReservation()
    {
        var (db, userId, fromAccountId, toAccountNumber) = await Setup();
        var controller = CreateController(db, userId, HttpStatusCode.BadRequest);
        await Assert.ThrowsAsync<ArgumentException>(() => controller.CreateFedNow(new CreateFedNowTransferRequest
        {
            FromAccountId = fromAccountId,
            ToAccountNumber = toAccountNumber,
            Amount = 100m
        }));
        var account = await db.Accounts.FindAsync(fromAccountId);
        Assert.Equal(0m, account!.ReservedBalance);
    }
}