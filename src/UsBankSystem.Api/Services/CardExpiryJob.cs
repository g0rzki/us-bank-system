using Microsoft.EntityFrameworkCore;
using UsBankSystem.Core.Domain.Cards;
using UsBankSystem.Infrastructure.Persistence;

namespace UsBankSystem.Api.Services;

public class CardExpiryJob(IServiceScopeFactory scopeFactory, ILogger<CardExpiryJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ExpireCardsAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task ExpireCardsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var expired = await db.Cards
            .Where(c => c.Status != CardStatus.Expired && c.ExpiresAt < DateTime.UtcNow)
            .ToListAsync(ct);

        if (expired.Count == 0) return;

        foreach (var card in expired)
            card.Status = CardStatus.Expired;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Expired {Count} card(s)", expired.Count);
    }
}
