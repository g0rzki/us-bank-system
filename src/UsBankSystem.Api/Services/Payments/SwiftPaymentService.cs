using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UsBankSystem.Api.Configuration;
using UsBankSystem.Api.Integrations;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Core.Domain.Common;
using UsBankSystem.Core.Domain.Swift;
using UsBankSystem.Core.Domain.Transactions;
using UsBankSystem.Core.Domain.Transfers;
using UsBankSystem.Core.Entities;
using UsBankSystem.Infrastructure.Persistence;
using Transfer = UsBankSystem.Core.Entities.Transfer;

namespace UsBankSystem.Api.Services.Payments;

public class SwiftPaymentService(AppDbContext db, SwiftGateway swiftGateway, IOptions<PaymentSessionConfig> paymentConfig) : PaymentServiceBase(db)
{
    public async Task<TransferResponse> CreateAsync(Guid userId, CreateSwiftTransferRequest request)
    {
        SwiftRequestValidator.Validate(request.Iban, request.Bic, request.ChargeBearer, request.Currency, request.ValueDate);
        var valueDate = SwiftRequestValidator.ResolveValueDate(request.ValueDate);

        var fromAccount = await ResolveFromAccountAsync(userId, request.FromAccountId);
        var fromUser = await Db.Users.FindAsync(fromAccount.UserId)
            ?? throw new InvalidOperationException($"User {fromAccount.UserId} not found for account {fromAccount.Id}");
        var fromAccountName = $"{fromUser.FirstName} {fromUser.LastName}".Trim();

        if (await IsJuniorInitiatedAsync(userId, fromAccount.Id))
            return await CreatePendingApprovalAsync(fromAccount, null, request.Amount, request.Currency, TransferChannel.Swift, request.Description);

        // Serializable ensures the daily-limit check and the ReservedBalance update are atomic.
        await using var tx = await Db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        // Reload inside the transaction to get values locked for this transaction.
        await Db.Entry(fromAccount).ReloadAsync();

        var availableBalance = fromAccount.Balance - fromAccount.ReservedBalance;
        if (availableBalance < request.Amount)
            throw new ArgumentException("Insufficient funds");

        var todaySwiftTotal = await GetTodayTransferTotalByChannelAsync(fromAccount.Id, TransferChannel.Swift);
        SwiftRequestValidator.ValidateDailyLimit(todaySwiftTotal, request.Amount, paymentConfig.Value.Swift.DailyLimitPerAccount);

        // Pre-generate UETR and persist it before calling the gateway so that a successful
        // gateway call that is followed by a failed SaveChanges cannot lose the UETR.
        var uetr = Guid.NewGuid().ToString().ToLowerInvariant();
        fromAccount.ReservedBalance += request.Amount;

        var transfer = new Transfer
        {
            Id = Guid.NewGuid(),
            FromAccountId = fromAccount.Id,
            ToAccountId = null,
            ToAccountNumber = request.Iban,
            Amount = request.Amount,
            Currency = request.Currency.ToUpperInvariant(),
            Channel = TransferChannel.Swift,
            Status = TransferStatus.Pending,
            ExternalReferenceId = uetr,
            Description = request.Description,
            RequiresApproval = false,
            CreatedAt = DateTime.UtcNow
        };

        Db.Transfers.Add(transfer);
        Db.Transactions.Add(CreateTransaction(fromAccount.Id, request.Amount, TransactionType.Debit, TransactionStatus.Pending, request.Description ?? "SWIFT transfer", transfer.Id));
        await Db.SaveChangesAsync();
        await tx.CommitAsync();

        var gatewayResult = await swiftGateway.SendAsync(new(
            TransferId: transfer.Id,
            Amount: transfer.Amount,
            Currency: transfer.Currency,
            Description: transfer.Description,
            Metadata: new Dictionary<string, string>
            {
                ["uetr"] = uetr,
                ["iban"] = request.Iban,
                ["bic"] = request.Bic,
                ["beneficiaryName"] = request.BeneficiaryName,
                ["beneficiaryAddress"] = request.BeneficiaryAddress ?? "",
                ["chargeBearer"] = request.ChargeBearer,
                ["valueDate"] = valueDate.ToString("yyyyMMdd"),
                ["remittanceInfo"] = request.RemittanceInfo ?? "",
                ["fromAccountNumber"] = fromAccount.AccountNumber,
                ["fromAccountName"] = fromAccountName
            }
        ));

        if (!gatewayResult.Success)
        {
            transfer.Status = TransferStatus.Failed;
            fromAccount.ReservedBalance -= request.Amount;
            await Db.SaveChangesAsync();
            throw new ArgumentException(gatewayResult.Error ?? "SWIFT gateway error");
        }

        return MapToResponse(transfer);
    }
}
