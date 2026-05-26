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
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Api.Services;
using UsBankSystem.Core.Entities;
using UsBankSystem.Infrastructure.Persistence;
using UsBankSystem.Tests.Helpers;

namespace UsBankSystem.Tests.Cards;

public class GetCardsTests
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
        new(new HttpClient(new MockHttpMessageHandler(HttpStatusCode.OK, "{\"cardToken\":\"tok_test\"}")) { BaseAddress = new Uri("http://localhost:6005") },
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
    public async Task GetCards_EmptyAccount_ReturnsEmptyList()
    {
        var (db, userId, accountId, _) = await Setup();
        var controller = CreateController(db, userId);

        var result = await controller.GetCards(accountId);

        var ok = Assert.IsType<OkObjectResult>(result);
        var cards = Assert.IsType<List<CardResponse>>(ok.Value);
        Assert.Empty(cards);
    }

    [Fact]
    public async Task GetCards_WithCards_ReturnsAll()
    {
        var (db, userId, accountId, _) = await Setup();
        db.Cards.AddRange(
            new Card { Id = Guid.NewGuid(), AccountId = accountId, Last4 = "1111", Type = "debit", Status = "active", ExpiresAt = DateTime.UtcNow.AddYears(3), CreatedAt = DateTime.UtcNow },
            new Card { Id = Guid.NewGuid(), AccountId = accountId, Last4 = "2222", Type = "prepaid", Status = "active", ExpiresAt = DateTime.UtcNow.AddYears(2), CreatedAt = DateTime.UtcNow }
        );
        await db.SaveChangesAsync();
        var controller = CreateController(db, userId);

        var result = await controller.GetCards(accountId);

        var ok = Assert.IsType<OkObjectResult>(result);
        var cards = Assert.IsType<List<CardResponse>>(ok.Value);
        Assert.Equal(2, cards.Count);
    }

    [Fact]
    public async Task GetCards_AccountNotFound_Throws()
    {
        var (db, userId, _, _) = await Setup();
        var controller = CreateController(db, userId);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            controller.GetCards(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetCards_OtherUsersAccount_Throws()
    {
        var (db, _, accountId, otherUserId) = await Setup();
        var controller = CreateController(db, otherUserId);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.GetCards(accountId));
    }
}
