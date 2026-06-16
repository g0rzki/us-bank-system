using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using UsBankSystem.Api.Controllers;
using UsBankSystem.Api.Integrations;
using UsBankSystem.Api.Models.Auth;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Api.Services;
using UsBankSystem.Core.Entities;
using UsBankSystem.Infrastructure.Persistence;
using UsBankSystem.Tests.Helpers;

namespace UsBankSystem.Tests.Cards;

public class UpdateCardLimitsTests
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

    private CardsGateway CreateGateway() =>
        new(new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "{\"card_token\":\"tok_test\",\"masked_pan\":\"**** **** **** 1234\"}")) { BaseAddress = new Uri("http://localhost:6005") },
            CreateConfig(),
            NullLogger<CardsGateway>.Instance);

    private CardsController CreateController(AppDbContext db, Guid userId)
    {
        var service = new CardService(db, CreateGateway(), NullLogger<CardService>.Instance);
        var controller = new CardsController(service);
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

    private async Task<(AppDbContext db, Guid userId, Guid accountId, Guid cardId, Guid otherUserId, Guid juniorAccountId, Guid parentUserId)> Setup(
        decimal? dailyLimit = null, decimal? monthlyLimit = null)
    {
        var db = CreateDb();
        var authService = new AuthService(db, CreateConfig());

        await authService.RegisterAsync(new RegisterRequest
        {
            Email = "parent@example.com", Password = "Password123!", FirstName = "Jan", LastName = "Kowalski"
        });
        await authService.RegisterAsync(new RegisterRequest
        {
            Email = "other@example.com", Password = "Password123!", FirstName = "Other", LastName = "User"
        });
        await authService.RegisterAsync(new RegisterRequest
        {
            Email = "junior@example.com", Password = "Password123!", FirstName = "Emma", LastName = "Kowalski"
        });

        var users = await db.Users.ToListAsync();
        var parent = users.First(u => u.Email == "parent@example.com");
        var other = users.First(u => u.Email == "other@example.com");
        var junior = users.First(u => u.Email == "junior@example.com");

        var account = new Account
        {
            Id = Guid.NewGuid(), UserId = parent.Id, AccountNumber = "1000000001",
            Type = "checking", Balance = 1000m, ReservedBalance = 0,
            Currency = "USD", Status = "active", CreatedAt = DateTime.UtcNow
        };
        var juniorAccount = new Account
        {
            Id = Guid.NewGuid(), UserId = junior.Id, AccountNumber = "1000000002",
            Type = "checking", Balance = 500m, ReservedBalance = 0,
            Currency = "USD", Status = "active", CreatedAt = DateTime.UtcNow
        };
        db.Accounts.AddRange(account, juniorAccount);

        db.JuniorAccounts.Add(new JuniorAccount
        {
            Id = Guid.NewGuid(), AccountId = juniorAccount.Id, ParentUserId = parent.Id,
            CreatedAt = DateTime.UtcNow
        });

        var card = new Card
        {
            Id = Guid.NewGuid(), AccountId = account.Id, Last4 = "1234",
            Type = "prepaid", Status = "active", DailyLimit = dailyLimit, MonthlyLimit = monthlyLimit,
            ExpiresAt = DateTime.UtcNow.AddYears(3), CreatedAt = DateTime.UtcNow
        };
        db.Cards.Add(card);
        await db.SaveChangesAsync();

        return (db, parent.Id, account.Id, card.Id, other.Id, juniorAccount.Id, junior.Id);
    }

    [Fact]
    public async Task UpdateLimits_BothLimits_Returns200()
    {
        var (db, userId, accountId, cardId, _, _, _) = await Setup();
        var controller = CreateController(db, userId);

        var result = await controller.UpdateCardLimits(accountId, cardId,
            new UpdateCardLimitsRequest { DailyLimit = 100m, MonthlyLimit = 500m });

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<CardResponse>(ok.Value);
        Assert.Equal(100m, response.DailyLimit);
        Assert.Equal(500m, response.MonthlyLimit);
    }

    [Fact]
    public async Task UpdateLimits_OnlyDaily_UpdatesOnlyDaily()
    {
        var (db, userId, accountId, cardId, _, _, _) = await Setup(dailyLimit: 50m, monthlyLimit: 300m);
        var controller = CreateController(db, userId);

        var result = await controller.UpdateCardLimits(accountId, cardId,
            new UpdateCardLimitsRequest { DailyLimit = 100m });

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<CardResponse>(ok.Value);
        Assert.Equal(100m, response.DailyLimit);
        Assert.Equal(300m, response.MonthlyLimit);
    }

    [Fact]
    public async Task UpdateLimits_OnlyMonthly_UpdatesOnlyMonthly()
    {
        var (db, userId, accountId, cardId, _, _, _) = await Setup(dailyLimit: 50m, monthlyLimit: 300m);
        var controller = CreateController(db, userId);

        var result = await controller.UpdateCardLimits(accountId, cardId,
            new UpdateCardLimitsRequest { MonthlyLimit = 500m });

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<CardResponse>(ok.Value);
        Assert.Equal(50m, response.DailyLimit);
        Assert.Equal(500m, response.MonthlyLimit);
    }

    [Fact]
    public async Task UpdateLimits_MonthlyLessThanDaily_Throws()
    {
        var (db, userId, accountId, cardId, _, _, _) = await Setup();
        var controller = CreateController(db, userId);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            controller.UpdateCardLimits(accountId, cardId,
                new UpdateCardLimitsRequest { DailyLimit = 500m, MonthlyLimit = 200m }));
    }

    [Fact]
    public async Task UpdateLimits_PartialUpdate_NewDailyExceedsExistingMonthly_Throws()
    {
        var (db, userId, accountId, cardId, _, _, _) = await Setup(dailyLimit: 50m, monthlyLimit: 200m);
        var controller = CreateController(db, userId);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            controller.UpdateCardLimits(accountId, cardId,
                new UpdateCardLimitsRequest { DailyLimit = 500m }));
    }

    [Fact]
    public async Task UpdateLimits_NoLimitsProvided_Throws()
    {
        var (db, userId, accountId, cardId, _, _, _) = await Setup();
        var controller = CreateController(db, userId);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            controller.UpdateCardLimits(accountId, cardId,
                new UpdateCardLimitsRequest()));
    }

    [Fact]
    public async Task UpdateLimits_OtherUsersAccount_Throws()
    {
        var (db, _, accountId, cardId, otherUserId, _, _) = await Setup();
        var controller = CreateController(db, otherUserId);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.UpdateCardLimits(accountId, cardId,
                new UpdateCardLimitsRequest { DailyLimit = 100m, MonthlyLimit = 500m }));
    }

    [Fact]
    public async Task UpdateLimits_CardNotFound_Throws()
    {
        var (db, userId, accountId, _, _, _, _) = await Setup();
        var controller = CreateController(db, userId);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            controller.UpdateCardLimits(accountId, Guid.NewGuid(),
                new UpdateCardLimitsRequest { DailyLimit = 100m, MonthlyLimit = 500m }));
    }

    [Fact]
    public async Task UpdateLimits_JuniorCard_ByParent_Returns200()
    {
        var (db, parentUserId, _, _, _, juniorAccountId, _) = await Setup();

        var juniorCard = new Card
        {
            Id = Guid.NewGuid(), AccountId = juniorAccountId, Last4 = "5678",
            Type = "prepaid", Status = "active", DailyLimit = 50m, MonthlyLimit = 200m,
            ExpiresAt = DateTime.UtcNow.AddYears(3), CreatedAt = DateTime.UtcNow
        };
        db.Cards.Add(juniorCard);
        await db.SaveChangesAsync();

        var controller = CreateController(db, parentUserId);

        var result = await controller.UpdateCardLimits(juniorAccountId, juniorCard.Id,
            new UpdateCardLimitsRequest { DailyLimit = 100m, MonthlyLimit = 500m });

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<CardResponse>(ok.Value);
        Assert.Equal(100m, response.DailyLimit);
        Assert.Equal(500m, response.MonthlyLimit);
    }

    [Fact]
    public async Task UpdateLimits_JuniorCard_ByJunior_Throws()
    {
        var (db, _, _, _, _, juniorAccountId, juniorUserId) = await Setup();

        var juniorCard = new Card
        {
            Id = Guid.NewGuid(), AccountId = juniorAccountId, Last4 = "5678",
            Type = "prepaid", Status = "active", DailyLimit = 50m, MonthlyLimit = 200m,
            ExpiresAt = DateTime.UtcNow.AddYears(3), CreatedAt = DateTime.UtcNow
        };
        db.Cards.Add(juniorCard);
        await db.SaveChangesAsync();

        var controller = CreateController(db, juniorUserId);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.UpdateCardLimits(juniorAccountId, juniorCard.Id,
                new UpdateCardLimitsRequest { DailyLimit = 100m, MonthlyLimit = 500m }));
    }
}
