using Microsoft.EntityFrameworkCore;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Core.Domain.Transactions;
using UsBankSystem.Core.Domain.Transfers;
using UsBankSystem.Core.Entities;
using UsBankSystem.Infrastructure.Persistence;
using Transfer = UsBankSystem.Core.Entities.Transfer;

namespace UsBankSystem.Api.Services.Payments;

public abstract class PaymentServiceBase(AppDbContext db)
{
    protected async Task<TransferResponse> CreatePendingApprovalAsync(
        Account fromAccount, Guid? toAccountId, decimal amount, string currency, string channel, string? description)
    {
        fromAccount.ReservedBalance += amount;

        var transfer = new Transfer
        {
            Id = Guid.NewGuid(),
            FromAccountId = fromAccount.Id,
            ToAccountId = toAccountId,
            Amount = amount,
            Currency = currency.ToUpperInvariant(),
            Channel = channel,
            Status = TransferStatus.PendingApproval,
            Description = description,
            RequiresApproval = true,
            CreatedAt = DateTime.UtcNow
        };

        db.Transfers.Add(transfer);
        await db.SaveChangesAsync();
        return MapToResponse(transfer);
    }

    protected async Task<bool> IsJuniorAccountAsync(Guid accountId) =>
        await db.JuniorAccounts.AnyAsync(j => j.AccountId == accountId);

    protected async Task<decimal?> GetDailyTransferLimitAsync(Guid accountId)
    {
        var card = await db.Cards
            .Where(c => c.AccountId == accountId && c.Type == "prepaid" && c.Status == "active")
            .FirstOrDefaultAsync();
        return card?.DailyLimit;
    }

    protected async Task<decimal> GetTodayTransferTotalAsync(Guid accountId)
    {
        var today = DateTime.UtcNow.Date;
        return await db.Transfers
            .Where(t => t.FromAccountId == accountId && t.CreatedAt >= today
                && t.Status != TransferStatus.Rejected && t.Status != TransferStatus.Failed)
            .SumAsync(t => t.Amount);
    }

    protected async Task<decimal> GetTodayTransferTotalByChannelAsync(Guid accountId, string channel)
    {
        var today = DateTime.UtcNow.Date;
        return await db.Transfers
            .Where(t => t.FromAccountId == accountId && t.Channel == channel && t.CreatedAt >= today
                && t.Status != TransferStatus.Rejected && t.Status != TransferStatus.Failed)
            .SumAsync(t => t.Amount);
    }

    internal static TransferResponse MapToResponse(Transfer transfer) => new()
    {
        Id = transfer.Id,
        FromAccountId = transfer.FromAccountId,
        ToAccountId = transfer.ToAccountId,
        Amount = transfer.Amount,
        Currency = transfer.Currency,
        Channel = transfer.Channel,
        Status = transfer.Status,
        Description = transfer.Description,
        CreatedAt = transfer.CreatedAt,
        CompletedAt = transfer.CompletedAt,
        RequiresApproval = transfer.RequiresApproval
    };

    protected static Transaction CreateTransaction(Guid accountId, decimal amount, string type, string status, string description, Guid referenceTransferId) => new()
    {
        Id = Guid.NewGuid(),
        AccountId = accountId,
        Amount = amount,
        Type = type,
        Status = status,
        Description = description,
        ReferenceId = referenceTransferId.ToString(),
        CreatedAt = DateTime.UtcNow
    };
}
