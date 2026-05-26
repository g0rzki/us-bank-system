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

public class RegisterCardTests
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

    private CardsGateway CreateGateway(HttpStatusCode status = HttpStatusCode.OK, string body = "{\"cardToken\":\"tok_test\"}") =>
        new(new HttpClient(new MockHttpMessageHandler(status, body)),
            NullLogger<CardsGateway>.Instance);

    private CardsController CreateController(AppDbContext db, Guid userId, CardsGateway? gateway = null)
    {
        var service = new CardService(db, gateway ?? CreateGateway(), NullLogger<CardService>.Instance);
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

    private async Task<(AppDbContext db, Guid userId, Guid accountId, Guid otherUserId)> Setup()
    {
        var db = CreateDb();
        var authService = new AuthService(db, CreateConfig());

        await authService.RegisterAsync(new RegisterRequest
        {
            Email = "user@example.com", Password = "Password123!", FirstName = "Jan", LastName = "Kowalski"
        });
        await authService.RegisterAsync(new RegisterRequest
        {
            Email = "other@example.com", Password = "Password123!", FirstName = "Other", LastName = "User"
        });

        var users = await db.Users.ToListAsync();
        var user = users.First(u => u.Email == "user@example.com");
        var other = users.First(u => u.Email == "other@example.com");

        var account = new Account
        {
            Id = Guid.NewGuid(), UserId = user.Id, AccountNumber = "1000000001",
            Type = "checking", Balance = 1000m, ReservedBalance = 0,
            Currency = "USD", Status = "active", CreatedAt = DateTime.UtcNow
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        return (db, user.Id, account.Id, other.Id);
    }

    [Fact]
    public async Task RegisterCard_Debit_Returns201()
    {
        var (db, userId, accountId, _) = await Setup();
        var controller = CreateController(db, userId);

        var result = await controller.RegisterCard(accountId, new RegisterCardRequest { Type = "debit" });

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(201, created.StatusCode);
    }

    [Fact]
    public async Task RegisterCard_ReturnsCorrectData()
    {
        var (db, userId, accountId, _) = await Setup();
        var controller = CreateController(db, userId);

        var result = await controller.RegisterCard(accountId, new RegisterCardRequest { Type = "debit", DailyLimit = 500m, MonthlyLimit = 2000m });

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var response = Assert.IsType<CardResponse>(created.Value);
        Assert.Equal("debit", response.Type);
        Assert.Equal("active", response.Status);
        Assert.Equal(500m, response.DailyLimit);
        Assert.Equal(2000m, response.MonthlyLimit);
        Assert.Equal(4, response.Last4.Length);
        Assert.True(response.ExpiresAt > DateTime.UtcNow.AddYears(4));
    }

    [Fact]
    public async Task RegisterCard_SavedToDatabase()
    {
        var (db, userId, accountId, _) = await Setup();
        var controller = CreateController(db, userId);

        await controller.RegisterCard(accountId, new RegisterCardRequest { Type = "prepaid" });

        var card = await db.Cards.FirstAsync();
        Assert.Equal(accountId, card.AccountId);
        Assert.Equal("prepaid", card.Type);
        Assert.Equal("active", card.Status);
        Assert.Equal("tok_test", card.ExternalCardToken);
    }

    [Fact]
    public async Task RegisterCard_GeneratesLast4Digits()
    {
        var (db, userId, accountId, _) = await Setup();
        var controller = CreateController(db, userId);

        await controller.RegisterCard(accountId, new RegisterCardRequest { Type = "debit" });

        var card = await db.Cards.FirstAsync();
        Assert.Equal(4, card.Last4.Length);
        Assert.True(card.Last4.All(char.IsDigit));
    }

    [Fact]
    public async Task RegisterCard_ExpiresInFiveYears()
    {
        var (db, userId, accountId, _) = await Setup();
        var controller = CreateController(db, userId);

        await controller.RegisterCard(accountId, new RegisterCardRequest { Type = "debit" });

        var card = await db.Cards.FirstAsync();
        Assert.True(card.ExpiresAt > DateTime.UtcNow.AddYears(4).AddMonths(11));
        Assert.True(card.ExpiresAt < DateTime.UtcNow.AddYears(5).AddDays(1));
    }

    [Fact]
    public async Task RegisterCard_InvalidType_Throws()
    {
        var (db, userId, accountId, _) = await Setup();
        var controller = CreateController(db, userId);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            controller.RegisterCard(accountId, new RegisterCardRequest { Type = "credit" }));
    }

    [Fact]
    public async Task RegisterCard_DuplicateActiveCard_Throws()
    {
        var (db, userId, accountId, _) = await Setup();
        var controller = CreateController(db, userId);

        await controller.RegisterCard(accountId, new RegisterCardRequest { Type = "debit" });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.RegisterCard(accountId, new RegisterCardRequest { Type = "debit" }));
    }

    [Fact]
    public async Task RegisterCard_DifferentTypes_BothAllowed()
    {
        var (db, userId, accountId, _) = await Setup();
        var controller = CreateController(db, userId);

        await controller.RegisterCard(accountId, new RegisterCardRequest { Type = "debit" });
        await controller.RegisterCard(accountId, new RegisterCardRequest { Type = "prepaid" });

        Assert.Equal(2, await db.Cards.CountAsync());
    }

    [Fact]
    public async Task RegisterCard_AccountNotFound_Throws()
    {
        var (db, userId, _, _) = await Setup();
        var controller = CreateController(db, userId);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            controller.RegisterCard(Guid.NewGuid(), new RegisterCardRequest { Type = "debit" }));
    }

    [Fact]
    public async Task RegisterCard_OtherUsersAccount_Throws()
    {
        var (db, _, accountId, otherUserId) = await Setup();
        var controller = CreateController(db, otherUserId);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.RegisterCard(accountId, new RegisterCardRequest { Type = "debit" }));
    }

    [Fact]
    public async Task RegisterCard_GatewayFailure_StillSavesCard()
    {
        var (db, userId, accountId, _) = await Setup();
        var failingGateway = CreateGateway(HttpStatusCode.InternalServerError, "error");
        var controller = CreateController(db, userId, failingGateway);

        await controller.RegisterCard(accountId, new RegisterCardRequest { Type = "debit" });

        var card = await db.Cards.FirstAsync();
        Assert.Null(card.ExternalCardToken);
        Assert.Equal("active", card.Status);
    }
}
