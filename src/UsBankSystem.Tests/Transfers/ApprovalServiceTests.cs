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
using UsBankSystem.Core.Entities;
using UsBankSystem.Infrastructure.Persistence;
using UsBankSystem.Tests.Helpers;

namespace UsBankSystem.Tests.Transfers;

public class ApprovalServiceTests
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
            FedNow = new TimeoutConfig { TimeoutSeconds = 10 },
            Swift = new SwiftConfig { TimeoutSeconds = 10 }
        });

    private TransfersController CreateController(AppDbContext db, Guid userId)
    {
        var achGateway = AchTestHelpers.CreateGateway();
        var rtpGateway = new RtpGateway(new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "{}")) { BaseAddress = new Uri("http://localhost:6002") }, NullLogger<RtpGateway>.Instance);
        var fedNowGateway = new FedNowGateway(new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "{}")) { BaseAddress = new Uri("http://localhost:6003") }, NullLogger<FedNowGateway>.Instance);
        var swiftGateway = new SwiftGateway(new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "{}")) { BaseAddress = new Uri("http://localhost:6004") }, new MemoryCache(new MemoryCacheOptions()), Options.Create(new SwiftOptions()), NullLogger<SwiftGateway>.Instance);
        var internalPayment = new InternalPaymentService(db);
        var achPayment = new AchPaymentService(db, achGateway, CreatePaymentConfig());
        var rtpPayment = new RtpPaymentService(db, rtpGateway, CreatePaymentConfig());
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

    private async Task<(AppDbContext db, Guid userId, Guid parentAccountId, Guid juniorAccountId, Guid toAccountId)> Setup()
    {
        var db = CreateDb();
        var authService = new AuthService(db, CreateConfig());
        await authService.RegisterAsync(new RegisterRequest
        {
            Email = "parent@example.com",
            Password = "Password123!",
            FirstName = "Jan",
            LastName = "Kowalski"
        });
        var user = await db.Users.FirstAsync();

        var parentAccount = new Account
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            AccountNumber = "1000000001",
            Type = "checking",
            Balance = 1000m,
            ReservedBalance = 0m,
            Currency = "USD",
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };
        var juniorAccount = new Account
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            AccountNumber = "2000000001",
            Type = "checking",
            Balance = 200m,
            ReservedBalance = 0m,
            Currency = "USD",
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };
        var toAccount = new Account
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            AccountNumber = "3000000001",
            Type = "checking",
            Balance = 0m,
            ReservedBalance = 0m,
            Currency = "USD",
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };
        db.Accounts.AddRange(parentAccount, juniorAccount, toAccount);

        db.JuniorAccounts.Add(new JuniorAccount
        {
            Id = Guid.NewGuid(),
            AccountId = juniorAccount.Id,
            ParentUserId = user.Id,
            DateOfBirth = new DateOnly(2015, 6, 15),
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        return (db, user.Id, parentAccount.Id, juniorAccount.Id, toAccount.Id);
    }

    [Fact]
    public async Task CreateInternal_FromJuniorAccount_StatusIsPendingApproval()
    {
        var (db, userId, _, juniorAccountId, toAccountId) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateInternal(new CreateInternalTransferRequest
        {
            FromAccountId = juniorAccountId,
            ToAccountId = toAccountId,
            Amount = 50m
        });
        var transfer = await db.Transfers.FirstAsync();
        Assert.Equal(TransferStatus.PendingApproval, transfer.Status);
        Assert.True(transfer.RequiresApproval);
    }

    [Fact]
    public async Task CreateInternal_FromJuniorAccount_FundsReserved()
    {
        var (db, userId, _, juniorAccountId, toAccountId) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateInternal(new CreateInternalTransferRequest
        {
            FromAccountId = juniorAccountId,
            ToAccountId = toAccountId,
            Amount = 50m
        });
        var account = await db.Accounts.FindAsync(juniorAccountId);
        Assert.Equal(50m, account!.ReservedBalance);
        Assert.Equal(200m, account.Balance);
    }

    [Fact]
    public async Task CreateInternal_FromJuniorAccount_BalanceNotDebited()
    {
        var (db, userId, _, juniorAccountId, toAccountId) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateInternal(new CreateInternalTransferRequest
        {
            FromAccountId = juniorAccountId,
            ToAccountId = toAccountId,
            Amount = 50m
        });
        var toAcc = await db.Accounts.FindAsync(toAccountId);
        Assert.Equal(0m, toAcc!.Balance);
    }

    [Fact]
    public async Task CreateInternal_FromRegularAccount_StatusIsCompleted()
    {
        var (db, userId, parentAccountId, _, toAccountId) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateInternal(new CreateInternalTransferRequest
        {
            FromAccountId = parentAccountId,
            ToAccountId = toAccountId,
            Amount = 100m
        });
        var transfer = await db.Transfers.FirstAsync();
        Assert.Equal(TransferStatus.Completed, transfer.Status);
        Assert.False(transfer.RequiresApproval);
    }

    [Fact]
    public async Task CreateInternal_FromJuniorAccount_DailyLimitRespected()
    {
        var (db, userId, _, juniorAccountId, toAccountId) = await Setup();

        db.Cards.Add(new Card
        {
            Id = Guid.NewGuid(),
            AccountId = juniorAccountId,
            Last4 = "9001",
            Type = "prepaid",
            Status = "active",
            DailyLimit = 30m,
            MonthlyLimit = 100m,
            ExpiresAt = DateTime.UtcNow.AddYears(3),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, userId);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            controller.CreateInternal(new CreateInternalTransferRequest
            {
                FromAccountId = juniorAccountId,
                ToAccountId = toAccountId,
                Amount = 50m
            }));
    }
}

