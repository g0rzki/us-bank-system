using Microsoft.EntityFrameworkCore;
using UsBankSystem.Api.Integrations;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Core.Domain.Blik;
using UsBankSystem.Core.Domain.Common;
using UsBankSystem.Core.Domain.Transactions;
using UsBankSystem.Core.Entities;
using UsBankSystem.Infrastructure.Persistence;

namespace UsBankSystem.Api.Services;

public class BlikService(AppDbContext db, IKlikApiClient klikClient, ILogger<BlikService> logger)
{
    public async Task<BlikCodeResponse> GenerateCodeAsync(Guid userId, Guid accountId)
    {
        var isJunior = await db.JuniorAccounts.AnyAsync(j => j.Account.UserId == userId);
        if (isJunior)
            throw new UnauthorizedAccessException("BLIK is not available for junior accounts");

        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId && a.Status == AccountStatus.Active)
            ?? throw new KeyNotFoundException("Account not found or inactive");

        if (account.UserId != userId)
            throw new UnauthorizedAccessException("Account does not belong to the current user");

        // Expire any active codes for this user before generating a new one
        var activeCodes = await db.BlikCodes
            .Where(c => c.UserId == userId && c.Status == BlikCodeStatus.Active)
            .ToListAsync();
        foreach (var c in activeCodes)
            c.Status = BlikCodeStatus.Expired;

        var result = await klikClient.GenerateCodeAsync(userId.ToString());
        if (!result.Success)
            throw new InvalidOperationException($"KLIK code generation failed: {result.Error}");

