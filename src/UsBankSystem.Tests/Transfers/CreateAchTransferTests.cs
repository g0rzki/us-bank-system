using System.Net;
using System.Security.Claims;
using System.Text;
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
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Api.Services;
using UsBankSystem.Infrastructure.Persistence;
using UsBankSystem.Tests.Helpers;

namespace UsBankSystem.Tests.Transfers;

public class CreateAchTransferTests
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
            Ach = new AchConfig { BatchWindowMinutes = 1, CutoffHour = 23 }
        });

    private static AchGateway CreateGateway(HttpStatusCode statusCode, string body)
    {
        var handler = new MockHttpMessageHandler(statusCode, body);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:6001") };
        return new AchGateway(client, NullLogger<AchGateway>.Instance);
    }

    private TransfersController CreateController(AppDbContext db, Guid userId, HttpStatusCode gatewayStatus = HttpStatusCode.OK)
    {
        var gateway = CreateGateway(gatewayStatus, """{"referenceId":"ACH-REF-001"}""");
		var rtpGateway = new RtpGateway(
        	new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "{}"))
            	{ BaseAddress = new Uri("http://localhost:6002") },
        	NullLogger<RtpGateway>.Instance);
        var service = new TransferService(db, gateway, rtpGateway, CreatePaymentConfig());
        var controller = new TransfersController(service);
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
        var account = await db.Accounts.FirstAsync();
        account.Balance = 1000m;
        await db.SaveChangesAsync();
        return (db, user.Id, account.Id);
    }

    [Fact]
    public async Task CreateAch_ValidRequest_Returns201()
    {
        var (db, userId, accountId) = await Setup();
        var controller = CreateController(db, userId);
        var result = await controller.CreateAch(new CreateAchTransferRequest
        {
            FromAccountId = accountId,
            ToRoutingNumber = "021000021",
            ToAccountNumber = "1234567890",
            Amount = 100m
        });
        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
    }

    [Fact]
    public async Task CreateAch_FundsReserved()
    {
        var (db, userId, accountId) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateAch(new CreateAchTransferRequest
        {
            FromAccountId = accountId,
            ToRoutingNumber = "021000021",
            ToAccountNumber = "1234567890",
            Amount = 100m
        });
        var account = await db.Accounts.FindAsync(accountId);
        Assert.Equal(100m, account!.ReservedBalance);
        Assert.Equal(1000m, account.Balance);
    }

    [Fact]
    public async Task CreateAch_InsufficientFunds_Returns400()
    {
        var (db, userId, accountId) = await Setup();
        var controller = CreateController(db, userId);
        var result = await controller.CreateAch(new CreateAchTransferRequest
        {
            FromAccountId = accountId,
            ToRoutingNumber = "021000021",
            ToAccountNumber = "1234567890",
            Amount = 9999m
        });
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateAch_GatewayFailure_Returns400AndReleasesReservation()
    {
        var (db, userId, accountId) = await Setup();
        var controller = CreateController(db, userId, HttpStatusCode.BadRequest);
        var result = await controller.CreateAch(new CreateAchTransferRequest
        {
            FromAccountId = accountId,
            ToRoutingNumber = "021000021",
            ToAccountNumber = "1234567890",
            Amount = 100m
        });
        Assert.IsType<BadRequestObjectResult>(result);
        var account = await db.Accounts.FindAsync(accountId);
        Assert.Equal(0m, account!.ReservedBalance);
    }

    [Fact]
    public async Task CreateAch_ExternalReferenceIdSaved()
    {
        var (db, userId, accountId) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateAch(new CreateAchTransferRequest
        {
            FromAccountId = accountId,
            ToRoutingNumber = "021000021",
            ToAccountNumber = "1234567890",
            Amount = 100m
        });
        var transfer = await db.Transfers.FirstAsync();
        Assert.Equal("ACH-REF-001", transfer.ExternalReferenceId);
    }
}