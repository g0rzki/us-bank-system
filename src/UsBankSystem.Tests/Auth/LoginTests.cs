using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using UsBankSystem.Api.Controllers;
using UsBankSystem.Api.Models.Auth;
using UsBankSystem.Api.Services;
using UsBankSystem.Infrastructure.Persistence;

namespace UsBankSystem.Tests.Auth;

public class LoginTests
{
    private AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private IConfiguration CreateConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "test_secret_minimum_32_characters_required!"
            })
            .Build();

    private AuthController CreateController(AppDbContext db) =>
        new(new AuthService(db, CreateConfig()));

    private async Task<AuthController> CreateControllerWithUser(AppDbContext db, string email = "test@example.com", string password = "Password123!")
    {
        var controller = CreateController(db);
        await controller.Register(new RegisterRequest
        {
            Email = email,
            Password = password,
            FirstName = "Jan",
            LastName = "Kowalski"
        });
        return controller;
    }

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithToken()
    {
        var db = CreateDb();
        var controller = await CreateControllerWithUser(db);

        var result = await controller.Login(new LoginRequest
        {
            Email = "test@example.com",
            Password = "Password123!"
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var db = CreateDb();
        var controller = await CreateControllerWithUser(db);

        var result = await controller.Login(new LoginRequest
        {
            Email = "test@example.com",
            Password = "WrongPassword!"
        });

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Login_NonExistentEmail_Returns401()
    {
        var db = CreateDb();
        var controller = await CreateControllerWithUser(db);

        var result = await controller.Login(new LoginRequest
        {
            Email = "nieistnieje@example.com",
            Password = "Password123!"
        });

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Login_EmailCaseInsensitive_Returns200()
    {
        var db = CreateDb();
        var controller = await CreateControllerWithUser(db);

        var result = await controller.Login(new LoginRequest
        {
            Email = "TEST@EXAMPLE.COM",
            Password = "Password123!"
        });

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Login_InactiveUser_Returns401()
    {
        var db = CreateDb();
        var controller = await CreateControllerWithUser(db);

        var user = await db.Users.FirstAsync();
        user.Status = "inactive";
        await db.SaveChangesAsync();

        var result = await controller.Login(new LoginRequest
        {
            Email = "test@example.com",
            Password = "Password123!"
        });

        Assert.IsType<UnauthorizedObjectResult>(result);
    }
}
