using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UsBankSystem.Api.Controllers;
using UsBankSystem.Api.Models.Auth;
using UsBankSystem.Infrastructure.Persistence;

namespace UsBankSystem.Tests.Auth;

public class RegisterTests
{
    private AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task Register_ValidRequest_Returns201()
    {
        var db = CreateDb();
        var controller = new AuthController(db);

        var result = await controller.Register(new RegisterRequest
        {
            Email = "test@example.com",
            Password = "Password123!",
            FirstName = "Jan",
            LastName = "Kowalski"
        });

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, status.StatusCode);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409()
    {
        var db = CreateDb();
        var controller = new AuthController(db);
        var request = new RegisterRequest
        {
            Email = "duplicate@example.com",
            Password = "Password123!",
            FirstName = "Jan",
            LastName = "Kowalski"
        };

        await controller.Register(request);
        var result = await controller.Register(request);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task Register_EmailStoredAsLowercase()
    {
        var db = CreateDb();
        var controller = new AuthController(db);

        await controller.Register(new RegisterRequest
        {
            Email = "TEST@EXAMPLE.COM",
            Password = "Password123!",
            FirstName = "Jan",
            LastName = "Kowalski"
        });

        var user = await db.Users.FirstAsync();
        Assert.Equal("test@example.com", user.Email);
    }

    [Fact]
    public async Task Register_PasswordIsHashed()
    {
        var db = CreateDb();
        var controller = new AuthController(db);

        await controller.Register(new RegisterRequest
        {
            Email = "test@example.com",
            Password = "Password123!",
            FirstName = "Jan",
            LastName = "Kowalski"
        });

        var user = await db.Users.FirstAsync();
        Assert.NotEqual("Password123!", user.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify("Password123!", user.PasswordHash));
    }

    [Fact]
    public async Task Register_UserSavedToDatabase()
    {
        var db = CreateDb();
        var controller = new AuthController(db);

        await controller.Register(new RegisterRequest
        {
            Email = "test@example.com",
            Password = "Password123!",
            FirstName = "Jan",
            LastName = "Kowalski"
        });

        var user = await db.Users.FirstAsync();
        Assert.Equal("Jan", user.FirstName);
        Assert.Equal("Kowalski", user.LastName);
        Assert.Equal("active", user.Status);
    }
}
