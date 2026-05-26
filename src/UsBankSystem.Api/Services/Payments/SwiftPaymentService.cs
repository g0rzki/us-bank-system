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

        var availableBalance = fromAccount.Balance - fromAccount.ReservedBalance;
        if (availableBalance < request.Amount)
            throw new ArgumentException("Insufficient funds");

        if (await IsJuniorInitiatedAsync(userId, fromAccount.Id))
            return await CreatePendingApprovalAsync(fromAccount, null, request.Amount, request.Currency, TransferChannel.Swift, request.Description);

        var todaySwiftTotal = await GetTodayTransferTotalByChannelAsync(fromAccount.Id, TransferChannel.Swift);
        SwiftRequestValidator.ValidateDailyLimit(todaySwiftTotal, request.Amount, paymentConfig.Value.Swift.DailyLimitPerAccount);

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
            Description = request.Description,
            RequiresApproval = false,
            CreatedAt = DateTime.UtcNow
        };

        Db.Transfers.Add(transfer);
        Db.Transactions.Add(CreateTransaction(fromAccount.Id, request.Amount, TransactionType.Debit, TransactionStatus.Pending, request.Description ?? "SWIFT transfer", transfer.Id));
        await Db.SaveChangesAsync();

        var gatewayResult = await swiftGateway.SendAsync(new(
            TransferId: transfer.Id,
            Amount: transfer.Amount,
            Currency: transfer.Currency,
            Description: transfer.Description,
            Metadata: new Dictionary<string, string>
            {
                ["iban"] = request.Iban,
                ["bic"] = request.Bic,
                ["beneficiaryName"] = request.BeneficiaryName,
                ["beneficiaryAddress"] = request.BeneficiaryAddress ?? "",
                ["chargeBearer"] = request.ChargeBearer,
                ["valueDate"] = valueDate.ToString("yyyyMMdd"),
                ["remittanceInfo"] = request.RemittanceInfo ?? ""
            }
        ));

        if (!gatewayResult.Success)
        {
            transfer.Status = TransferStatus.Failed;
            fromAccount.ReservedBalance -= request.Amount;
            await Db.SaveChangesAsync();
            throw new ArgumentException(gatewayResult.Error ?? "SWIFT gateway error");
        }

        transfer.ExternalReferenceId = gatewayResult.ExternalReferenceId;
        await Db.SaveChangesAsync();
        return MapToResponse(transfer);
    }
}
