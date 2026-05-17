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

namespace UsBankSystem.Api.Services;

public class TransferService(
	AppDbContext db,
	AchGateway achGateway,
	RtpGateway rtpGateway,
	FedNowGateway fedNowGateway,
	SwiftGateway swiftGateway,
	IOptions<PaymentSessionConfig> paymentConfig)
{
    public async Task<TransferResponse> CreateInternalAsync(Guid userId, CreateInternalTransferRequest request)
    {
        if (!CurrencyCode.IsValid(request.Currency))
            throw new ArgumentException($"Unsupported currency '{request.Currency}'");

        var fromAccount = await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.FromAccountId && a.UserId == userId && a.Status == AccountStatus.Active)
            ?? throw new KeyNotFoundException("Source account not found or inactive");

        var toAccount = await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.ToAccountId && a.Status == AccountStatus.Active)
            ?? throw new KeyNotFoundException("Destination account not found or inactive");

        if (fromAccount.Id == toAccount.Id)
            throw new ArgumentException("Cannot transfer to the same account");

        // Sprawdź czy konto źródłowe to konto junior
        var isJuniorAccount = false; // TODO: US-30 - sprawdzenie konta junior

        var availableBalance = fromAccount.Balance - fromAccount.ReservedBalance;
        if (availableBalance < request.Amount)
            throw new ArgumentException("Insufficient funds");

        // Sprawdź dzienny limit transferów
        var dailyLimit = await GetDailyTransferLimitAsync(fromAccount.Id);
        var todayTotal = await GetTodayTransferTotalAsync(fromAccount.Id);
        if (dailyLimit.HasValue && todayTotal + request.Amount > dailyLimit.Value)
            throw new ArgumentException($"Daily transfer limit exceeded. Limit: {dailyLimit}, used: {todayTotal}, requested: {request.Amount}");

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
            fromAccount.Balance -= request.Amount;
            toAccount.Balance += request.Amount;
            transfer.Status = TransferStatus.Completed;
            transfer.CompletedAt = DateTime.UtcNow;

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
            fromAccount.ReservedBalance += request.Amount;
        }

        db.Transfers.Add(transfer);
        await db.SaveChangesAsync();

        return new TransferResponse
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
        };
    }

    public async Task<TransferResponse> CreateAchAsync(Guid userId, CreateAchTransferRequest request)
    {
        if (!CurrencyCode.IsValid(request.Currency))
            throw new ArgumentException($"Unsupported currency '{request.Currency}'");

        var fromAccount = await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.FromAccountId && a.UserId == userId && a.Status == AccountStatus.Active)
            ?? throw new KeyNotFoundException("Source account not found or inactive");

        var availableBalance = fromAccount.Balance - fromAccount.ReservedBalance;
        if (availableBalance < request.Amount)
            throw new ArgumentException("Insufficient funds");

        var config = paymentConfig.Value.Ach;
        var now = DateTime.UtcNow;
        var cutoff = new DateTime(now.Year, now.Month, now.Day, config.CutoffHour, 0, 0, DateTimeKind.Utc);
        var nextBatch = now > cutoff ? cutoff.AddDays(1) : cutoff;

        fromAccount.ReservedBalance += request.Amount;

        var transfer = new Transfer
        {
            Id = Guid.NewGuid(),
            FromAccountId = fromAccount.Id,
            ToAccountId = null,
            Amount = request.Amount,
            Currency = request.Currency.ToUpperInvariant(),
            Channel = TransferChannel.Ach,
            Status = TransferStatus.Pending,
            Description = request.Description,
            RequiresApproval = false,
            CreatedAt = DateTime.UtcNow
        };

        db.Transfers.Add(transfer);

        db.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(),
            AccountId = fromAccount.Id,
            Amount = request.Amount,
            Type = TransactionType.Debit,
            Status = TransactionStatus.Pending,
            Description = request.Description ?? "ACH transfer",
            ReferenceId = transfer.Id.ToString(),
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var gatewayResult = await achGateway.SendAsync(new(
            TransferId: transfer.Id,
            Amount: transfer.Amount,
            Currency: transfer.Currency,
            Description: transfer.Description,
            Metadata: new Dictionary<string, string>
            {
                ["toRoutingNumber"] = request.ToRoutingNumber,
                ["toAccountNumber"] = request.ToAccountNumber
            }
        ));

        if (!gatewayResult.Success)
        {
            transfer.Status = TransferStatus.Failed;
            fromAccount.ReservedBalance -= request.Amount;
            await db.SaveChangesAsync();
            throw new ArgumentException(gatewayResult.Error ?? "ACH gateway error");
        }

        transfer.ExternalReferenceId = gatewayResult.ExternalReferenceId;
        await db.SaveChangesAsync();

        return new TransferResponse
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
            RequiresApproval = transfer.RequiresApproval,
            EstimatedSettlement = nextBatch
        };
    }

	public async Task<TransferResponse> CreateRtpAsync(Guid userId, CreateRtpTransferRequest request)
	{
    	if (!CurrencyCode.IsValid(request.Currency))
        	throw new ArgumentException($"Unsupported currency '{request.Currency}'");

    	var fromAccount = await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.FromAccountId && a.UserId == userId && a.Status == AccountStatus.Active)
        	?? throw new KeyNotFoundException("Source account not found or inactive");

    	var toAccount = await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.ToAccountId && a.Status == AccountStatus.Active)
        	?? throw new KeyNotFoundException("Destination account not found or inactive");

    	if (fromAccount.Id == toAccount.Id)
        	throw new ArgumentException("Cannot transfer to the same account");

    	var availableBalance = fromAccount.Balance - fromAccount.ReservedBalance;
    	if (availableBalance < request.Amount)
        	throw new ArgumentException("Insufficient funds");

    	var timeout = paymentConfig.Value.Rtp.TimeoutSeconds;

    	fromAccount.ReservedBalance += request.Amount;

    	var transfer = new Transfer
    	{
        	Id = Guid.NewGuid(),
        	FromAccountId = fromAccount.Id,
        	ToAccountId = toAccount.Id,
        	Amount = request.Amount,
        	Currency = request.Currency.ToUpperInvariant(),
        	Channel = TransferChannel.Rtp,
        	Status = TransferStatus.Pending,
        	Description = request.Description,
        	RequiresApproval = false,
        	CreatedAt = DateTime.UtcNow
    	};

    	db.Transfers.Add(transfer);
    	await db.SaveChangesAsync();

    	using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
    	var gatewayResult = await rtpGateway.SendAsync(new(
        	TransferId: transfer.Id,
        	Amount: transfer.Amount,
        	Currency: transfer.Currency,
        	Description: transfer.Description,
        	Metadata: new Dictionary<string, string>
        	{
            	["toAccountId"] = toAccount.Id.ToString()
        	}
    	), cts.Token);

    	if (!gatewayResult.Success)
    	{
        	transfer.Status = TransferStatus.Failed;
        	fromAccount.ReservedBalance -= request.Amount;
        	await db.SaveChangesAsync();
        	throw new ArgumentException(gatewayResult.Error ?? "RTP gateway error");
    	}

    	fromAccount.Balance -= request.Amount;
    	fromAccount.ReservedBalance -= request.Amount;
    	toAccount.Balance += request.Amount;
    	transfer.Status = TransferStatus.Completed;
    	transfer.CompletedAt = DateTime.UtcNow;
    	transfer.ExternalReferenceId = gatewayResult.ExternalReferenceId;

    	db.Transactions.AddRange(
        	new Transaction
        	{
            	Id = Guid.NewGuid(),
            	AccountId = fromAccount.Id,
            	Amount = request.Amount,
            	Type = TransactionType.Debit,
            	Status = TransactionStatus.Completed,
            	Description = request.Description ?? "RTP transfer",
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
            	Description = request.Description ?? "RTP transfer",
            	ReferenceId = transfer.Id.ToString(),
            	CreatedAt = DateTime.UtcNow
        	}
    	);

    	await db.SaveChangesAsync();

    	return new TransferResponse
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
    	};
	}

	public async Task<TransferResponse> CreateFedNowAsync(Guid userId, CreateFedNowTransferRequest request)
	{
    	if (!CurrencyCode.IsValid(request.Currency))
        	throw new ArgumentException($"Unsupported currency '{request.Currency}'");

    	var fromAccount = await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.FromAccountId && a.UserId == userId && a.Status == AccountStatus.Active)
        	?? throw new KeyNotFoundException("Source account not found or inactive");

    	var toAccount = await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.ToAccountId && a.Status == AccountStatus.Active)
        	?? throw new KeyNotFoundException("Destination account not found or inactive");

    	if (fromAccount.Id == toAccount.Id)
        	throw new ArgumentException("Cannot transfer to the same account");

    	var availableBalance = fromAccount.Balance - fromAccount.ReservedBalance;
    	if (availableBalance < request.Amount)
        	throw new ArgumentException("Insufficient funds");

    	var timeout = paymentConfig.Value.FedNow.TimeoutSeconds;

    	fromAccount.ReservedBalance += request.Amount;

    	var transfer = new Transfer
    	{
        	Id = Guid.NewGuid(),
        	FromAccountId = fromAccount.Id,
        	ToAccountId = toAccount.Id,
        	Amount = request.Amount,
        	Currency = request.Currency.ToUpperInvariant(),
        	Channel = TransferChannel.FedNow,
        	Status = TransferStatus.Pending,
        	Description = request.Description,
        	RequiresApproval = false,
        	CreatedAt = DateTime.UtcNow
    	};

    	db.Transfers.Add(transfer);
    	await db.SaveChangesAsync();

    	using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
    	var gatewayResult = await fedNowGateway.SendAsync(new(
        	TransferId: transfer.Id,
        	Amount: transfer.Amount,
        	Currency: transfer.Currency,
        	Description: transfer.Description,
        	Metadata: new Dictionary<string, string>
        	{
            	["toAccountId"] = toAccount.Id.ToString()
        	}
    	), cts.Token);

    	if (!gatewayResult.Success)
    	{
        	transfer.Status = TransferStatus.Failed;
        	fromAccount.ReservedBalance -= request.Amount;
        	await db.SaveChangesAsync();
        	throw new ArgumentException(gatewayResult.Error ?? "FedNow gateway error");
    	}

    	fromAccount.Balance -= request.Amount;
    	fromAccount.ReservedBalance -= request.Amount;
    	toAccount.Balance += request.Amount;
    	transfer.Status = TransferStatus.Completed;
    	transfer.CompletedAt = DateTime.UtcNow;
    	transfer.ExternalReferenceId = gatewayResult.ExternalReferenceId;

    	db.Transactions.AddRange(
        	new Transaction
        	{
            	Id = Guid.NewGuid(),
            	AccountId = fromAccount.Id,
            	Amount = request.Amount,
            	Type = TransactionType.Debit,
            	Status = TransactionStatus.Completed,
            	Description = request.Description ?? "FedNow transfer",
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
            	Description = request.Description ?? "FedNow transfer",
            	ReferenceId = transfer.Id.ToString(),
            	CreatedAt = DateTime.UtcNow
        	}
    	);

    	await db.SaveChangesAsync();

    	return new TransferResponse
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
    	};
	}

    public async Task<List<PendingApprovalTransferResponse>> GetPendingApprovalAsync(Guid userId)
    {
        return await db.Transfers
            .Where(t =>
                t.Status == TransferStatus.PendingApproval &&
                db.JuniorAccounts.Any(j =>
                    j.AccountId == t.FromAccountId &&
                    j.ParentAccount.UserId == userId))
            .OrderBy(t => t.CreatedAt)
            .Select(t => new PendingApprovalTransferResponse
            {
                Id = t.Id,
                FromAccountId = t.FromAccountId,
                FromAccountNumber = t.FromAccount.AccountNumber,
                ToAccountId = t.ToAccountId,
                Amount = t.Amount,
                Currency = t.Currency,
                Channel = t.Channel,
                Description = t.Description,
                CreatedAt = t.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<List<TransferResponse>> GetAllAsync(Guid userId)
    {
        return await db.Transfers
            .Include(t => t.FromAccount)
            .Where(t => t.FromAccount.UserId == userId || db.Accounts.Any(a => a.Id == t.ToAccountId && a.UserId == userId))
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new TransferResponse
            {
                Id = t.Id,
                FromAccountId = t.FromAccountId,
                ToAccountId = t.ToAccountId,
                Amount = t.Amount,
                Currency = t.Currency,
                Channel = t.Channel,
                Status = t.Status,
                Description = t.Description,
                CreatedAt = t.CreatedAt,
                CompletedAt = t.CompletedAt,
                RequiresApproval = t.RequiresApproval,
                EstimatedSettlement = null
            })
            .ToListAsync();
    }

    public async Task<TransferResponse> CreateSwiftAsync(Guid userId, CreateSwiftTransferRequest request)
    {
        SwiftRequestValidator.Validate(request.Iban, request.Bic, request.ChargeBearer, request.Currency, request.ValueDate);

        var valueDate = SwiftRequestValidator.ResolveValueDate(request.ValueDate);

        var fromAccount = await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.FromAccountId && a.UserId == userId && a.Status == AccountStatus.Active)
            ?? throw new KeyNotFoundException("Source account not found or inactive");

        var availableBalance = fromAccount.Balance - fromAccount.ReservedBalance;
        if (availableBalance < request.Amount)
            throw new ArgumentException("Insufficient funds");

        var todaySwiftTotal = await GetTodayTransferTotalByChannelAsync(fromAccount.Id, TransferChannel.Swift);
        SwiftRequestValidator.ValidateDailyLimit(todaySwiftTotal, request.Amount, paymentConfig.Value.Swift.DailyLimitPerAccount);

        fromAccount.ReservedBalance += request.Amount;

        var transfer = new Transfer
        {
            Id = Guid.NewGuid(),
            FromAccountId = fromAccount.Id,
            ToAccountId = null,
            Amount = request.Amount,
            Currency = request.Currency.ToUpperInvariant(),
            Channel = TransferChannel.Swift,
            Status = TransferStatus.Pending,
            Description = request.Description,
            RequiresApproval = false,
            CreatedAt = DateTime.UtcNow
        };

        db.Transfers.Add(transfer);

        db.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(),
            AccountId = fromAccount.Id,
            Amount = request.Amount,
            Type = TransactionType.Debit,
            Status = TransactionStatus.Pending,
            Description = request.Description ?? "SWIFT transfer",
            ReferenceId = transfer.Id.ToString(),
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        // SWIFT settlement takes 1-5 business days — final status comes via webhook
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
            await db.SaveChangesAsync();
            throw new ArgumentException(gatewayResult.Error ?? "SWIFT gateway error");
        }

        transfer.ExternalReferenceId = gatewayResult.ExternalReferenceId;
        await db.SaveChangesAsync();

        return new TransferResponse
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
        };
    }

    public async Task<TransferStatusResponse> GetStatusAsync(Guid userId, Guid transferId)
    {
        var transfer = await db.Transfers
            .Include(t => t.FromAccount)
            .FirstOrDefaultAsync(t => t.Id == transferId)
            ?? throw new KeyNotFoundException("Transfer not found");

        if (transfer.FromAccount.UserId != userId)
            throw new UnauthorizedAccessException("Access denied");

        return new TransferStatusResponse
        {
            TransferId = transfer.Id,
            Status = transfer.Status,
            Channel = transfer.Channel,
            CreatedAt = transfer.CreatedAt,
            CompletedAt = transfer.CompletedAt,
            ExternalReferenceId = transfer.ExternalReferenceId
        };
    }

    public async Task ProcessWebhookAsync(Guid transferId, string status, string? referenceId)
    {
        var transfer = await db.Transfers
            .Include(t => t.FromAccount)
            .Include(t => t.ToAccount)
            .FirstOrDefaultAsync(t => t.Id == transferId)
            ?? throw new KeyNotFoundException("Transfer not found");

        if (transfer.Status != TransferStatus.Pending)
            throw new ArgumentException("Transfer is not in pending state");

        if (status == TransferStatus.Completed)
        {
            transfer.FromAccount.Balance -= transfer.Amount;
            transfer.FromAccount.ReservedBalance -= transfer.Amount;
            if (transfer.ToAccount is not null)
                transfer.ToAccount.Balance += transfer.Amount;

            transfer.Status = TransferStatus.Completed;
            transfer.CompletedAt = DateTime.UtcNow;
            transfer.ExternalReferenceId = referenceId ?? transfer.ExternalReferenceId;

            var existingDebit = await db.Transactions.FirstOrDefaultAsync(t =>
                t.ReferenceId == transfer.Id.ToString() && t.Type == TransactionType.Debit);

            if (existingDebit is not null)
            {
                existingDebit.Status = TransactionStatus.Completed;
            }
            else
            {
                db.Transactions.Add(new Transaction
                {
                    Id = Guid.NewGuid(),
                    AccountId = transfer.FromAccountId,
                    Amount = transfer.Amount,
                    Type = TransactionType.Debit,
                    Status = TransactionStatus.Completed,
                    Description = transfer.Description ?? $"{transfer.Channel} transfer",
                    ReferenceId = transfer.Id.ToString(),
                    CreatedAt = DateTime.UtcNow
                });
            }

            if (transfer.ToAccountId is not null)
            {
                db.Transactions.Add(new Transaction
                {
                    Id = Guid.NewGuid(),
                    AccountId = transfer.ToAccountId.Value,
                    Amount = transfer.Amount,
                    Type = TransactionType.Credit,
                    Status = TransactionStatus.Completed,
                    Description = transfer.Description ?? $"{transfer.Channel} transfer",
                    ReferenceId = transfer.Id.ToString(),
                    CreatedAt = DateTime.UtcNow
                });
            }
        }
        else if (status == TransferStatus.Failed)
        {
            transfer.FromAccount.ReservedBalance -= transfer.Amount;
            transfer.Status = TransferStatus.Failed;
        }
        else
        {
            throw new ArgumentException($"Invalid status '{status}'");
        }

        await db.SaveChangesAsync();
    }

    private async Task<decimal?> GetDailyTransferLimitAsync(Guid accountId)
    {
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

    private async Task<decimal> GetTodayTransferTotalByChannelAsync(Guid accountId, string channel)
    {
        var today = DateTime.UtcNow.Date;
        return await db.Transfers
            .Where(t => t.FromAccountId == accountId
                        && t.Channel == channel
                        && t.CreatedAt >= today
                        && t.Status != TransferStatus.Rejected
                        && t.Status != TransferStatus.Failed)
            .SumAsync(t => t.Amount);
    }
}
