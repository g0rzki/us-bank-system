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
using UsBankSystem.Infrastructure.Persistence;

namespace UsBankSystem.Tests.Accounts;

public class CreateJuniorAccountTests
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

    private async Task<(AppDbContext db, Guid userId, Guid parentAccountId)> Setup()
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
        var controller = CreateController(db, user.Id);
        await controller.Create(new CreateAccountRequest { Type = "checking" });
        var account = await db.Accounts.FirstAsync();
        return (db, user.Id, account.Id);
    }

    private static DateOnly ValidDateOfBirth() =>
        DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-10));

    [Fact]
    public async Task CreateJunior_ValidRequest_Returns201()
    {
        var (db, userId, parentAccountId) = await Setup();
        var controller = CreateController(db, userId);
        var result = await controller.CreateJunior(new CreateJuniorAccountRequest
        {
            ParentAccountId = parentAccountId,
            DateOfBirth = ValidDateOfBirth()
        });
        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
    }

    [Fact]
    public async Task CreateJunior_ReturnsCorrectData()
    {
        var (db, userId, parentAccountId) = await Setup();
        var controller = CreateController(db, userId);
        var dob = ValidDateOfBirth();
        var result = await controller.CreateJunior(new CreateJuniorAccountRequest
        {
            ParentAccountId = parentAccountId,
            DateOfBirth = dob
        });
        var created = Assert.IsType<ObjectResult>(result);
        var response = Assert.IsType<JuniorAccountResponse>(created.Value);
        Assert.Equal(dob, response.DateOfBirth);
        Assert.Equal(0, response.Balance);
        Assert.Null(response.CardDailyLimit);
    }

    [Fact]
    public async Task CreateJunior_SavedToDatabase()
    {
        var (db, userId, parentAccountId) = await Setup();
        var controller = CreateController(db, userId);
        await controller.CreateJunior(new CreateJuniorAccountRequest
        {
            ParentAccountId = parentAccountId,
            DateOfBirth = ValidDateOfBirth()
        });
        Assert.Equal(1, await db.JuniorAccounts.CountAsync());
        Assert.Equal(2, await db.Accounts.CountAsync());
    }

    [Fact]
    public async Task CreateJunior_TooYoung_Throws()
    {
        var (db, userId, parentAccountId) = await Setup();
        var controller = CreateController(db, userId);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            controller.CreateJunior(new CreateJuniorAccountRequest
            {
                ParentAccountId = parentAccountId,
                DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-5))
            }));
    }

    [Fact]
    public async Task CreateJunior_TooOld_Throws()
    {
        var (db, userId, parentAccountId) = await Setup();
        var controller = CreateController(db, userId);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            controller.CreateJunior(new CreateJuniorAccountRequest
            {
                ParentAccountId = parentAccountId,
                DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-15))
            }));
    }

    [Fact]
    public async Task CreateJunior_FutureDateOfBirth_Throws()
    {
        var (db, userId, parentAccountId) = await Setup();
        var controller = CreateController(db, userId);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            controller.CreateJunior(new CreateJuniorAccountRequest
            {
                ParentAccountId = parentAccountId,
                DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))
            }));
    }

    [Fact]
    public async Task CreateJunior_ParentAccountNotFound_Throws()
    {
        var (db, userId, _) = await Setup();
        var controller = CreateController(db, userId);
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            controller.CreateJunior(new CreateJuniorAccountRequest
            {
                ParentAccountId = Guid.NewGuid(),
                DateOfBirth = ValidDateOfBirth()
            }));
    }

    [Fact]
    public async Task CreateJunior_OtherUsersParentAccount_Throws()
    {
        var (db, _, parentAccountId) = await Setup();
        var otherUserId = Guid.NewGuid();
        var controller = CreateController(db, otherUserId);
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            controller.CreateJunior(new CreateJuniorAccountRequest
            {
                ParentAccountId = parentAccountId,
                DateOfBirth = ValidDateOfBirth()
            }));
    }
}