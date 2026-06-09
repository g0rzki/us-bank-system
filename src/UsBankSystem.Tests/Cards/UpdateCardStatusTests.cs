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

public class UpdateCardStatusTests
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
        new(new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "{\"success\":true}")) { BaseAddress = new Uri("http://localhost:6005") },
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

    private async Task<(AppDbContext db, Guid userId, Guid accountId, Guid cardId, Guid otherUserId)> Setup(
        string cardStatus = "active", DateTime? blockedAt = null)
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

        var card = new Card
        {
            Id = Guid.NewGuid(), AccountId = account.Id, Last4 = "1234",
            Type = "debit", Status = cardStatus, BlockedAt = blockedAt,
            ExpiresAt = DateTime.UtcNow.AddYears(3), CreatedAt = DateTime.UtcNow
        };
        db.Cards.Add(card);
        await db.SaveChangesAsync();

        return (db, user.Id, account.Id, card.Id, other.Id);
    }

    [Fact]
    public async Task UpdateStatus_ActiveToBlocked_Returns200()
    {
        var (db, userId, accountId, cardId, _) = await Setup();
        var controller = CreateController(db, userId);

        var result = await controller.UpdateCardStatus(accountId, cardId, new UpdateCardStatusRequest { Status = "blocked" });

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<CardResponse>(ok.Value);
        Assert.Equal("blocked", response.Status);
    }

    [Fact]
    public async Task UpdateStatus_Block_SetsBlockedAt()
    {
        var (db, userId, accountId, cardId, _) = await Setup();
        var controller = CreateController(db, userId);

        await controller.UpdateCardStatus(accountId, cardId, new UpdateCardStatusRequest { Status = "blocked" });

        var card = await db.Cards.FirstAsync();
        Assert.NotNull(card.BlockedAt);
        Assert.True(card.BlockedAt > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task UpdateStatus_Unblock_AfterCooldown_Returns200()
    {
        var (db, userId, accountId, cardId, _) = await Setup("blocked", DateTime.UtcNow.AddHours(-25));
        var controller = CreateController(db, userId);

        var result = await controller.UpdateCardStatus(accountId, cardId, new UpdateCardStatusRequest { Status = "active" });

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<CardResponse>(ok.Value);
        Assert.Equal("active", response.Status);
        Assert.Null(response.BlockedAt);
    }

    [Fact]
    public async Task UpdateStatus_Unblock_BeforeCooldown_Throws()
    {
        var (db, userId, accountId, cardId, _) = await Setup("blocked", DateTime.UtcNow.AddHours(-2));
        var controller = CreateController(db, userId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.UpdateCardStatus(accountId, cardId, new UpdateCardStatusRequest { Status = "active" }));
    }

    [Fact]
    public async Task UpdateStatus_ExpiredCard_Throws()
    {
        var (db, userId, accountId, cardId, _) = await Setup("expired");
        var controller = CreateController(db, userId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.UpdateCardStatus(accountId, cardId, new UpdateCardStatusRequest { Status = "active" }));
    }

    [Fact]
    public async Task UpdateStatus_InvalidStatus_Throws()
    {
        var (db, userId, accountId, cardId, _) = await Setup();
        var controller = CreateController(db, userId);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            controller.UpdateCardStatus(accountId, cardId, new UpdateCardStatusRequest { Status = "expired" }));
    }

    [Fact]
    public async Task UpdateStatus_CardNotFound_Throws()
    {
        var (db, userId, accountId, _, _) = await Setup();
        var controller = CreateController(db, userId);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            controller.UpdateCardStatus(accountId, Guid.NewGuid(), new UpdateCardStatusRequest { Status = "blocked" }));
    }

    [Fact]
    public async Task UpdateStatus_OtherUsersAccount_Throws()
    {
        var (db, _, accountId, cardId, otherUserId) = await Setup();
        var controller = CreateController(db, otherUserId);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.UpdateCardStatus(accountId, cardId, new UpdateCardStatusRequest { Status = "blocked" }));
    }
}
