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

public class FedNowPaymentService(AppDbContext db, FedNowGateway fedNowGateway, IOptions<PaymentSessionConfig> paymentConfig) : PaymentServiceBase(db)
{
    public async Task<TransferResponse> CreateAsync(Guid userId, CreateFedNowTransferRequest request)
    {
        if (!CurrencyCode.IsValid(request.Currency))
            throw new ArgumentException($"Unsupported currency '{request.Currency}'");

        var fromAccount = await ResolveFromAccountAsync(userId, request.FromAccountId);

        var toAccount = await Db.Accounts.FirstOrDefaultAsync(a => a.AccountNumber == request.ToAccountNumber && a.Status == AccountStatus.Active)
            ?? throw new KeyNotFoundException("Destination account not found or inactive");

        if (fromAccount.Id == toAccount.Id)
            throw new ArgumentException("Cannot transfer to the same account");

        var availableBalance = fromAccount.Balance - fromAccount.ReservedBalance;
        if (availableBalance < request.Amount)
            throw new ArgumentException("Insufficient funds");

        if (await IsJuniorInitiatedAsync(userId, fromAccount.Id))
            return await CreatePendingApprovalAsync(fromAccount, toAccount.Id, request.Amount, request.Currency, TransferChannel.FedNow, request.Description);

        fromAccount.ReservedBalance += request.Amount;

        var transfer = new Transfer
        {
            Id = Guid.NewGuid(),
            FromAccountId = fromAccount.Id,
            ToAccountId = toAccount.Id,
            ToAccountNumber = toAccount.AccountNumber,
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

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(paymentConfig.Value.FedNow.TimeoutSeconds));
        var gatewayResult = await fedNowGateway.SendAsync(new(
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
            throw new ArgumentException(gatewayResult.Error ?? "FedNow gateway error");
        }

        fromAccount.Balance -= request.Amount;
        fromAccount.ReservedBalance -= request.Amount;
        toAccount.Balance += request.Amount;
        transfer.Status = TransferStatus.Completed;
        transfer.CompletedAt = DateTime.UtcNow;
        transfer.ExternalReferenceId = gatewayResult.ExternalReferenceId;

        Db.Transactions.AddRange(
            CreateTransaction(fromAccount.Id, request.Amount, TransactionType.Debit, TransactionStatus.Completed, request.Description ?? "FedNow transfer", transfer.Id),
            CreateTransaction(toAccount.Id, request.Amount, TransactionType.Credit, TransactionStatus.Completed, request.Description ?? "FedNow transfer", transfer.Id)
        );

        await Db.SaveChangesAsync();
        return MapToResponse(transfer);
    }
}
