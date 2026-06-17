using Microsoft.EntityFrameworkCore;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Api.Services.Payments;
using UsBankSystem.Core.Domain.Transactions;
using UsBankSystem.Core.Domain.Transfers;
using UsBankSystem.Core.Entities;
using UsBankSystem.Infrastructure.Persistence;
using Transfer = UsBankSystem.Core.Entities.Transfer;

namespace UsBankSystem.Api.Services;

public class TransferService(AppDbContext db)
{
    public async Task<List<TransferResponse>> GetAllAsync(Guid userId)
    {
        return await db.Transfers
            .Include(t => t.FromAccount)
            .Where(t => t.FromAccount.UserId == userId || db.Accounts.Any(a => a.Id == t.ToAccountId && a.UserId == userId))
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new TransferResponse
            {
                Id = t.Id,
                FromAccountId = t.FromAccountId,
                ToAccountId = t.ToAccountId,
                Amount = t.Amount,
                Currency = t.Currency,
                Channel = t.Channel,
                Status = t.Status,
                Description = t.Description,
                CreatedAt = t.CreatedAt,
                CompletedAt = t.CompletedAt,
                RequiresApproval = t.RequiresApproval,
                EstimatedSettlement = null
            })
            .ToListAsync();
    }

    public async Task<TransferStatusResponse> GetStatusAsync(Guid userId, Guid transferId)
    {
        var transfer = await db.Transfers
            .Include(t => t.FromAccount)
            .FirstOrDefaultAsync(t => t.Id == transferId)
            ?? throw new KeyNotFoundException("Transfer not found");

        var isOwner = transfer.FromAccount.UserId == userId
            || (transfer.ToAccountId.HasValue && await db.Accounts.AnyAsync(a => a.Id == transfer.ToAccountId && a.UserId == userId))
            || await db.JuniorAccounts.AnyAsync(j => j.AccountId == transfer.FromAccountId && j.ParentUserId == userId);

        if (!isOwner)
            throw new UnauthorizedAccessException("Access denied");

        return new TransferStatusResponse
        {
            TransferId = transfer.Id,
            Status = transfer.Status,
            Channel = transfer.Channel,
            CreatedAt = transfer.CreatedAt,
            CompletedAt = transfer.CompletedAt,
            ExternalReferenceId = transfer.ExternalReferenceId
        };
    }

    public async Task<List<PendingApprovalTransferResponse>> GetPendingApprovalAsync(Guid userId)
    {
        return await db.Transfers
            .Where(t =>
                t.Status == TransferStatus.PendingApproval &&
                db.JuniorAccounts.Any(j => j.AccountId == t.FromAccountId && j.ParentUserId == userId))
            .OrderBy(t => t.CreatedAt)
            .Select(t => new PendingApprovalTransferResponse
            {
                Id = t.Id,
                FromAccountId = t.FromAccountId,
                FromAccountNumber = t.FromAccount.AccountNumber,
                ToAccountId = t.ToAccountId,
                Amount = t.Amount,
                Currency = t.Currency,
                Channel = t.Channel,
                Description = t.Description,
                CreatedAt = t.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<TransferResponse> ApproveAsync(Guid userId, Guid transferId)
    {
        var transfer = await db.Transfers
            .Include(t => t.FromAccount)
            .Include(t => t.ToAccount)
            .FirstOrDefaultAsync(t => t.Id == transferId)
            ?? throw new KeyNotFoundException("Transfer not found");

        if (transfer.Status != TransferStatus.PendingApproval)
            throw new InvalidOperationException("Transfer is not awaiting approval");

        var isParent = await db.JuniorAccounts.AnyAsync(j => j.AccountId == transfer.FromAccountId && j.ParentUserId == userId);
        if (!isParent)
            throw new UnauthorizedAccessException("Access denied");

        var availableBalance = transfer.FromAccount.Balance - transfer.FromAccount.ReservedBalance;
        if (availableBalance < transfer.Amount)
            throw new InvalidOperationException("Insufficient funds");

        transfer.FromAccount.Balance -= transfer.Amount;
        transfer.FromAccount.ReservedBalance -= transfer.Amount;
        if (transfer.ToAccount is not null)
            transfer.ToAccount.Balance += transfer.Amount;

        transfer.Status = TransferStatus.Completed;
        transfer.ApprovedBy = userId;
        transfer.ApprovedAt = DateTime.UtcNow;
        transfer.CompletedAt = DateTime.UtcNow;

        db.Transactions.AddRange(
            new Transaction
            {
                Id = Guid.NewGuid(),
                AccountId = transfer.FromAccountId,
                Amount = transfer.Amount,
                Type = TransactionType.Debit,
                Status = TransactionStatus.Completed,
                Description = transfer.Description ?? "Junior transfer",
                ReferenceId = transfer.Id.ToString(),
                CreatedAt = DateTime.UtcNow
            },
            new Transaction
            {
                Id = Guid.NewGuid(),
                AccountId = transfer.ToAccountId!.Value,
                Amount = transfer.Amount,
                Type = TransactionType.Credit,
                Status = TransactionStatus.Completed,
                Description = transfer.Description ?? "Junior transfer",
                ReferenceId = transfer.Id.ToString(),
                CreatedAt = DateTime.UtcNow
            }
        );

        await db.SaveChangesAsync();
        return PaymentServiceBase.MapToResponse(transfer);
    }

    public async Task<TransferResponse> RejectAsync(Guid userId, Guid transferId)
    {
        var transfer = await db.Transfers
            .Include(t => t.FromAccount)
            .FirstOrDefaultAsync(t => t.Id == transferId)
            ?? throw new KeyNotFoundException("Transfer not found");

        if (transfer.Status != TransferStatus.PendingApproval)
            throw new InvalidOperationException("Transfer is not awaiting approval");

        var isParent = await db.JuniorAccounts.AnyAsync(j => j.AccountId == transfer.FromAccountId && j.ParentUserId == userId);
        if (!isParent)
            throw new UnauthorizedAccessException("Access denied");

        transfer.FromAccount.ReservedBalance -= transfer.Amount;
        transfer.Status = TransferStatus.Rejected;
        transfer.RejectedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return PaymentServiceBase.MapToResponse(transfer);
    }

    public async Task ProcessWebhookAsync(Guid transferId, string status, string? referenceId)
    {
        var transfer = await db.Transfers
            .Include(t => t.FromAccount)
            .Include(t => t.ToAccount)
            .FirstOrDefaultAsync(t => t.Id == transferId)
            ?? throw new KeyNotFoundException("Transfer not found");

        if (transfer.Status != TransferStatus.Pending)
            throw new ArgumentException("Transfer is not in pending state");

        if (status == TransferStatus.Completed)
        {
            transfer.FromAccount.Balance -= transfer.Amount;
            transfer.FromAccount.ReservedBalance -= transfer.Amount;
            if (transfer.ToAccount is not null)
                transfer.ToAccount.Balance += transfer.Amount;

            transfer.Status = TransferStatus.Completed;
            transfer.CompletedAt = DateTime.UtcNow;
            transfer.ExternalReferenceId = referenceId ?? transfer.ExternalReferenceId;

            var existingDebit = await db.Transactions.FirstOrDefaultAsync(t =>
                t.ReferenceId == transfer.Id.ToString() && t.Type == TransactionType.Debit);

            if (existingDebit is not null)
            {
                existingDebit.Status = TransactionStatus.Completed;
            }
            else
            {
                db.Transactions.Add(new Transaction
                {
                    Id = Guid.NewGuid(),
                    AccountId = transfer.FromAccountId,
                    Amount = transfer.Amount,
                    Type = TransactionType.Debit,
                    Status = TransactionStatus.Completed,
                    Description = transfer.Description ?? $"{transfer.Channel} transfer",
                    ReferenceId = transfer.Id.ToString(),
                    CreatedAt = DateTime.UtcNow
                });
            }

            if (transfer.ToAccountId is not null)
            {
                db.Transactions.Add(new Transaction
                {
                    Id = Guid.NewGuid(),
                    AccountId = transfer.ToAccountId.Value,
                    Amount = transfer.Amount,
                    Type = TransactionType.Credit,
                    Status = TransactionStatus.Completed,
                    Description = transfer.Description ?? $"{transfer.Channel} transfer",
                    ReferenceId = transfer.Id.ToString(),
                    CreatedAt = DateTime.UtcNow
                });
            }
        }
        else if (status == TransferStatus.Failed)
        {
            transfer.FromAccount.ReservedBalance -= transfer.Amount;
            transfer.Status = TransferStatus.Failed;
        }
        else if (status == TransferStatus.Rejected)
        {
            transfer.FromAccount.ReservedBalance -= transfer.Amount;
            transfer.Status = TransferStatus.Rejected;
            transfer.RejectedAt = DateTime.UtcNow;
        }
        else
        {
            throw new ArgumentException($"Invalid status '{status}'");
        }

        await db.SaveChangesAsync();
    }
}
