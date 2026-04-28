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
using UsBankSystem.Core.Domain.Common;
using UsBankSystem.Infrastructure.Persistence;

namespace UsBankSystem.Tests.Accounts;

public class GetBalanceTests
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

    private async Task<(AppDbContext db, Guid userId, Guid accountId)> Setup()
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

    [Fact]
    public async Task GetBalance_ExistingAccount_Returns200()
    {
        var (db, userId, accountId) = await Setup();
        var controller = CreateController(db, userId);
        var result = await controller.GetBalance(accountId);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
    }

    [Fact]
    public async Task GetBalance_ReturnsCorrectData()
    {
        var (db, userId, accountId) = await Setup();
        var controller = CreateController(db, userId);
        var result = await controller.GetBalance(accountId);
        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<BalanceResponse>(ok.Value);
        Assert.Equal(accountId, response.AccountId);
        Assert.Equal(0, response.Balance);
        Assert.Equal(0, response.ReservedBalance);
        Assert.Equal(0, response.AvailableBalance);
        Assert.Equal(CurrencyCode.USD, response.Currency);
    }

    [Fact]
    public async Task GetBalance_NotFound_Returns404()
    {
        var (db, userId, _) = await Setup();
        var controller = CreateController(db, userId);
        var result = await controller.GetBalance(Guid.NewGuid());
        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(404, notFound.StatusCode);
    }

    [Fact]
    public async Task GetBalance_OtherUsersAccount_Returns403()
    {
        var (db, _, accountId) = await Setup();
        var controller = CreateController(db, Guid.NewGuid());
        var result = await controller.GetBalance(accountId);
        Assert.IsType<ForbidResult>(result);
    }
}