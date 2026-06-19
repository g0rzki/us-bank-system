using Microsoft.EntityFrameworkCore;
using Npgsql;
using UsBankSystem.Core.Domain.Transactions;
using UsBankSystem.Core.Entities;
using UsBankSystem.Infrastructure.Persistence;

namespace UsBankSystem.Api.Services.Polling;

/// <summary>
/// Credits customer accounts for incoming external transfers (ACH, FedNow, etc.).
/// Shared between all settlement rails — each rail passes parsed entry data.
/// </summary>
public class IncomingTransferProcessor(IServiceScopeFactory scopeFactory, ILogger<IncomingTransferProcessor> logger)
{
    public record IncomingEntry(string AccountNumber, decimal Amount, string Description, string ExternalRef);

    public async Task ProcessAsync(IEnumerable<IncomingEntry> entries, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var entry in entries)
        {
            try
            {
                await CreditAccountAsync(db, entry, ct);
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
            {
                // Unique constraint violation on IX_Transactions_ReferenceId_AccountId:
                // another pod already credited this entry — this is expected under concurrent polling.
                logger.LogDebug("Incoming transfer {ExternalRef} already credited (duplicate blocked by DB constraint)", entry.ExternalRef);
                db.ChangeTracker.Clear();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to credit account for incoming entry {ExternalRef}, skipping", entry.ExternalRef);
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task CreditAccountAsync(AppDbContext db, IncomingEntry entry, CancellationToken ct)
    {
        var account = await db.Accounts
            .FirstOrDefaultAsync(a => a.AccountNumber == entry.AccountNumber, ct);

        if (account is null)
        {
            logger.LogWarning("Incoming transfer: account {AccountNumber} not found, skipping", entry.AccountNumber);
            return;
        }

        var alreadyProcessed = await db.Transactions
            .AnyAsync(t => t.ReferenceId == entry.ExternalRef && t.AccountId == account.Id, ct);

        if (alreadyProcessed)
        {
            logger.LogDebug("Incoming transfer {ExternalRef} already credited to {AccountNumber}, skipping", entry.ExternalRef, entry.AccountNumber);
            return;
        }

        account.Balance += entry.Amount;
        db.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Amount = entry.Amount,
            Type = TransactionType.Credit,
            Status = TransactionStatus.Completed,
            Description = entry.Description,
            ReferenceId = entry.ExternalRef,
            CreatedAt = DateTime.UtcNow
        });

        logger.LogInformation("Credited {Amount} to account {AccountNumber} (ref: {ExternalRef})", entry.Amount, entry.AccountNumber, entry.ExternalRef);
    }
}
