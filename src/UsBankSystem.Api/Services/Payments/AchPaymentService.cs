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

        // [5][8] NACHA has no currency field — ACH settles exclusively in USD.
        // Check before the junior redirect so the constraint applies to all paths.
        if (request.Currency.ToUpperInvariant() != "USD")
            throw new ArgumentException("ACH transfers are USD-only (NACHA does not support other currencies)");

        // [2] ACH external transfers cannot be routed through the PendingApproval flow:
        // gateway metadata (routing number, recipient, etc.) is not persisted on the Transfer
        // and approval via ApproveAsync cannot submit a NACHA file. Parent must submit directly.
        if (await IsJuniorInitiatedAsync(userId, fromAccount.Id))
            throw new InvalidOperationException("ACH external transfers cannot be initiated by junior account holders. The account parent must submit the transfer directly via the ACH endpoint.");

        var config = paymentConfig.Value.Ach;

        // [6] FedACH cutoff is published in Eastern Time; computing it in UTC produces
        //     a 4–5 hour error depending on DST and would misroute same-day transfers.
        var easternZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var nowEt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, easternZone);
        var cutoffEt = new DateTime(nowEt.Year, nowEt.Month, nowEt.Day, config.CutoffHour, 0, 0);
        var nextBatchEt = nowEt >= cutoffEt ? cutoffEt.AddDays(1) : cutoffEt;
        var nextBatch = TimeZoneInfo.ConvertTimeToUtc(nextBatchEt, easternZone);

        // [1] commit-then-compensate: short DB commit before gateway I/O so no row lock
        //     is held during the SFTP upload (up to 30s). If the process crashes between
        //     commit and compensation, TimeoutStalePendingTransfersAsync reclaims the reserve.
        var now = DateTime.UtcNow;  // [12] single timestamp for Transfer and Transaction

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
            CreatedAt = now
        };

        transfer.ExternalReferenceId = AchGateway.ComputeFileId(transfer.Id);

        Db.Transfers.Add(transfer);
        Db.Transactions.Add(CreateTransaction(fromAccount.Id, request.Amount, TransactionType.Debit, TransactionStatus.Pending, request.Description ?? "ACH transfer", transfer.Id, now));
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

            var pendingDebit = Db.ChangeTracker.Entries<UsBankSystem.Core.Entities.Transaction>()
                .FirstOrDefault(e => e.Entity.ReferenceId == transfer.Id.ToString()
                                  && e.Entity.Type == TransactionType.Debit)?.Entity;
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
