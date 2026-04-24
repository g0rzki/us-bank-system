using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using UsBankSystem.Api.Controllers;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Services;
using UsBankSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace UsBankSystem.Tests.Accounts;

public class CreateAccountTests
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

    private async Task<(AccountsController controller, AppDbContext db, Guid userId)> Setup()
    {
        var db = CreateDb();
        var authService = new AuthService(db, CreateConfig());
        await authService.RegisterAsync(new UsBankSystem.Api.Models.Auth.RegisterRequest
        {
            Email = "test@example.com",
            Password = "Password123!",
            FirstName = "Jan",
            LastName = "Kowalski"
        });
        var user = await db.Users.FirstAsync();

        var controller = new AccountsController(new AccountService(db));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
                }))
            }
        };

        return (controller, db, user.Id);
    }

    [Fact]
    public async Task CreateAccount_Checking_Returns201()
    {
        var (controller, _, _) = await Setup();
        var result = await controller.Create(new CreateAccountRequest { Type = "checking" });
        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
    }

    [Fact]
    public async Task CreateAccount_Savings_Returns201()
    {
        var (controller, _, _) = await Setup();
        var result = await controller.Create(new CreateAccountRequest { Type = "savings" });
        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
    }

    [Fact]
    public async Task CreateAccount_SavedToDatabase()
    {
        var (controller, db, userId) = await Setup();
        await controller.Create(new CreateAccountRequest { Type = "checking" });
        var account = await db.Accounts.FirstAsync();
        Assert.Equal(userId, account.UserId);
        Assert.Equal("checking", account.Type);
        Assert.Equal("USD", account.Currency);
        Assert.Equal("active", account.Status);
        Assert.Equal(0, account.Balance);
    }

    [Fact]
    public async Task CreateAccount_AccountNumberIsUnique()
    {
        var (controller, db, _) = await Setup();
        await controller.Create(new CreateAccountRequest { Type = "checking" });
        await controller.Create(new CreateAccountRequest { Type = "savings" });
        var numbers = db.Accounts.Select(a => a.AccountNumber).ToList();
        Assert.Equal(2, numbers.Distinct().Count());
    }

    [Fact]
    public async Task CreateAccount_DefaultCurrencyIsUsd()
    {
        var (controller, db, _) = await Setup();
        await controller.Create(new CreateAccountRequest { Type = "checking" });
        var account = await db.Accounts.FirstAsync();
        Assert.Equal("USD", account.Currency);
    }

    [Fact]
    public async Task CreateAccount_InvalidType_Returns400()
    {
        var (controller, _, _) = await Setup();
        var result = await controller.Create(new CreateAccountRequest { Type = "invalid" });
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, bad.StatusCode);
    }

    [Fact]
    public async Task CreateAccount_InvalidCurrency_Returns400()
    {
        var (controller, _, _) = await Setup();
        var result = await controller.Create(new CreateAccountRequest { Type = "checking", Currency = "EUR" });
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, bad.StatusCode);
    }
}
