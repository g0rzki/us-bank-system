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

namespace UsBankSystem.Api.Services;

public class TransferService(
	AppDbContext db,
	AchGateway achGateway,
	RtpGateway rtpGateway,
	FedNowGateway fedNowGateway,
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
}
