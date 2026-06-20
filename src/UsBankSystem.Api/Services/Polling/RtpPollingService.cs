using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UsBankSystem.Api.Configuration;
using UsBankSystem.Api.Integrations.FedNow;
using UsBankSystem.Api.Integrations.Rtp;
using UsBankSystem.Core.Domain.Transactions;
using UsBankSystem.Core.Domain.Transfers;
using UsBankSystem.Core.Entities;
using UsBankSystem.Infrastructure.Persistence;
using Transfer = UsBankSystem.Core.Entities.Transfer;

namespace UsBankSystem.Api.Services.Polling;

public class RtpPollingService(
    IServiceScopeFactory scopeFactory,
    Pacs002Parser pacs002Parser,
    Pacs008Parser pacs008Parser,
    Pacs002Builder pacs002Builder,
    IOptions<PaymentSessionConfig> paymentConfig,
    ILogger<RtpPollingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = paymentConfig.Value.Rtp;
        var pollInterval = TimeSpan.FromSeconds(config.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var gateway = scope.ServiceProvider.GetRequiredService<RtpTchGateway>();
                var messages = await gateway.PollIncomingAsync(stoppingToken);

                foreach (var msg in messages)
                {
                    try
                    {
                        await ProcessMessageAsync(msg, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "RTP: failed to process queue message {QueueId} ({Type})", msg.QueueId, msg.Type);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "RTP polling iteration failed");
            }

            await Task.Delay(pollInterval, stoppingToken);
        }
    }

    private async Task ProcessMessageAsync(RtpQueueMessage msg, CancellationToken ct)
    {
        var xml = msg.Payload;

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (Exception ex)
        {
            logger.LogWarning("RTP: unparseable XML in queue message {QueueId}: {Error}", msg.QueueId, ex.Message);
            return;
        }

        var ns = doc.Root?.Name.Namespace;

        if (ns == Pacs002Parser.Ns)
        {
            await HandlePacs002Async(xml, ct);
        }
        else if (ns == Pacs008Parser.Ns)
        {
            await HandleIncomingPacs008Async(xml, ct);
        }
        else
        {
            logger.LogWarning("RTP: unrecognized XML namespace '{Namespace}' in queue message {QueueId}", ns?.NamespaceName, msg.QueueId);
        }
    }

    private async Task HandlePacs002Async(string xml, CancellationToken ct)
    {
        Pacs002Result result;
        try
        {
            result = pacs002Parser.Parse(xml);
        }
        catch (Exception ex)
        {
            logger.LogWarning("RTP: failed to parse pacs.002: {Error}", ex.Message);
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var transferService = scope.ServiceProvider.GetRequiredService<TransferService>();

        var transfer = await FindTransferByEndToEndId(db, result.OriginalEndToEndId, TransferChannel.Rtp, ct);

        if (transfer is null)
        {
            logger.LogWarning("RTP: pacs.002 references unknown transfer (EndToEndId: '{EndToEndId}')", result.OriginalEndToEndId);
            return;
        }

        if (transfer.Status != TransferStatus.Pending)
        {
            logger.LogWarning("RTP: pacs.002 for transfer {TransferId} ignored — already '{Status}'", transfer.Id, transfer.Status);
            return;
        }

        switch (result.TransactionStatus)
        {
            case "ACCP":
                await transferService.ProcessWebhookAsync(transfer.Id, TransferStatus.Completed, result.AccountServicerRef, ct);
                logger.LogInformation("RTP: transfer {TransferId} completed (ACCP)", transfer.Id);
                break;

            case "RJCT":
                await transferService.ProcessWebhookAsync(transfer.Id, TransferStatus.Rejected, null, ct);
                logger.LogInformation("RTP: transfer {TransferId} rejected (RJCT)", transfer.Id);
                break;

            case "PDNG":
            case "ACTC":
                logger.LogInformation("RTP: transfer {TransferId} intermediate status '{Status}', staying Pending", transfer.Id, result.TransactionStatus);
                break;

            default:
                logger.LogWarning("RTP: pacs.002 unknown TxSts '{Status}' for transfer {TransferId}", result.TransactionStatus, transfer.Id);
                break;
        }
    }

    private async Task HandleIncomingPacs008Async(string xml, CancellationToken ct)
    {
        IncomingPacs008 incoming;
        try
        {
            incoming = pacs008Parser.Parse(xml);
        }
        catch (Exception ex)
        {
            logger.LogWarning("RTP: failed to parse incoming pacs.008: {Error}", ex.Message);
            return;
        }

        var config = paymentConfig.Value.Rtp;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var gateway = scope.ServiceProvider.GetRequiredService<RtpTchGateway>();

        var alreadyExists = await db.Transfers.AnyAsync(
            t => t.ExternalReferenceId == incoming.EndToEndId && t.Channel == TransferChannel.Rtp, ct);
        if (alreadyExists)
        {
            logger.LogWarning("RTP: duplicate incoming pacs.008 EndToEndId '{EndToEndId}', skipping", incoming.EndToEndId);
            return;
        }

        var account = await db.Accounts.FirstOrDefaultAsync(
            a => a.AccountNumber == incoming.CreditorAccountNumber && a.Status == "active", ct);

        if (account is null)
        {
            logger.LogWarning("RTP: incoming pacs.008 for unknown account '{AccountNumber}'. Sending settle RJCT", incoming.CreditorAccountNumber);
            await SendSettleResponseAsync(incoming, "RJCT", config, gateway, ct);
            return;
        }

        account.Balance += incoming.Amount;

        var transfer = new Transfer
        {
            Id = Guid.NewGuid(),
            FromAccountId = null,
            ToAccountId = account.Id,
            ToAccountNumber = incoming.CreditorAccountNumber,
            Amount = incoming.Amount,
            Currency = incoming.Currency,
            Channel = TransferChannel.Rtp,
            Status = TransferStatus.Completed,
            ExternalReferenceId = incoming.EndToEndId,
            Description = $"Incoming RTP from {incoming.DebtorName}",
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };
        db.Transfers.Add(transfer);

        db.Transactions.Add(new Transaction
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Amount = incoming.Amount,
            Type = TransactionType.Credit,
            Status = TransactionStatus.Completed,
            Description = transfer.Description,
            ReferenceId = transfer.Id.ToString(),
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("RTP: incoming transfer {TransferId} credited to account {AccountNumber}, amount {Amount} {Currency}",
            transfer.Id, account.AccountNumber, incoming.Amount, incoming.Currency);

        await SendSettleResponseAsync(incoming, "ACCP", config, gateway, ct);
    }

    private async Task SendSettleResponseAsync(IncomingPacs008 incoming, string status, RtpConfig config, RtpTchGateway gateway, CancellationToken ct)
    {
        var responseXml = pacs002Builder.Build(new Pacs002Data(
            MsgId: $"MSG-{DateTime.UtcNow:yyyyMMdd}-STS-{Guid.NewGuid():N}",
            OriginalMsgId: incoming.MsgId,
            OriginalEndToEndId: incoming.EndToEndId,
            Status: status,
            AccountServicerRef: $"REF-{Guid.NewGuid():N}",
            Amount: incoming.Amount,
            Currency: incoming.Currency,
            DebtorBankName: incoming.DebtorBankName,
            DebtorBankRtn: incoming.DebtorBankRtn,
            DebtorName: incoming.DebtorName,
            DebtorAccountNumber: incoming.DebtorAccountNumber,
            CreditorBankName: config.BankLegalName,
            CreditorBankRtn: config.BankRtn,
            CreditorName: incoming.CreditorName,
            CreditorAccountNumber: incoming.CreditorAccountNumber
        ));

        var ok = await gateway.SettleAsync(responseXml, ct);
        if (!ok)
            logger.LogWarning("RTP: failed to send settle {Status} for MsgId '{MsgId}'", status, incoming.MsgId);
        else
            logger.LogInformation("RTP: sent settle {Status} for MsgId '{MsgId}'", status, incoming.MsgId);
    }

    private static async Task<Transfer?> FindTransferByEndToEndId(AppDbContext db, string endToEndId, string channel, CancellationToken ct)
    {
        if (endToEndId.StartsWith("E2E-") && endToEndId.Length >= 36 && Guid.TryParse(endToEndId[4..], out var id))
        {
            var transfer = await db.Transfers.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (transfer is not null) return transfer;
        }

        return await db.Transfers.FirstOrDefaultAsync(
            t => t.ExternalReferenceId == endToEndId && t.Channel == channel, ct);
    }
}
