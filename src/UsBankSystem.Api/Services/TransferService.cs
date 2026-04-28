using Microsoft.EntityFrameworkCore;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Core.Domain.Common;
using UsBankSystem.Core.Domain.Transactions;
using UsBankSystem.Core.Domain.Transfers;
using UsBankSystem.Core.Entities;
using UsBankSystem.Infrastructure.Persistence;
using Transfer = UsBankSystem.Core.Entities.Transfer; 

namespace UsBankSystem.Api.Services;

public class TransferService(AppDbContext db)
{
    public async Task<(bool Success, string? Error, int StatusCode, TransferResponse? Result)> CreateInternalAsync(Guid userId, CreateInternalTransferRequest request)
    {
        if (!CurrencyCode.IsValid(request.Currency))
            return (false, $"Unsupported currency '{request.Currency}'", 400, null);

        var fromAccount = await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.FromAccountId && a.UserId == userId && a.Status == AccountStatus.Active);
        if (fromAccount is null)
            return (false, "Source account not found or inactive", 404, null);

        var toAccount = await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.ToAccountId && a.Status == AccountStatus.Active);
        if (toAccount is null)
            return (false, "Destination account not found or inactive", 404, null);

        if (fromAccount.Id == toAccount.Id)
            return (false, "Cannot transfer to the same account", 400, null);

        // Sprawdź czy konto źródłowe to konto junior
        var isJuniorAccount = false; // TODO: US-30 - sprawdzenie konta junior
        // var isJuniorAccount = await db.Set<JuniorAccount>().AnyAsync(j => j.AccountId == fromAccount.Id);

        var availableBalance = fromAccount.Balance - fromAccount.ReservedBalance;
        if (availableBalance < request.Amount)
            return (false, "Insufficient funds", 400, null);
        
        // Sprawdź dzienny limit transferów
        var dailyLimit = await GetDailyTransferLimitAsync(fromAccount.Id);
        var todayTotal = await GetTodayTransferTotalAsync(fromAccount.Id);
        if (dailyLimit.HasValue && todayTotal + request.Amount > dailyLimit.Value)
            return (false, $"Daily transfer limit exceeded. Limit: {dailyLimit}, used: {todayTotal}, requested: {request.Amount}", 400, null);

        var requiresApproval = isJuniorAccount;
        var status = requiresApproval ? TransferStatus.PendingApproval : TransferStatus.Pending;

        var transfer = new Transfer
        {
            Id = Guid.NewGuid(),
            FromAccountId = fromAccount.Id,
            ToAccountId = toAccount.Id,
            Amount = request.Amount,
            Currency = request.Currency.ToUpperInvariant(),
            Channel = TransferChannel.Internal,
            Status = status,
            Description = request.Description,
            RequiresApproval = requiresApproval,
            CreatedAt = DateTime.UtcNow
        };

        if (!requiresApproval)
        {
            // Wykonaj przelew natychmiast
            fromAccount.Balance -= request.Amount;
            toAccount.Balance += request.Amount;
            transfer.Status = TransferStatus.Completed;
            transfer.CompletedAt = DateTime.UtcNow;

            // Zapisz transakcje
            db.Transactions.AddRange(
                new Transaction
                {
                    Id = Guid.NewGuid(),
                    AccountId = fromAccount.Id,
                    Amount = request.Amount,
                    Type = TransactionType.Debit,
                    Status = TransactionStatus.Completed,
                    Description = request.Description ?? "Internal transfer",
                    ReferenceId = transfer.Id.ToString(),
                    CreatedAt = DateTime.UtcNow
                },
                new Transaction
                {
                    Id = Guid.NewGuid(),
                    AccountId = toAccount.Id,
                    Amount = request.Amount,
                    Type = TransactionType.Credit,
                    Status = TransactionStatus.Completed,
                    Description = request.Description ?? "Internal transfer",
                    ReferenceId = transfer.Id.ToString(),
                    CreatedAt = DateTime.UtcNow
                }
            );
        }
        else
        {
            // Zablokuj środki na koncie junior
            fromAccount.ReservedBalance += request.Amount;
        }

        db.Transfers.Add(transfer);
        await db.SaveChangesAsync();

        return (true, null, 201, new TransferResponse
        {
            Id = transfer.Id,
            FromAccountId = transfer.FromAccountId,
            ToAccountId = transfer.ToAccountId,
            Amount = transfer.Amount,
            Currency = transfer.Currency,
            Channel = transfer.Channel,
            Status = transfer.Status,
            Description = transfer.Description,
            CreatedAt = transfer.CreatedAt,
            CompletedAt = transfer.CompletedAt,
            RequiresApproval = transfer.RequiresApproval
        });
    }
    
    private async Task<decimal?> GetDailyTransferLimitAsync(Guid accountId)
    {
        // Karta prepaid junior ma limit — na razie zwracamy null (brak limitu)
        // TODO: US-30 — podpiąć limit z JuniorAccount/Card
        return await Task.FromResult<decimal?>(null);
    }

    private async Task<decimal> GetTodayTransferTotalAsync(Guid accountId)
    {
        var today = DateTime.UtcNow.Date;
        return await db.Transfers
            .Where(t => t.FromAccountId == accountId
                        && t.CreatedAt >= today
                        && t.Status != TransferStatus.Rejected
                        && t.Status != TransferStatus.Failed)
            .SumAsync(t => t.Amount);
    }
}