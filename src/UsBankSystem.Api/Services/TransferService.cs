using System.Data;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UsBankSystem.Api.Configuration;
using UsBankSystem.Api.Integrations.FedNow;
using UsBankSystem.Api.Integrations.Rtp;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Api.Services.Payments;
using UsBankSystem.Core.Domain.Transactions;
using UsBankSystem.Core.Domain.Transfers;
using UsBankSystem.Core.Entities;
using UsBankSystem.Infrastructure.Persistence;
using Transfer = UsBankSystem.Core.Entities.Transfer;

namespace UsBankSystem.Api.Services;

public class TransferService(
    AppDbContext db,
    FedNowMqGateway mqGateway,
    RtpTchGateway rtpTchGateway,
    Pacs008Builder pacs008Builder,
    IOptions<PaymentSessionConfig> paymentConfig)
{
    public async Task<List<TransferResponse>> GetAllAsync(Guid userId)
    {
        return await db.Transfers
            .Include(t => t.FromAccount)
            .Where(t => t.FromAccount!.UserId == userId || db.Accounts.Any(a => a.Id == t.ToAccountId && a.UserId == userId))
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
        // Ownership predicate included in the query so both "not found" and "not authorized"
        // return the same 404 — prevents IDOR enumeration via distinguishable error codes.
        var transfer = await db.Transfers
            .Include(t => t.FromAccount)
            .FirstOrDefaultAsync(t => t.Id == transferId
                && (t.FromAccount!.UserId == userId
                    || db.Accounts.Any(a => a.Id == t.ToAccountId && a.UserId == userId)
                    || db.JuniorAccounts.Any(j => j.AccountId == t.FromAccountId && j.ParentUserId == userId)))
            ?? throw new KeyNotFoundException("Transfer not found");

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
                FromAccountNumber = t.FromAccount != null ? t.FromAccount.AccountNumber : null,
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

        if (transfer.FromAccount is null)
            throw new InvalidOperationException($"Transfer {transferId} has no source account");

        var availableBalance = transfer.FromAccount.Balance - transfer.FromAccount.ReservedBalance;
        if (availableBalance < transfer.Amount)
            throw new InvalidOperationException("Insufficient funds");

        if (transfer.Channel == TransferChannel.FedNow)
            return await ApproveFedNowAsync(transfer, userId);

        if (transfer.Channel == TransferChannel.Rtp && transfer.ToRoutingNumber is not null)
            return await ApproveRtpExternalAsync(transfer, userId);

        if (transfer.Channel == TransferChannel.Ach && transfer.ToAccountId is null)
            throw new InvalidOperationException(
                "ACH external transfers cannot be approved through this flow. " +
                "Reject this transfer and have the parent submit directly via the ACH endpoint.");

        transfer.FromAccount.Balance -= transfer.Amount;
        transfer.FromAccount.ReservedBalance -= transfer.Amount;
        if (transfer.ToAccount is not null)
            transfer.ToAccount.Balance += transfer.Amount;

        transfer.Status = TransferStatus.Completed;
        transfer.ApprovedBy = userId;
        transfer.ApprovedAt = DateTime.UtcNow;
        transfer.CompletedAt = DateTime.UtcNow;

        var transactions = new List<Transaction>
        {
            new()
            {
                Id = Guid.NewGuid(),
                AccountId = transfer.FromAccountId!.Value,
                Amount = transfer.Amount,
                Type = TransactionType.Debit,
                Status = TransactionStatus.Completed,
                Description = transfer.Description ?? "Junior transfer",
                ReferenceId = transfer.Id.ToString(),
                CreatedAt = DateTime.UtcNow
            }
        };

        if (transfer.ToAccountId.HasValue)
        {
            transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(),
                AccountId = transfer.ToAccountId.Value,
                Amount = transfer.Amount,
                Type = TransactionType.Credit,
                Status = TransactionStatus.Completed,
                Description = transfer.Description ?? "Junior transfer",
                ReferenceId = transfer.Id.ToString(),
                CreatedAt = DateTime.UtcNow
            });
        }

        db.Transactions.AddRange(transactions);

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

        if (transfer.FromAccount is null)
            throw new InvalidOperationException($"Transfer {transferId} has no source account");

        transfer.FromAccount.ReservedBalance -= transfer.Amount;
        transfer.Status = TransferStatus.Rejected;
        transfer.RejectedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return PaymentServiceBase.MapToResponse(transfer);
    }

    public async Task ProcessSwiftReceiveAsync(string uetr, bool isReturn, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var transfer = await db.Transfers
            .Include(t => t.FromAccount)
            .FirstOrDefaultAsync(t => t.ExternalReferenceId == uetr, ct);

        if (transfer is null || transfer.Status != TransferStatus.Pending)
        {
            await tx.RollbackAsync(ct);
            return;
        }

        if (isReturn)
        {
            transfer.FromAccount.ReservedBalance -= transfer.Amount;
            transfer.Status = TransferStatus.Failed;

            var debit = await db.Transactions.FirstOrDefaultAsync(
                t => t.ReferenceId == transfer.Id.ToString() && t.Type == TransactionType.Debit, ct);
            if (debit is not null)
                debit.Status = TransactionStatus.Failed;
        }
        else
        {
            transfer.FromAccount.Balance -= transfer.Amount;
            transfer.FromAccount.ReservedBalance -= transfer.Amount;
            transfer.Status = TransferStatus.Completed;
            transfer.CompletedAt = DateTime.UtcNow;

            var debit = await db.Transactions.FirstOrDefaultAsync(
                t => t.ReferenceId == transfer.Id.ToString() && t.Type == TransactionType.Debit, ct);
            if (debit is not null)
                debit.Status = TransactionStatus.Completed;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task ProcessWebhookAsync(Guid transferId, string status, string? referenceId, CancellationToken ct = default)
    {
        var transfer = await db.Transfers
            .Include(t => t.FromAccount)
            .Include(t => t.ToAccount)
            .FirstOrDefaultAsync(t => t.Id == transferId, ct)
            ?? throw new KeyNotFoundException("Transfer not found");

        if (transfer.Status != TransferStatus.Pending)
            throw new ArgumentException("Transfer is not in pending state");

        if (transfer.FromAccount is null)
            throw new InvalidOperationException($"Transfer {transferId} has no source account");

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
                t.ReferenceId == transfer.Id.ToString() && t.Type == TransactionType.Debit, ct);

            if (existingDebit is not null)
            {
                existingDebit.Status = TransactionStatus.Completed;
            }
            else
            {
                db.Transactions.Add(new Transaction
                {
                    Id = Guid.NewGuid(),
                    AccountId = transfer.FromAccountId!.Value,
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

            var failedDebit = await db.Transactions.FirstOrDefaultAsync(t =>
                t.ReferenceId == transfer.Id.ToString() && t.Type == TransactionType.Debit, ct);
            if (failedDebit is not null)
                failedDebit.Status = TransactionStatus.Failed;
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

        await db.SaveChangesAsync(ct);
    }

    private async Task<TransferResponse> ApproveRtpExternalAsync(Transfer transfer, Guid userId)
    {
        if (transfer.FromAccount is null)
            throw new InvalidOperationException($"Transfer {transfer.Id} has no source account");

        var config = paymentConfig.Value.Rtp;

        transfer.ApprovedBy = userId;
        transfer.ApprovedAt = DateTime.UtcNow;

        var accountOwner = await db.Users.FirstOrDefaultAsync(u => u.Id == transfer.FromAccount.UserId);
        var senderName = accountOwner is not null ? $"{accountOwner.FirstName} {accountOwner.LastName}" : "Unknown";

        var msgId = $"MSG-{DateTime.UtcNow:yyyyMMdd}-{transfer.Id:N}";
        var endToEndId = $"E2E-{transfer.Id:N}";

        var pacs008Xml = pacs008Builder.Build(new Pacs008Data(
            MsgId: msgId,
            EndToEndId: endToEndId,
            Amount: transfer.Amount,
            Currency: transfer.Currency,
            DebtorBankName: config.BankCode,
            DebtorBankRtn: config.BankRtn,
            DebtorName: senderName,
            DebtorAccountNumber: transfer.FromAccount.AccountNumber,
            CreditorBankName: transfer.ToBankCode ?? transfer.ToRoutingNumber ?? "",
            CreditorBankRtn: transfer.ToRoutingNumber ?? "",
            CreditorName: transfer.RecipientName ?? "Unknown",
            CreditorAccountNumber: transfer.ToAccountNumber ?? "",
            Description: transfer.Description
        ));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(config.TimeoutSeconds));
        var result = await rtpTchGateway.SendPacs008Async(pacs008Xml, cts.Token);

        if (!result.Success)
        {
            transfer.Status = TransferStatus.Failed;
            transfer.FromAccount.ReservedBalance -= transfer.Amount;
            await db.SaveChangesAsync();
            return PaymentServiceBase.MapToResponse(transfer);
        }

        transfer.Status = TransferStatus.Pending;
        transfer.ExternalReferenceId = endToEndId;
        await db.SaveChangesAsync();
        return PaymentServiceBase.MapToResponse(transfer);
    }

    private async Task<TransferResponse> ApproveFedNowAsync(Transfer transfer, Guid userId)
    {
        if (transfer.FromAccount is null)
            throw new InvalidOperationException($"Transfer {transfer.Id} has no source account");

        var config = paymentConfig.Value.FedNow;

        transfer.ApprovedBy = userId;
        transfer.ApprovedAt = DateTime.UtcNow;

        var accountOwner = await db.Users.FirstOrDefaultAsync(u => u.Id == transfer.FromAccount.UserId);
        var senderName = accountOwner is not null ? $"{accountOwner.FirstName} {accountOwner.LastName}" : "Unknown";

        var msgId = $"MSG-{DateTime.UtcNow:yyyyMMdd}-{transfer.Id:N}";
        var endToEndId = $"E2E-{transfer.Id:N}";

        var pacs008Xml = pacs008Builder.Build(new Pacs008Data(
            MsgId: msgId,
            EndToEndId: endToEndId,
            Amount: transfer.Amount,
            Currency: transfer.Currency,
            DebtorBankName: config.BankLegalName,
            DebtorBankRtn: config.BankRtn,
            DebtorName: senderName,
            DebtorAccountNumber: transfer.FromAccount.AccountNumber,
            CreditorBankName: "Unknown Bank",
            CreditorBankRtn: transfer.ToRoutingNumber ?? "",
            CreditorName: transfer.RecipientName ?? "Unknown",
            CreditorAccountNumber: transfer.ToAccountNumber ?? "",
            Description: transfer.Description
        ));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(config.TimeoutSeconds));
        var (success, error) = await mqGateway.SendXmlAsync(Encoding.UTF8.GetBytes(pacs008Xml), cts.Token);

        if (!success)
        {
            transfer.Status = TransferStatus.Failed;
            transfer.FromAccount.ReservedBalance -= transfer.Amount;
            await db.SaveChangesAsync();
            return PaymentServiceBase.MapToResponse(transfer);
        }

        transfer.Status = TransferStatus.Pending;
        transfer.ExternalReferenceId = msgId;
        await db.SaveChangesAsync();
        return PaymentServiceBase.MapToResponse(transfer);
    }
}
