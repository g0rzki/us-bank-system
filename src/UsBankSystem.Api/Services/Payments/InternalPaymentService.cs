using Microsoft.EntityFrameworkCore;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Core.Domain.Common;
using UsBankSystem.Core.Domain.Transactions;
using UsBankSystem.Core.Domain.Transfers;
using UsBankSystem.Core.Entities;
using UsBankSystem.Infrastructure.Persistence;
using Transfer = UsBankSystem.Core.Entities.Transfer;

namespace UsBankSystem.Api.Services.Payments;

public class InternalPaymentService(AppDbContext db) : PaymentServiceBase(db)
{
    public async Task<TransferResponse> CreateAsync(Guid userId, CreateInternalTransferRequest request)
    {
        if (!CurrencyCode.IsValid(request.Currency))
            throw new ArgumentException($"Unsupported currency '{request.Currency}'");

        var fromAccount = await ResolveFromAccountAsync(userId, request.FromAccountId);

        var toAccount = request.ToAccountId.HasValue
            ? await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.ToAccountId.Value && a.Status == AccountStatus.Active)
            : await db.Accounts.FirstOrDefaultAsync(a => a.AccountNumber == request.ToAccountNumber && a.Status == AccountStatus.Active);
        if (toAccount is null)
            throw new KeyNotFoundException("Destination account not found or inactive");

        if (fromAccount.Id == toAccount.Id)
            throw new ArgumentException("Cannot transfer to the same account");

        var availableBalance = fromAccount.Balance - fromAccount.ReservedBalance;
        if (availableBalance < request.Amount)
            throw new ArgumentException("Insufficient funds");

        var dailyLimit = await GetDailyTransferLimitAsync(fromAccount.Id);
        var todayTotal = await GetTodayTransferTotalAsync(fromAccount.Id);
        if (dailyLimit.HasValue && todayTotal + request.Amount > dailyLimit.Value)
            throw new ArgumentException($"Daily transfer limit exceeded. Limit: {dailyLimit}, used: {todayTotal}, requested: {request.Amount}");

        if (await IsJuniorInitiatedAsync(userId, fromAccount.Id))
            return await CreatePendingApprovalAsync(fromAccount, toAccount.Id, request.Amount, request.Currency, TransferChannel.Internal, request.Description);

        var transfer = new Transfer
        {
            Id = Guid.NewGuid(),
            FromAccountId = fromAccount.Id,
            ToAccountId = toAccount.Id,
            ToAccountNumber = toAccount.AccountNumber,
            Amount = request.Amount,
            Currency = request.Currency.ToUpperInvariant(),
            Channel = TransferChannel.Internal,
            Status = TransferStatus.Completed,
            Description = request.Description,
            RequiresApproval = false,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };

        fromAccount.Balance -= request.Amount;
        toAccount.Balance += request.Amount;

        db.Transactions.AddRange(
            CreateTransaction(fromAccount.Id, request.Amount, TransactionType.Debit, TransactionStatus.Completed, request.Description ?? "Internal transfer", transfer.Id),
            CreateTransaction(toAccount.Id, request.Amount, TransactionType.Credit, TransactionStatus.Completed, request.Description ?? "Internal transfer", transfer.Id)
        );

        db.Transfers.Add(transfer);
        await db.SaveChangesAsync();
        return MapToResponse(transfer);
    }
}
