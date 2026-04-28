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
using UsBankSystem.Core.Domain.Transactions;
using UsBankSystem.Core.Entities;
using UsBankSystem.Infrastructure.Persistence;

namespace UsBankSystem.Tests.Accounts;

public class GetTransactionsTests
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

    private static void AddTransactions(AppDbContext db, Guid accountId, int count)
    {
        var transactions = Enumerable.Range(1, count).Select(i => new Transaction
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Amount = i * 10m,
            Type = TransactionType.Credit,
            Status = TransactionStatus.Completed,
            Description = $"Transaction {i}",
            CreatedAt = DateTime.UtcNow.AddMinutes(-i)
        });
        db.Transactions.AddRange(transactions);
        db.SaveChanges();
    }

    [Fact]
    public async Task GetTransactions_EmptyAccount_Returns200WithEmptyList()
    {
        var (db, userId, accountId) = await Setup();
        var controller = CreateController(db, userId);
        var result = await controller.GetTransactions(accountId);
        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<PagedResponse<TransactionResponse>>(ok.Value);
        Assert.Empty(response.Items);
        Assert.Equal(0, response.Total);
    }

    [Fact]
    public async Task GetTransactions_ReturnsCorrectCount()
    {
        var (db, userId, accountId) = await Setup();
        AddTransactions(db, accountId, 5);
        var controller = CreateController(db, userId);
        var result = await controller.GetTransactions(accountId);
        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<PagedResponse<TransactionResponse>>(ok.Value);
        Assert.Equal(5, response.Total);
        Assert.Equal(5, response.Items.Count);
    }

    [Fact]
    public async Task GetTransactions_Pagination_ReturnsCorrectPage()
    {
        var (db, userId, accountId) = await Setup();
        AddTransactions(db, accountId, 25);
        var controller = CreateController(db, userId);
        var result = await controller.GetTransactions(accountId, page: 2, pageSize: 10);
        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<PagedResponse<TransactionResponse>>(ok.Value);
        Assert.Equal(25, response.Total);
        Assert.Equal(10, response.Items.Count);
        Assert.Equal(2, response.Page);
        Assert.Equal(3, response.TotalPages);
    }

    [Fact]
    public async Task GetTransactions_OrderedByDateDescending()
    {
        var (db, userId, accountId) = await Setup();
        AddTransactions(db, accountId, 3);
        var controller = CreateController(db, userId);
        var result = await controller.GetTransactions(accountId);
        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<PagedResponse<TransactionResponse>>(ok.Value);
        var dates = response.Items.Select(t => t.CreatedAt).ToList();
        Assert.Equal(dates.OrderByDescending(d => d).ToList(), dates);
    }

    [Fact]
    public async Task GetTransactions_NotFound_Returns404()
    {
        var (db, userId, _) = await Setup();
        var controller = CreateController(db, userId);
        var result = await controller.GetTransactions(Guid.NewGuid());
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetTransactions_OtherUsersAccount_Returns403()
    {
        var (db, _, accountId) = await Setup();
        var controller = CreateController(db, Guid.NewGuid());
        var result = await controller.GetTransactions(accountId);
        Assert.IsType<ForbidResult>(result);
    }
}