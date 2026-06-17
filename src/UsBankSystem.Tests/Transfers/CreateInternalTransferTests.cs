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
using UsBankSystem.Api.Models.Auth;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Api.Services;
using UsBankSystem.Api.Services.Payments;
using UsBankSystem.Core.Domain.Common;
using UsBankSystem.Core.Domain.Transfers;
using UsBankSystem.Infrastructure.Persistence;
using UsBankSystem.Tests.Helpers;

namespace UsBankSystem.Tests.Transfers;

public class CreateInternalTransferTests
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

    private TransfersController CreateController(AppDbContext db, Guid userId)
	{
    	var handler = new MockHttpMessageHandler(HttpStatusCode.OK, """{"referenceId":"REF-001"}""");
    	var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:6001") };
    	var achGateway = new AchGateway(httpClient, NullLogger<AchGateway>.Instance);
    	var rtpGateway = new RtpGateway(
        	new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "{}"))
            	{ BaseAddress = new Uri("http://localhost:6002") },
        	NullLogger<RtpGateway>.Instance);
		var mqGateway = new FedNowMqGateway(
        	new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, """{"status":"sent"}"""))
            	{ BaseAddress = new Uri("http://localhost:8770") },
        	NullLogger<FedNowMqGateway>.Instance);
    	var paymentConfig = Options.Create(new PaymentSessionConfig
    	{
        	Ach = new AchConfig { BatchWindowMinutes = 1, CutoffHour = 23 },
        	Rtp = new TimeoutConfig { TimeoutSeconds = 10 },
 			FedNow = new FedNowConfig { TimeoutSeconds = 10, PollIntervalSeconds = 1, BankRtn = "040104018", BankLegalName = "Baguette Bank" },
            Swift = new SwiftConfig { TimeoutSeconds = 10 }
    	});
        var swiftGateway = new SwiftGateway(
            new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "{}"))
                { BaseAddress = new Uri("http://localhost:6004") },
            NullLogger<SwiftGateway>.Instance);
    	var internalPayment = new InternalPaymentService(db);
    	var achPayment = new AchPaymentService(db, achGateway, paymentConfig);
    	var rtpPayment = new RtpPaymentService(db, rtpGateway, paymentConfig);
    	var fedNowPayment = new FedNowPaymentService(db, mqGateway, new Pacs008Builder(), paymentConfig);
    	var swiftPayment = new SwiftPaymentService(db, swiftGateway, paymentConfig);
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

    [Fact]
    public async Task CreateInternal_ValidRequest_Returns201()
    {
        var (db, userId, fromAccountId, toAccountId) = await Setup();
        var controller = CreateController(db, userId);
        var result = await controller.CreateInternal(new CreateInternalTransferRequest
        {
            FromAccountId = fromAccountId,
            ToAccountId = toAccountId,
            Amount = 100m,
            Description = "Test transfer"
        });
        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
    }

    [Fact]
    public async Task CreateInternal_BalanceUpdated()
    {
        var (db, userId, fromAccountId, toAccountId) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateInternal(new CreateInternalTransferRequest
        {
            FromAccountId = fromAccountId,
            ToAccountId = toAccountId,
            Amount = 100m
        });
        var fromAccount = await db.Accounts.FindAsync(fromAccountId);
        var toAccount = await db.Accounts.FindAsync(toAccountId);
        Assert.Equal(900m, fromAccount!.Balance);
        Assert.Equal(100m, toAccount!.Balance);
    }

    [Fact]
    public async Task CreateInternal_TransferStatusCompleted()
    {
        var (db, userId, fromAccountId, toAccountId) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateInternal(new CreateInternalTransferRequest
        {
            FromAccountId = fromAccountId,
            ToAccountId = toAccountId,
            Amount = 100m
        });
        var transfer = await db.Transfers.FirstAsync();
        Assert.Equal(TransferStatus.Completed, transfer.Status);
    }

    [Fact]
    public async Task CreateInternal_TwoTransactionsCreated()
    {
        var (db, userId, fromAccountId, toAccountId) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateInternal(new CreateInternalTransferRequest
        {
            FromAccountId = fromAccountId,
            ToAccountId = toAccountId,
            Amount = 100m
        });
        Assert.Equal(2, await db.Transactions.CountAsync());
    }

    [Fact]
    public async Task CreateInternal_InsufficientFunds_Throws()
    {
        var (db, userId, fromAccountId, toAccountId) = await Setup();
        var controller = CreateController(db, userId);
        await Assert.ThrowsAsync<ArgumentException>(() => controller.CreateInternal(new CreateInternalTransferRequest
        {
            FromAccountId = fromAccountId,
            ToAccountId = toAccountId,
            Amount = 9999m
        }));
    }

    [Fact]
    public async Task CreateInternal_SameAccount_Throws()
    {
        var (db, userId, fromAccountId, _) = await Setup();
        var controller = CreateController(db, userId);
        await Assert.ThrowsAsync<ArgumentException>(() => controller.CreateInternal(new CreateInternalTransferRequest
        {
            FromAccountId = fromAccountId,
            ToAccountId = fromAccountId,
            Amount = 100m
        }));
    }

    [Fact]
    public async Task CreateInternal_InvalidCurrency_Throws()
    {
        var (db, userId, fromAccountId, toAccountId) = await Setup();
        var controller = CreateController(db, userId);
        await Assert.ThrowsAsync<ArgumentException>(() => controller.CreateInternal(new CreateInternalTransferRequest
        {
            FromAccountId = fromAccountId,
            ToAccountId = toAccountId,
            Amount = 100m,
            Currency = "EUR"
        }));
    }

    [Fact]
    public async Task CreateInternal_BlockedAccount_Throws()
    {
        var (db, userId, fromAccountId, toAccountId) = await Setup();
        var account = await db.Accounts.FindAsync(fromAccountId);
        account!.Status = AccountStatus.Blocked;
        await db.SaveChangesAsync();
        var controller = CreateController(db, userId);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => controller.CreateInternal(new CreateInternalTransferRequest
        {
            FromAccountId = fromAccountId,
            ToAccountId = toAccountId,
            Amount = 100m
        }));
    }
}


