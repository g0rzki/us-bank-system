using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UsBankSystem.Api.Configuration;
using UsBankSystem.Api.Integrations;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Core.Domain.Common;
using UsBankSystem.Core.Domain.Transactions;
using UsBankSystem.Core.Domain.Transfers;
using UsBankSystem.Core.Entities;
using UsBankSystem.Infrastructure.Persistence;
using Transfer = UsBankSystem.Core.Entities.Transfer;

namespace UsBankSystem.Api.Services.Payments;

public class AchPaymentService(AppDbContext db, AchGateway achGateway, IOptions<PaymentSessionConfig> paymentConfig) : PaymentServiceBase(db)
{
    public async Task<TransferResponse> CreateAsync(Guid userId, CreateAchTransferRequest request)
    {
        if (!CurrencyCode.IsValid(request.Currency))
            throw new ArgumentException($"Unsupported currency '{request.Currency}'");

        var fromAccount = await ResolveFromAccountAsync(userId, request.FromAccountId);

        var availableBalance = fromAccount.Balance - fromAccount.ReservedBalance;
        if (availableBalance < request.Amount)
            throw new ArgumentException("Insufficient funds");

        if (await IsJuniorInitiatedAsync(userId, fromAccount.Id))
            return await CreatePendingApprovalAsync(fromAccount, null, request.Amount, request.Currency, TransferChannel.Ach, request.Description);

        var config = paymentConfig.Value.Ach;
        var now = DateTime.UtcNow;
        var cutoff = new DateTime(now.Year, now.Month, now.Day, config.CutoffHour, 0, 0, DateTimeKind.Utc);
        var nextBatch = now > cutoff ? cutoff.AddDays(1) : cutoff;

        fromAccount.ReservedBalance += request.Amount;

        var transfer = new Transfer
        {
            Id = Guid.NewGuid(),
            FromAccountId = fromAccount.Id,
            ToAccountId = null,
            ToAccountNumber = request.ToAccountNumber,
            Amount = request.Amount,
            Currency = request.Currency.ToUpperInvariant(),
            Channel = TransferChannel.Ach,
            Status = TransferStatus.Pending,
            Description = request.Description,
            RequiresApproval = false,
            CreatedAt = DateTime.UtcNow
        };

        // fileId is deterministic from transfer.Id — set it before the first save so ACK polling
        // can match the .ack file even if the process crashes after SFTP upload but before a
        // second SaveChangesAsync would have run.
        transfer.ExternalReferenceId = transfer.Id.ToString("N")[..16].ToUpperInvariant();

        Db.Transfers.Add(transfer);
        Db.Transactions.Add(CreateTransaction(fromAccount.Id, request.Amount, TransactionType.Debit, TransactionStatus.Pending, request.Description ?? "ACH transfer", transfer.Id));
        await Db.SaveChangesAsync();

        var gatewayResult = await achGateway.SendAsync(new(
            TransferId: transfer.Id,
            Amount: transfer.Amount,
            Currency: transfer.Currency,
            Description: transfer.Description,
            Metadata: new Dictionary<string, string>
            {
                ["toRoutingNumber"] = request.ToRoutingNumber,
                ["toAccountNumber"] = request.ToAccountNumber,
                ["recipientName"] = request.RecipientName,
                ["accountType"] = request.AccountType
            }
        ));

        if (!gatewayResult.Success)
        {
            transfer.Status = TransferStatus.Failed;
            fromAccount.ReservedBalance -= request.Amount;

            // Mark the pending debit transaction as Failed so it doesn't stay orphaned
            var pendingDebit = await Db.Transactions.FirstOrDefaultAsync(
                t => t.ReferenceId == transfer.Id.ToString() && t.Type == TransactionType.Debit);
            if (pendingDebit is not null)
                pendingDebit.Status = TransactionStatus.Failed;

            await Db.SaveChangesAsync();
            throw new ArgumentException(gatewayResult.Error ?? "ACH gateway error");
        }

        var response = MapToResponse(transfer);
        response.EstimatedSettlement = nextBatch;
        return response;
    }
}
