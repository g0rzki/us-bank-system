using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UsBankSystem.Api.Configuration;
using UsBankSystem.Api.Integrations.FedNow;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Core.Domain.Common;
using UsBankSystem.Core.Domain.Transfers;
using UsBankSystem.Infrastructure.Persistence;
using Transfer = UsBankSystem.Core.Entities.Transfer;

namespace UsBankSystem.Api.Services.Payments;

public class FedNowPaymentService(
    AppDbContext db,
    FedNowMqGateway mqGateway,
    Pacs008Builder pacs008Builder,
    IOptions<PaymentSessionConfig> paymentConfig) : PaymentServiceBase(db)
{
    public async Task<TransferResponse> CreateAsync(Guid userId, CreateFedNowTransferRequest request)
    {
        if (!CurrencyCode.IsValid(request.Currency))
            throw new ArgumentException($"Unsupported currency '{request.Currency}'");

        var fromAccount = await ResolveFromAccountAsync(userId, request.FromAccountId);

        var availableBalance = fromAccount.Balance - fromAccount.ReservedBalance;
        if (availableBalance < request.Amount)
            throw new ArgumentException("Insufficient funds");

        if (await IsJuniorInitiatedAsync(userId, fromAccount.Id))
            return await CreatePendingApprovalAsync(fromAccount, null, request.Amount, request.Currency, TransferChannel.FedNow, request.Description,
                request.ToAccountNumber, request.ToRoutingNumber, request.RecipientName);

        fromAccount.ReservedBalance += request.Amount;

        var transfer = new Transfer
        {
            Id = Guid.NewGuid(),
            FromAccountId = fromAccount.Id,
            ToAccountId = null,
            ToAccountNumber = request.ToAccountNumber,
            Amount = request.Amount,
            Currency = request.Currency.ToUpperInvariant(),
            Channel = TransferChannel.FedNow,
            Status = TransferStatus.Pending,
            Description = request.Description,
            RequiresApproval = false,
            CreatedAt = DateTime.UtcNow
        };

        Db.Transfers.Add(transfer);
        await Db.SaveChangesAsync();

        var config = paymentConfig.Value.FedNow;

        var user = await Db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        var senderName = user is not null ? $"{user.FirstName} {user.LastName}" : "Unknown";

        var msgId = $"MSG-{DateTime.UtcNow:yyyyMMdd}-{transfer.Id:N}";
        var endToEndId = $"E2E-{transfer.Id:N}";

        var pacs008Xml = pacs008Builder.Build(new Pacs008Data(
            MsgId: msgId,
            EndToEndId: endToEndId,
            Amount: request.Amount,
            Currency: transfer.Currency,
            DebtorBankName: config.BankLegalName,
            DebtorBankRtn: config.BankRtn,
            DebtorName: senderName,
            DebtorAccountNumber: fromAccount.AccountNumber,
            CreditorBankName: "Unknown Bank",
            CreditorBankRtn: request.ToRoutingNumber,
            CreditorName: request.RecipientName ?? "Unknown",
            CreditorAccountNumber: request.ToAccountNumber,
            Description: request.Description
        ));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(config.TimeoutSeconds));
        var (success, error) = await mqGateway.SendXmlAsync(Encoding.UTF8.GetBytes(pacs008Xml), cts.Token);

        if (!success)
        {
            transfer.Status = TransferStatus.Failed;
            fromAccount.ReservedBalance -= request.Amount;
            await Db.SaveChangesAsync();
            throw new ArgumentException(error ?? "FedNow MQ gateway error");
        }

        transfer.ExternalReferenceId = msgId;
        await Db.SaveChangesAsync();

        return MapToResponse(transfer);
    }
}
