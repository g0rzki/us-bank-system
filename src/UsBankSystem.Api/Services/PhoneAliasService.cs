using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UsBankSystem.Api.Configuration;
using UsBankSystem.Api.Integrations;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Api.Services.Payments;
using UsBankSystem.Core.Domain;
using UsBankSystem.Core.Domain.Blik;
using UsBankSystem.Core.Domain.Common;
using UsBankSystem.Core.Domain.Transactions;
using UsBankSystem.Core.Domain.Transfers;
using UsBankSystem.Core.Entities;
using UsBankSystem.Infrastructure.Persistence;
using Transfer = UsBankSystem.Core.Entities.Transfer;

namespace UsBankSystem.Api.Services;

public class PhoneAliasService(
    AppDbContext db,
    IKlikP2pClient klikP2p,
    InternalPaymentService internalPayment,
    FedNowGateway fedNowGateway,
    IOptions<PaymentSessionConfig> paymentConfig,
    IConfiguration cfg)
{
    public async Task<PhoneAliasResponse> RegisterAliasAsync(Guid userId, Guid accountId, string phone)
    {
        await GuardJuniorAsync(userId);

        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId && a.Status == AccountStatus.Active)
            ?? throw new KeyNotFoundException("Account not found or inactive");

        if (account.UserId != userId)
            throw new UnauthorizedAccessException("Account does not belong to the current user");

        var alreadyRegistered = await db.PhoneAliases.AnyAsync(p => p.AccountId == accountId && p.Status == PhoneAliasStatus.Active);
        if (alreadyRegistered)
            throw new InvalidOperationException("A phone alias is already registered for this account");

        var routingNumber = cfg["Bank:RoutingNumber"]
            ?? throw new InvalidOperationException("Bank routing number is not configured");

        var result = await klikP2p.RegisterAliasAsync(phone, routingNumber, account.AccountNumber);

        var alias = new PhoneAlias
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Phone = phone,
            KlikAliasId = result.AliasId,
            Status = PhoneAliasStatus.Active,
            RegisteredAt = result.RegisteredAt,
            CreatedAt = DateTime.UtcNow
        };
        db.PhoneAliases.Add(alias);
        await db.SaveChangesAsync();

        return MapToResponse(alias);
    }

    public async Task DeleteAliasAsync(Guid userId, Guid accountId)
    {
        await GuardJuniorAsync(userId);

        var alias = await db.PhoneAliases.FirstOrDefaultAsync(p => p.AccountId == accountId && p.Status == PhoneAliasStatus.Active)
            ?? throw new KeyNotFoundException("No active phone alias found for this account");

        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId)
            ?? throw new KeyNotFoundException("Account not found");

        if (account.UserId != userId)
            throw new UnauthorizedAccessException("Account does not belong to the current user");

        await klikP2p.DeleteAliasAsync(alias.Phone);

        alias.Status = PhoneAliasStatus.Deleted;
        await db.SaveChangesAsync();
    }

    public async Task<TransferResponse> SendToPhoneAsync(Guid userId, Guid fromAccountId, string phone, decimal amount, string currency, string? description)
    {
        await GuardJuniorAsync(userId);

        if (!CurrencyCode.IsValid(currency))
            throw new ArgumentException($"Unsupported currency '{currency}'");

        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == fromAccountId && a.Status == AccountStatus.Active)
            ?? throw new KeyNotFoundException("Source account not found or inactive");

        if (account.UserId != userId)
            throw new UnauthorizedAccessException("Account does not belong to the current user");

        var available = account.Balance - account.ReservedBalance;
        if (available < amount)
            throw new ArgumentException("Insufficient funds");

        var lookup = await klikP2p.LookupAliasAsync(phone);

        var ownRouting = cfg["Bank:RoutingNumber"];
        if (lookup.RoutingNumber == ownRouting)
        {
            return await internalPayment.CreateAsync(userId, new CreateInternalTransferRequest
            {
                FromAccountId = fromAccountId,
                ToAccountNumber = lookup.AccountNumber,
                Amount = amount,
                Currency = currency,
                Description = description ?? $"P2P transfer to {phone}"
            });
        }

        return await ExecuteExternalFedNowAsync(account, lookup, phone, amount, currency, description);
    }

    private async Task<TransferResponse> ExecuteExternalFedNowAsync(
        Account account, KlikP2pLookupResult lookup, string phone,
        decimal amount, string currency, string? description)
    {
        account.ReservedBalance += amount;

        var transfer = new Transfer
        {
            Id = Guid.NewGuid(),
            FromAccountId = account.Id,
            ToAccountId = null,
            ToAccountNumber = lookup.AccountNumber,
            Amount = amount,
            Currency = currency.ToUpperInvariant(),
            Channel = TransferChannel.FedNow,
            Status = TransferStatus.Pending,
            Description = description ?? $"P2P transfer to {phone}",
            RequiresApproval = false,
            CreatedAt = DateTime.UtcNow
        };
        db.Transfers.Add(transfer);
        await db.SaveChangesAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(paymentConfig.Value.FedNow.TimeoutSeconds));
        var gatewayResult = await fedNowGateway.SendAsync(new PaymentGatewayRequest(
            TransferId: transfer.Id,
            Amount: amount,
            Currency: transfer.Currency,
            Description: transfer.Description,
            Metadata: new Dictionary<string, string>
            {
                ["toPhone"] = phone,
                ["toAccountNumber"] = lookup.AccountNumber ?? string.Empty
            }
        ), cts.Token);

        if (!gatewayResult.Success)
        {
            transfer.Status = TransferStatus.Failed;
            account.ReservedBalance -= amount;
            await db.SaveChangesAsync();
            throw new InvalidOperationException(gatewayResult.Error ?? "FedNow gateway error");
        }

        account.Balance -= amount;
        account.ReservedBalance -= amount;
        transfer.Status = TransferStatus.Completed;
        transfer.CompletedAt = DateTime.UtcNow;
        transfer.ExternalReferenceId = gatewayResult.ExternalReferenceId;

        db.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Amount = amount,
            Type = TransactionType.Debit,
            Status = TransactionStatus.Completed,
            Description = transfer.Description ?? "P2P FedNow transfer",
            ReferenceId = transfer.Id.ToString(),
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        return PaymentServiceBase.MapToResponse(transfer);
    }

    private async Task GuardJuniorAsync(Guid userId)
    {
        var isJunior = await db.JuniorAccounts.AnyAsync(j => j.Account.UserId == userId);
        if (isJunior)
            throw new UnauthorizedAccessException("P2P is not available for junior accounts");
    }

    private static PhoneAliasResponse MapToResponse(PhoneAlias alias) => new()
    {
        Id = alias.Id,
        AccountId = alias.AccountId,
        Phone = alias.Phone,
        KlikAliasId = alias.KlikAliasId,
        Status = alias.Status,
        RegisteredAt = alias.RegisteredAt,
        CreatedAt = alias.CreatedAt
    };
}