        var blikCode = new BlikCode
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            UserId = userId,
            Code = result.Code!,
            Status = BlikCodeStatus.Active,
            ExpiresAt = result.ExpiresAt!.Value,
            CreatedAt = DateTime.UtcNow
        };
        db.BlikCodes.Add(blikCode);
        await db.SaveChangesAsync();

        var expiresIn = (int)(blikCode.ExpiresAt - DateTime.UtcNow).TotalSeconds;
        return new BlikCodeResponse { Code = blikCode.Code, ExpiresAt = blikCode.ExpiresAt, ExpiresIn = Math.Max(0, expiresIn) };
    }

    public async Task<object> HandleAuthorizeWebhookAsync(KlikWebhookAuthorizeRequest payload)
    {
        if (!Guid.TryParse(payload.UserId, out var userId))
            throw new KeyNotFoundException($"Invalid user_id format: {payload.UserId}");

        var userExists = await db.Users.AnyAsync(u => u.Id == userId);
        if (!userExists)
            throw new KeyNotFoundException($"User not found: {userId}");

        // Idempotent — return immediately if already processed
        if (await db.BlikAuthorizations.AnyAsync(a => a.KlikTransactionId == payload.TransactionId))
        {
            logger.LogInformation("Duplicate webhook for transaction {TxId}, ignoring", payload.TransactionId);
            return new { received = true, will_prompt_user = true };
        }

        // Find the user's most recent active BlikCode to associate the account
        var blikCode = await db.BlikCodes
            .Where(c => c.UserId == userId && c.Status == BlikCodeStatus.Active)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync();

        Guid accountId;
        if (blikCode != null)
        {
            blikCode.Status = BlikCodeStatus.Used;
            accountId = blikCode.AccountId;
        }
        else
        {
            // Fallback: find user's primary checking account
            var fallbackAccount = await db.Accounts.FirstOrDefaultAsync(
                a => a.UserId == userId && a.Type == "checking" && a.Status == AccountStatus.Active);
            if (fallbackAccount == null)
                throw new KeyNotFoundException("No active account found for user");
            accountId = fallbackAccount.Id;
        }

        var authorization = new BlikAuthorization
        {
            Id = Guid.NewGuid(),
            KlikTransactionId = payload.TransactionId,
            UserId = userId,
            AccountId = accountId,
            Amount = payload.Amount,
            Currency = payload.Currency,
            MerchantName = payload.MerchantName,
            IsOnUs = payload.IsOnUs,
            ExpiryTime = payload.ExpiryTime,
            Status = BlikAuthorizationStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        db.BlikAuthorizations.Add(authorization);
        await db.SaveChangesAsync();

        return new { received = true, will_prompt_user = true };
    }

    public async Task<BlikAuthorizationResponse> DecideAsync(Guid userId, Guid authorizationId, bool accepted)
    {
        var auth = await db.BlikAuthorizations
            .FirstOrDefaultAsync(a => a.Id == authorizationId && a.UserId == userId)
            ?? throw new KeyNotFoundException("Authorization not found");

        if (auth.Status != BlikAuthorizationStatus.Pending)
            throw new InvalidOperationException("Authorization is not pending");

        if (auth.ExpiryTime < DateTime.UtcNow)
        {
            auth.Status = BlikAuthorizationStatus.Timeout;
            auth.DecidedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            throw new InvalidOperationException("Authorization has expired");
        }

        if (!accepted)
        {
            await klikClient.ConfirmPaymentAsync(auth.KlikTransactionId, false, "USER_DECLINED");
            auth.Status = BlikAuthorizationStatus.Rejected;
            auth.DecidedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return MapToResponse(auth);
        }

        // Accepted path
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == auth.AccountId && a.Status == AccountStatus.Active)
            ?? throw new KeyNotFoundException("Account not found or inactive");

        if (account.Balance - account.ReservedBalance < auth.Amount)
        {
            await klikClient.ConfirmPaymentAsync(auth.KlikTransactionId, false, "INSUFFICIENT_FUNDS");
            auth.Status = BlikAuthorizationStatus.Rejected;
            auth.DecidedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return MapToResponse(auth);
        }

        account.Balance -= auth.Amount;

        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Amount = auth.Amount,
            Type = TransactionType.Debit,
            Status = "completed",
            Description = $"BLIK: {auth.MerchantName}",
            ReferenceId = auth.KlikTransactionId,
            CreatedAt = DateTime.UtcNow
        };
        db.Transactions.Add(tx);

        // TODO: is_on_us=true — credit merchant_net to merchant's account (not implemented in MVP)

        await klikClient.ConfirmPaymentAsync(auth.KlikTransactionId, true, null);

        auth.Status = BlikAuthorizationStatus.Accepted;
        auth.DecidedAt = DateTime.UtcNow;
        auth.LocalTransactionId = tx.Id;

        await db.SaveChangesAsync();
        return MapToResponse(auth);
    }

    public async Task<List<BlikAuthorizationResponse>> GetPendingAsync(Guid userId)
    {
        var auths = await db.BlikAuthorizations
            .Where(a => a.UserId == userId && a.Status == BlikAuthorizationStatus.Pending)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
        return auths.Select(MapToResponse).ToList();
    }

    public async Task<List<BlikAuthorizationResponse>> GetHistoryAsync(Guid userId)
    {
        var auths = await db.BlikAuthorizations
            .Where(a => a.UserId == userId && a.Status != BlikAuthorizationStatus.Pending)
            .OrderByDescending(a => a.DecidedAt ?? a.ExpiryTime)
            .ToListAsync();
        return auths.Select(MapToResponse).ToList();
    }

    private static BlikAuthorizationResponse MapToResponse(BlikAuthorization a) => new()
    {
        Id = a.Id,
        KlikTransactionId = a.KlikTransactionId,
        Amount = a.Amount,
        Currency = a.Currency,
        MerchantName = a.MerchantName,
        IsOnUs = a.IsOnUs,
        ExpiryTime = a.ExpiryTime,
        Status = a.Status,
        CreatedAt = a.CreatedAt,
        DecidedAt = a.DecidedAt,
        LocalTransactionId = a.LocalTransactionId
    };
}
