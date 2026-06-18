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
using Transfer = UsBankSystem.Core.Entities.Transfer;
using UsBankSystem.Infrastructure.Persistence;
using UsBankSystem.Tests.Helpers;

namespace UsBankSystem.Tests.Transfers;

public class GetPendingApprovalTests
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
        var transferService = new TransferService(db, mqGateway, new Pacs008Builder(), CreatePaymentConfig());
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

    private async Task<(AppDbContext db, Guid parentUserId, Guid parentAccountId, Guid juniorAccountId, Guid otherUserId, Guid otherAccountId)> Setup()
    {
        var db = CreateDb();

        var parentUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "parent@example.com",
            PasswordHash = "hash",
            FirstName = "Parent",
            LastName = "User",
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };
        var otherUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "other@example.com",
            PasswordHash = "hash",
            FirstName = "Other",
            LastName = "User",
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };
        db.Users.AddRange(parentUser, otherUser);

        var parentAccount = new Account
        {
            Id = Guid.NewGuid(),
            UserId = parentUser.Id,
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
            UserId = parentUser.Id,
            AccountNumber = "2000000001",
            Type = "checking",
            Balance = 100m,
            ReservedBalance = 30m,
            Currency = "USD",
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };
        var otherAccount = new Account
        {
            Id = Guid.NewGuid(),
            UserId = otherUser.Id,
            AccountNumber = "3000000001",
            Type = "checking",
            Balance = 500m,
            ReservedBalance = 0m,
            Currency = "USD",
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };
        db.Accounts.AddRange(parentAccount, juniorAccount, otherAccount);

        db.JuniorAccounts.Add(new JuniorAccount
        {
            Id = Guid.NewGuid(),
            AccountId = juniorAccount.Id,
            ParentUserId = parentUser.Id,
            DateOfBirth = new DateOnly(2015, 6, 15),
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        return (db, parentUser.Id, parentAccount.Id, juniorAccount.Id, otherUser.Id, otherAccount.Id);
    }

    private Transfer MakePendingApprovalTransfer(Guid fromAccountId, Guid toAccountId, string description = "Test") =>
        new()
        {
            Id = Guid.NewGuid(),
            FromAccountId = fromAccountId,
            ToAccountId = toAccountId,
            Amount = 30m,
            Currency = "USD",
            Channel = TransferChannel.Internal,
            Status = TransferStatus.PendingApproval,
            Description = description,
            RequiresApproval = true,
            CreatedAt = DateTime.UtcNow
        };

    [Fact]
    public async Task GetPendingApproval_WithPendingTransfers_Returns200WithList()
    {
        var (db, parentUserId, parentAccountId, juniorAccountId, _, otherAccountId) = await Setup();
        db.Transfers.Add(MakePendingApprovalTransfer(juniorAccountId, otherAccountId));
        await db.SaveChangesAsync();

        var controller = CreateController(db, parentUserId);
        var result = await controller.GetPendingApproval();

        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsType<List<PendingApprovalTransferResponse>>(ok.Value);
        Assert.Single(list);
        Assert.Equal(juniorAccountId, list[0].FromAccountId);
        Assert.Equal(30m, list[0].Amount);
    }

    [Fact]
    public async Task GetPendingApproval_NoPendingTransfers_ReturnsEmptyList()
    {
        var (db, parentUserId, _, _, _, _) = await Setup();
        var controller = CreateController(db, parentUserId);

        var result = await controller.GetPendingApproval();

        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsType<List<PendingApprovalTransferResponse>>(ok.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task GetPendingApproval_OtherUsersJuniorTransfers_NotVisible()
    {
        var (db, _, _, juniorAccountId, otherUserId, otherAccountId) = await Setup();
        db.Transfers.Add(MakePendingApprovalTransfer(juniorAccountId, otherAccountId));
        await db.SaveChangesAsync();

        var controller = CreateController(db, otherUserId);
        var result = await controller.GetPendingApproval();

        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsType<List<PendingApprovalTransferResponse>>(ok.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task GetPendingApproval_CompletedTransfersExcluded()
    {
        var (db, parentUserId, _, juniorAccountId, _, otherAccountId) = await Setup();
        db.Transfers.Add(new Transfer
        {
            Id = Guid.NewGuid(),
            FromAccountId = juniorAccountId,
            ToAccountId = otherAccountId,
            Amount = 50m,
            Currency = "USD",
            Channel = TransferChannel.Internal,
            Status = TransferStatus.Completed,
            RequiresApproval = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, parentUserId);
        var result = await controller.GetPendingApproval();

        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsType<List<PendingApprovalTransferResponse>>(ok.Value);
        Assert.Empty(list);
    }

    [Fact]
    public async Task GetPendingApproval_MultipleTransfers_OrderedByCreatedAt()
    {
        var (db, parentUserId, _, juniorAccountId, _, otherAccountId) = await Setup();
        db.Transfers.AddRange(
            new Transfer
            {
                Id = Guid.NewGuid(),
                FromAccountId = juniorAccountId,
                ToAccountId = otherAccountId,
                Amount = 10m,
                Currency = "USD",
                Channel = TransferChannel.Internal,
                Status = TransferStatus.PendingApproval,
                Description = "First",
                RequiresApproval = true,
                CreatedAt = DateTime.UtcNow.AddHours(-2)
            },
            new Transfer
            {
                Id = Guid.NewGuid(),
                FromAccountId = juniorAccountId,
                ToAccountId = otherAccountId,
                Amount = 20m,
                Currency = "USD",
                Channel = TransferChannel.Internal,
                Status = TransferStatus.PendingApproval,
                Description = "Second",
                RequiresApproval = true,
                CreatedAt = DateTime.UtcNow.AddHours(-1)
            }
        );
        await db.SaveChangesAsync();

        var controller = CreateController(db, parentUserId);
        var result = await controller.GetPendingApproval();

        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsType<List<PendingApprovalTransferResponse>>(ok.Value);
        Assert.Equal(2, list.Count);
        Assert.Equal("First", list[0].Description);
        Assert.Equal("Second", list[1].Description);
    }

    [Fact]
    public async Task GetPendingApproval_ReturnsCorrectAccountNumber()
    {
        var (db, parentUserId, _, juniorAccountId, _, otherAccountId) = await Setup();
        db.Transfers.Add(MakePendingApprovalTransfer(juniorAccountId, otherAccountId));
        await db.SaveChangesAsync();

        var controller = CreateController(db, parentUserId);
        var result = await controller.GetPendingApproval();

        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsType<List<PendingApprovalTransferResponse>>(ok.Value);
        var juniorAccountNumber = (await db.Accounts.FindAsync(juniorAccountId))!.AccountNumber;
        Assert.Equal(juniorAccountNumber, list[0].FromAccountNumber);
    }
}


