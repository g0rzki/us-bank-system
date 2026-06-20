using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UsBankSystem.Api.Configuration;
using UsBankSystem.Api.Integrations;
using UsBankSystem.Api.Integrations.FedNow;
using UsBankSystem.Api.Integrations.Rtp;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Core.Domain.Common;
using UsBankSystem.Core.Domain.Transactions;
using UsBankSystem.Core.Domain.Transfers;
using UsBankSystem.Core.Entities;
using UsBankSystem.Infrastructure.Persistence;
using Transfer = UsBankSystem.Core.Entities.Transfer;

namespace UsBankSystem.Api.Services.Payments;

public class RtpPaymentService(
    AppDbContext db,
    RtpGateway rtpGateway,
    RtpTchGateway rtpTchGateway,
    Pacs008Builder pacs008Builder,
    IOptions<PaymentSessionConfig> paymentConfig) : PaymentServiceBase(db)
{
    public async Task<TransferResponse> CreateAsync(Guid userId, CreateRtpTransferRequest request)
    {
        if (!CurrencyCode.IsValid(request.Currency))
            throw new ArgumentException($"Unsupported currency '{request.Currency}'");

        var fromAccount = await ResolveFromAccountAsync(userId, request.FromAccountId);

        var availableBalance = fromAccount.Balance - fromAccount.ReservedBalance;
        if (availableBalance < request.Amount)
            throw new ArgumentException("Insufficient funds");

        if (!string.IsNullOrWhiteSpace(request.ToRoutingNumber))
            return await CreateExternalAsync(userId, fromAccount, request);

        return await CreateInternalAsync(userId, fromAccount, request);
    }

    private async Task<TransferResponse> CreateExternalAsync(Guid userId, Account fromAccount, CreateRtpTransferRequest request)
    {
        if (await IsJuniorInitiatedAsync(userId, fromAccount.Id))
            return await CreatePendingApprovalAsync(fromAccount, null, request.Amount, request.Currency, TransferChannel.Rtp, request.Description,
                request.ToAccountNumber, request.ToRoutingNumber, request.RecipientName, request.ToBankCode);

        fromAccount.ReservedBalance += request.Amount;

        var transfer = new Transfer
        {
            Id = Guid.NewGuid(),
            FromAccountId = fromAccount.Id,
            ToAccountId = null,
            ToAccountNumber = request.ToAccountNumber,
            ToRoutingNumber = request.ToRoutingNumber,
            ToBankCode = request.ToBankCode,
            RecipientName = request.RecipientName,
            Amount = request.Amount,
            Currency = request.Currency.ToUpperInvariant(),
            Channel = TransferChannel.Rtp,
            Status = TransferStatus.Pending,
            Description = request.Description,
            RequiresApproval = false,
            CreatedAt = DateTime.UtcNow
        };

        Db.Transfers.Add(transfer);
        await Db.SaveChangesAsync();

        var config = paymentConfig.Value.Rtp;
        var user = await Db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        var senderName = user is not null ? $"{user.FirstName} {user.LastName}" : "Unknown";

        var msgId = $"MSG-{DateTime.UtcNow:yyyyMMdd}-{transfer.Id:N}";
        var endToEndId = $"E2E-{transfer.Id:N}";

        var creditorBankCode = request.ToBankCode ?? request.ToRoutingNumber!;

        var pacs008Xml = pacs008Builder.Build(new Pacs008Data(
            MsgId: msgId,
            EndToEndId: endToEndId,
            Amount: request.Amount,
            Currency: transfer.Currency,
            DebtorBankName: config.BankCode,
            DebtorBankRtn: config.BankRtn,
            DebtorName: senderName,
            DebtorAccountNumber: fromAccount.AccountNumber,
            CreditorBankName: creditorBankCode,
            CreditorBankRtn: request.ToRoutingNumber!,
            CreditorName: request.RecipientName ?? "Unknown",
            CreditorAccountNumber: request.ToAccountNumber,
            Description: request.Description
        ));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(config.TimeoutSeconds));
        var result = await rtpTchGateway.SendPacs008Async(pacs008Xml, cts.Token);

        if (!result.Success)
        {
            transfer.Status = TransferStatus.Failed;
            fromAccount.ReservedBalance -= request.Amount;
            await Db.SaveChangesAsync();
            throw new ArgumentException(result.Error ?? "RTP TCH gateway error");
        }

        transfer.ExternalReferenceId = endToEndId;
        await Db.SaveChangesAsync();

        return MapToResponse(transfer);
    }

    private async Task<TransferResponse> CreateInternalAsync(Guid userId, Account fromAccount, CreateRtpTransferRequest request)
    {
        var toAccount = await Db.Accounts.FirstOrDefaultAsync(a => a.AccountNumber == request.ToAccountNumber && a.Status == AccountStatus.Active)
            ?? throw new KeyNotFoundException("Destination account not found or inactive");

        if (fromAccount.Id == toAccount.Id)
            throw new ArgumentException("Cannot transfer to the same account");

        if (await IsJuniorInitiatedAsync(userId, fromAccount.Id))
            return await CreatePendingApprovalAsync(fromAccount, toAccount.Id, request.Amount, request.Currency, TransferChannel.Rtp, request.Description);

        fromAccount.ReservedBalance += request.Amount;

        var transfer = new Transfer
        {
            Id = Guid.NewGuid(),
            FromAccountId = fromAccount.Id,
            ToAccountId = toAccount.Id,
            ToAccountNumber = toAccount.AccountNumber,
            Amount = request.Amount,
            Currency = request.Currency.ToUpperInvariant(),
            Channel = TransferChannel.Rtp,
            Status = TransferStatus.Pending,
            Description = request.Description,
            RequiresApproval = false,
            CreatedAt = DateTime.UtcNow
        };

        Db.Transfers.Add(transfer);
        await Db.SaveChangesAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(paymentConfig.Value.Rtp.TimeoutSeconds));
        var gatewayResult = await rtpGateway.SendAsync(new(
            TransferId: transfer.Id,
            Amount: transfer.Amount,
            Currency: transfer.Currency,
            Description: transfer.Description,
            Metadata: new Dictionary<string, string> { ["toAccountId"] = toAccount.Id.ToString() }
        ), cts.Token);

        if (!gatewayResult.Success)
        {
            transfer.Status = TransferStatus.Failed;
            fromAccount.ReservedBalance -= request.Amount;
            await Db.SaveChangesAsync();
            throw new ArgumentException(gatewayResult.Error ?? "RTP gateway error");
        }

        fromAccount.Balance -= request.Amount;
        fromAccount.ReservedBalance -= request.Amount;
        toAccount.Balance += request.Amount;
        transfer.Status = TransferStatus.Completed;
        transfer.CompletedAt = DateTime.UtcNow;
        transfer.ExternalReferenceId = gatewayResult.ExternalReferenceId;

        Db.Transactions.AddRange(
            CreateTransaction(fromAccount.Id, request.Amount, TransactionType.Debit, TransactionStatus.Completed, request.Description ?? "RTP transfer", transfer.Id),
            CreateTransaction(toAccount.Id, request.Amount, TransactionType.Credit, TransactionStatus.Completed, request.Description ?? "RTP transfer", transfer.Id)
        );

        await Db.SaveChangesAsync();
        return MapToResponse(transfer);
    }
}
