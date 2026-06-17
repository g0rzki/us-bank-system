using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using UsBankSystem.Api.Controllers;
using UsBankSystem.Api.Integrations;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Services;
using UsBankSystem.Core.Domain.Blik;
using UsBankSystem.Core.Entities;
using UsBankSystem.Infrastructure.Persistence;

namespace UsBankSystem.Tests.Blik;

public class KlikWebhookSecurityTests
{
    private AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private KlikWebhookController CreateController(
        AppDbContext db,
        Dictionary<string, string?> configValues,
        Action<HttpContext>? configureHttp = null)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();
        var blikService = new BlikService(db, new FakeKlikApiClient(), NullLogger<BlikService>.Instance);
        var controller = new KlikWebhookController(blikService, cfg, NullLogger<KlikWebhookController>.Instance);

        var httpContext = new DefaultHttpContext();
        configureHttp?.Invoke(httpContext);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        return controller;
    }

    private async Task<(AppDbContext db, Guid userId)> SetupUserWithAccount()
    {
        var db = CreateDb();
        var user = new User
        {
            Id = Guid.NewGuid(), Email = "test@example.com", PasswordHash = "hash",
            FirstName = "Test", LastName = "User", Status = "active", CreatedAt = DateTime.UtcNow
        };
        var account = new Account
        {
            Id = Guid.NewGuid(), UserId = user.Id, AccountNumber = "1000000001",
            Type = "checking", Balance = 500m, ReservedBalance = 0,
            Currency = "USD", Status = "active", CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        db.Accounts.Add(account);
        db.BlikCodes.Add(new BlikCode
        {
            Id = Guid.NewGuid(), UserId = user.Id, AccountId = account.Id,
            Code = "123456", Status = BlikCodeStatus.Active,
            ExpiresAt = DateTime.UtcNow.AddSeconds(90), CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return (db, user.Id);
    }

    private KlikWebhookAuthorizeRequest MakePayload(Guid userId) => new()
    {
        TransactionId = Guid.NewGuid().ToString(),
        UserId = userId.ToString(),
        Amount = 50m,
        Currency = "USD",
        MerchantName = "Test Shop",
        IsOnUs = false,
        ExpiryTime = DateTime.UtcNow.AddSeconds(60),
        Zone = "US"
    };

    [Fact]
    public async Task NoSecret_NoFlag_ReturnsUnauthorized()
    {
        var (db, userId) = await SetupUserWithAccount();
        var controller = CreateController(db, new Dictionary<string, string?>());

        var result = await controller.Authorize(MakePayload(userId));

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task NoSecret_FlagTrue_ReturnsOk()
    {
        var (db, userId) = await SetupUserWithAccount();
        var controller = CreateController(db, new Dictionary<string, string?>
        {
            ["Klik:AllowUnsignedWebhooks"] = "true"
        });

        var result = await controller.Authorize(MakePayload(userId));

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task SecretConfigured_ValidHeader_ReturnsOk()
    {
        var (db, userId) = await SetupUserWithAccount();
        var controller = CreateController(db,
            new Dictionary<string, string?> { ["Klik:WebhookSecret"] = "test-secret" },
            http => http.Request.Headers["X-Webhook-Secret"] = "test-secret");

        var result = await controller.Authorize(MakePayload(userId));

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task SecretConfigured_WrongHeader_ReturnsUnauthorized()
    {
        var (db, userId) = await SetupUserWithAccount();
        var controller = CreateController(db,
            new Dictionary<string, string?> { ["Klik:WebhookSecret"] = "test-secret" },
            http => http.Request.Headers["X-Webhook-Secret"] = "wrong");

        var result = await controller.Authorize(MakePayload(userId));

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task SecretConfigured_NoHeader_ReturnsUnauthorized()
    {
        var (db, userId) = await SetupUserWithAccount();
        var controller = CreateController(db,
            new Dictionary<string, string?> { ["Klik:WebhookSecret"] = "test-secret" });

        var result = await controller.Authorize(MakePayload(userId));

        Assert.IsType<UnauthorizedResult>(result);
    }

    private class FakeKlikApiClient : IKlikApiClient
    {
        public Task<KlikGenerateCodeResult> GenerateCodeAsync(string userId, CancellationToken ct = default)
            => Task.FromResult(new KlikGenerateCodeResult(true, "123456", DateTime.UtcNow.AddSeconds(120), null));

        public Task<KlikConfirmResult> ConfirmPaymentAsync(string transactionId, bool accepted, string? rejectReason, CancellationToken ct = default)
            => Task.FromResult(new KlikConfirmResult(true, null));
    }
}
