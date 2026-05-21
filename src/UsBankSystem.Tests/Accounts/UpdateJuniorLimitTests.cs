using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using UsBankSystem.Api.Controllers;
using UsBankSystem.Api.Models.Auth;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Api.Services;
using UsBankSystem.Core.Entities;
using UsBankSystem.Infrastructure.Persistence;

namespace UsBankSystem.Tests.Accounts;

public class UpdateJuniorLimitTests
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

    private AccountsController CreateController(AppDbContext db, Guid userId)
    {
        var controller = new AccountsController(new AccountService(db), new TransactionService(db), new JuniorService(db));
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

    private async Task<(AppDbContext db, Guid parentUserId, Guid juniorAccountId, Guid otherUserId)> Setup()
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
        await authService.RegisterAsync(new RegisterRequest
        {
            Email = "other@example.com",
            Password = "Password123!",
            FirstName = "Other",
            LastName = "User"
        });
        var users = await db.Users.ToListAsync();
        var parentUser = users.First(u => u.Email == "parent@example.com");
        var otherUser = users.First(u => u.Email == "other@example.com");

        var juniorAccount = new Account
        {
            Id = Guid.NewGuid(),
            UserId = parentUser.Id,
            AccountNumber = "9000000001",
            Type = "checking",
            Balance = 0,
            ReservedBalance = 0,
            Currency = "USD",
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };
        db.Accounts.Add(juniorAccount);

        db.JuniorAccounts.Add(new JuniorAccount
        {
            Id = Guid.NewGuid(),
            AccountId = juniorAccount.Id,
            ParentUserId = parentUser.Id,
            DateOfBirth = new DateOnly(2015, 6, 15),
            CreatedAt = DateTime.UtcNow
        });

        db.Cards.Add(new Card
        {
            Id = Guid.NewGuid(),
            AccountId = juniorAccount.Id,
            Last4 = "9001",
            Type = "prepaid",
            Status = "active",
            DailyLimit = 50m,
            MonthlyLimit = 300m,
            ExpiresAt = DateTime.UtcNow.AddYears(3),
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        return (db, parentUser.Id, juniorAccount.Id, otherUser.Id);
    }

    [Fact]
    public async Task UpdateJuniorLimit_DailyLimit_Returns200()
    {
        var (db, parentUserId, juniorAccountId, _) = await Setup();
        var controller = CreateController(db, parentUserId);
        var result = await controller.UpdateJuniorLimit(juniorAccountId, new UpdateJuniorLimitRequest { DailyLimit = 100m });
        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<CardResponse>(ok.Value);
        Assert.Equal(100m, response.DailyLimit);
        Assert.Equal(300m, response.MonthlyLimit);
    }

    [Fact]
    public async Task UpdateJuniorLimit_MonthlyLimit_Returns200()
    {
        var (db, parentUserId, juniorAccountId, _) = await Setup();
        var controller = CreateController(db, parentUserId);
        var result = await controller.UpdateJuniorLimit(juniorAccountId, new UpdateJuniorLimitRequest { MonthlyLimit = 500m });
        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<CardResponse>(ok.Value);
        Assert.Equal(50m, response.DailyLimit);
        Assert.Equal(500m, response.MonthlyLimit);
    }

    [Fact]
    public async Task UpdateJuniorLimit_BothLimits_UpdatedCorrectly()
    {
        var (db, parentUserId, juniorAccountId, _) = await Setup();
        var controller = CreateController(db, parentUserId);
        await controller.UpdateJuniorLimit(juniorAccountId, new UpdateJuniorLimitRequest { DailyLimit = 75m, MonthlyLimit = 400m });
        var card = await db.Cards.FirstAsync();
        Assert.Equal(75m, card.DailyLimit);
        Assert.Equal(400m, card.MonthlyLimit);
    }

    [Fact]
    public async Task UpdateJuniorLimit_NoLimits_Throws()
    {
        var (db, parentUserId, juniorAccountId, _) = await Setup();
        var controller = CreateController(db, parentUserId);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            controller.UpdateJuniorLimit(juniorAccountId, new UpdateJuniorLimitRequest()));
    }

    [Fact]
    public async Task UpdateJuniorLimit_NotFound_Throws()
    {
        var (db, parentUserId, _, _) = await Setup();
        var controller = CreateController(db, parentUserId);
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            controller.UpdateJuniorLimit(Guid.NewGuid(), new UpdateJuniorLimitRequest { DailyLimit = 100m }));
    }

    [Fact]
    public async Task UpdateJuniorLimit_OtherUser_Throws()
    {
        var (db, _, juniorAccountId, otherUserId) = await Setup();
        var controller = CreateController(db, otherUserId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.UpdateJuniorLimit(juniorAccountId, new UpdateJuniorLimitRequest { DailyLimit = 100m }));
    }

    [Fact]
    public async Task UpdateJuniorLimit_NoCard_Throws()
    {
        var (db, parentUserId, _, _) = await Setup();
        var authService = new AuthService(db, CreateConfig());
        await authService.RegisterAsync(new RegisterRequest
        {
            Email = "junior2@example.com",
            Password = "Password123!",
            FirstName = "Junior",
            LastName = "Two"
        });
        var juniorUser = await db.Users.FirstAsync(u => u.Email == "junior2@example.com");
        var juniorAccount = new Account
        {
            Id = Guid.NewGuid(),
            UserId = juniorUser.Id,
            AccountNumber = "9000000002",
            Type = "checking",
            Balance = 0,
            ReservedBalance = 0,
            Currency = "USD",
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };
        db.Accounts.Add(juniorAccount);
        db.JuniorAccounts.Add(new JuniorAccount
        {
            Id = Guid.NewGuid(),
            AccountId = juniorAccount.Id,
            ParentUserId = parentUserId,
            DateOfBirth = new DateOnly(2015, 1, 1),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, parentUserId);
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            controller.UpdateJuniorLimit(juniorAccount.Id, new UpdateJuniorLimitRequest { DailyLimit = 100m }));
    }
}
