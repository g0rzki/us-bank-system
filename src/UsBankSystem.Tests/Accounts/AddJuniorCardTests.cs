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

public class AddJuniorCardTests
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
        var controller = new AccountsController(new AccountService(db));
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

        await db.SaveChangesAsync();
        return (db, parentUser.Id, juniorAccount.Id, otherUser.Id);
    }

    private static AddJuniorCardRequest ValidRequest() => new()
    {
        Last4 = "9001",
        ExpiresAt = DateTime.UtcNow.AddYears(3),
        DailyLimit = 50m,
        MonthlyLimit = 300m
    };

    [Fact]
    public async Task AddJuniorCard_ValidRequest_Returns201()
    {
        var (db, parentUserId, juniorAccountId, _) = await Setup();
        var controller = CreateController(db, parentUserId);
        var result = await controller.AddJuniorCard(juniorAccountId, ValidRequest());
        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
    }

    [Fact]
    public async Task AddJuniorCard_ReturnsCorrectData()
    {
        var (db, parentUserId, juniorAccountId, _) = await Setup();
        var controller = CreateController(db, parentUserId);
        var result = await controller.AddJuniorCard(juniorAccountId, ValidRequest());
        var created = Assert.IsType<ObjectResult>(result);
        var response = Assert.IsType<CardResponse>(created.Value);
        Assert.Equal("prepaid", response.Type);
        Assert.Equal("active", response.Status);
        Assert.Equal(50m, response.DailyLimit);
        Assert.Equal(300m, response.MonthlyLimit);
    }

    [Fact]
    public async Task AddJuniorCard_SavedToDatabase()
    {
        var (db, parentUserId, juniorAccountId, _) = await Setup();
        var controller = CreateController(db, parentUserId);
        await controller.AddJuniorCard(juniorAccountId, ValidRequest());
        var card = await db.Cards.FirstAsync();
        Assert.Equal("prepaid", card.Type);
        Assert.Equal(juniorAccountId, card.AccountId);
    }

    [Fact]
    public async Task AddJuniorCard_DuplicateCard_Throws()
    {
        var (db, parentUserId, juniorAccountId, _) = await Setup();
        var controller = CreateController(db, parentUserId);
        await controller.AddJuniorCard(juniorAccountId, ValidRequest());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.AddJuniorCard(juniorAccountId, ValidRequest()));
    }

    [Fact]
    public async Task AddJuniorCard_NotFound_Throws()
    {
        var (db, parentUserId, _, _) = await Setup();
        var controller = CreateController(db, parentUserId);
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            controller.AddJuniorCard(Guid.NewGuid(), ValidRequest()));
    }

    [Fact]
    public async Task AddJuniorCard_OtherUser_Throws()
    {
        var (db, _, juniorAccountId, otherUserId) = await Setup();
        var controller = CreateController(db, otherUserId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.AddJuniorCard(juniorAccountId, ValidRequest()));
    }
}