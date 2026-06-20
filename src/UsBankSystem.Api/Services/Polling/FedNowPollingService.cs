using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UsBankSystem.Api.Configuration;
using UsBankSystem.Api.Integrations.FedNow;
using UsBankSystem.Core.Domain.Transactions;
using UsBankSystem.Core.Domain.Transfers;
using UsBankSystem.Core.Entities;
using UsBankSystem.Infrastructure.Persistence;
using Transfer = UsBankSystem.Core.Entities.Transfer;

namespace UsBankSystem.Api.Services.Polling;

public class FedNowPollingService(
    IServiceScopeFactory scopeFactory,
    Pacs002Parser pacs002Parser,
    Pacs008Parser pacs008Parser,
    Pain013Parser pain013Parser,
    Pacs008Builder pacs008Builder,
    Pacs002Builder pacs002Builder,
    Pain014Builder pain014Builder,
    IOptions<PaymentSessionConfig> paymentConfig,
    ILogger<FedNowPollingService> logger) : BackgroundService
{
    private static readonly XNamespace NsPacs002 = Pacs002Parser.Ns;
    private static readonly XNamespace NsPacs008 = Pacs008Parser.Ns;
    private static readonly XNamespace NsPain013 = Pain013Parser.Ns;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(paymentConfig.Value.FedNow.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var pollScope = scopeFactory.CreateScope();
                var mqGateway = pollScope.ServiceProvider.GetRequiredService<FedNowMqGateway>();
                var xmlBytes = await mqGateway.PollIncomingAsync(stoppingToken);
                if (xmlBytes is not null)
                {
                    await ProcessMessageAsync(xmlBytes, stoppingToken);
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "FedNow polling iteration failed");
            }

            await Task.Delay(pollInterval, stoppingToken);
        }
    }

    internal async Task ProcessMessageAsync(byte[] xmlBytes, CancellationToken ct)
    {
        var xml = Encoding.UTF8.GetString(xmlBytes);

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (Exception ex)
        {
            logger.LogWarning("FedNow: received unparseable XML from /FIFO/out: {Error}. Content: {Content}",
                ex.Message, Truncate(xml));
            return;
        }

        var root = doc.Root;
        if (root is null)
        {
            logger.LogWarning("FedNow: received XML with null root. Content: {Content}", Truncate(xml));
            return;
        }

        var ns = root.Name.Namespace;

        if (ns == NsPacs002)
        {
            await HandlePacs002Async(xml, ct);
        }
        else if (ns == NsPacs008)
        {
            await HandleIncomingPacs008Async(xml, xmlBytes, ct);
        }
        else if (ns == NsPain013)
        {
            await HandlePain013Async(xml, xmlBytes, ct);
        }
        else
        {
            logger.LogWarning("FedNow: unrecognized XML namespace '{Namespace}' from /FIFO/out. Content: {Content}",
                ns.NamespaceName, Truncate(xml));
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
            logger.LogWarning("FedNow: failed to parse pacs.002: {Error}. Content: {Content}",
                ex.Message, Truncate(xml));
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var transferService = scope.ServiceProvider.GetRequiredService<TransferService>();

        Transfer? transfer = null;

        var transferId = ExtractTransferIdFromEndToEndId(result.OriginalEndToEndId);
        if (transferId is not null)
        {
            transfer = await db.Transfers.FirstOrDefaultAsync(t => t.Id == transferId.Value, ct);
        }

        if (transfer is null)
        {
            transfer = await db.Transfers.FirstOrDefaultAsync(
                t => t.ExternalReferenceId == result.OriginalEndToEndId && t.Channel == TransferChannel.FedNow, ct);
        }

        if (transfer is null)
        {
            logger.LogWarning("FedNow: pacs.002 references unknown transfer (EndToEndId: '{EndToEndId}', OriginalMsgId: '{MsgId}'). Content: {Content}",
                result.OriginalEndToEndId, result.OriginalMsgId, Truncate(xml));
            return;
        }

        if (transfer.Status != TransferStatus.Pending)
        {
            logger.LogWarning("FedNow: pacs.002 for transfer {TransferId} ignored — already in status '{Status}' (idempotency guard)",
                transfer.Id, transfer.Status);
            return;
        }

        switch (result.TransactionStatus)
        {
            case "ACCP":
                await transferService.ProcessWebhookAsync(transfer.Id, TransferStatus.Completed, result.AccountServicerRef);
                logger.LogInformation("FedNow: transfer {TransferId} completed (ACCP)", transfer.Id);
                break;

            case "RJCT":
                await transferService.ProcessWebhookAsync(transfer.Id, TransferStatus.Rejected, null);
                logger.LogInformation("FedNow: transfer {TransferId} rejected (RJCT)", transfer.Id);
                break;

            case "BLCK":
                await transferService.ProcessWebhookAsync(transfer.Id, TransferStatus.Failed, null);
                logger.LogInformation("FedNow: transfer {TransferId} failed (BLCK)", transfer.Id);
                break;

            case "PDNG":
            case "ACTC":
                logger.LogInformation("FedNow: transfer {TransferId} received intermediate status '{Status}', staying Pending",
                    transfer.Id, result.TransactionStatus);
                break;

            default:
                logger.LogWarning("FedNow: pacs.002 has unknown TxSts '{Status}' for transfer {TransferId}. Content: {Content}",
                    result.TransactionStatus, transfer.Id, Truncate(xml));
                break;
        }
    }

    private async Task HandleIncomingPacs008Async(string xml, byte[] xmlBytes, CancellationToken ct)
    {
        IncomingPacs008 incoming;
        try
        {
            incoming = pacs008Parser.Parse(xml);
        }
        catch (Exception ex)
        {
            logger.LogWarning("FedNow: failed to parse incoming pacs.008: {Error}. Content: {Content}",
                ex.Message, Truncate(xml));
            return;
        }

        var config = paymentConfig.Value.FedNow;

        if (incoming.CreditorBankRtn != config.BankRtn)
        {
            logger.LogWarning("FedNow: incoming pacs.008 creditor RTN '{Rtn}' does not match our RTN '{OurRtn}'. MsgId: {MsgId}",
                incoming.CreditorBankRtn, config.BankRtn, incoming.MsgId);
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var mqGateway = scope.ServiceProvider.GetRequiredService<FedNowMqGateway>();

        var alreadyExists = await db.Transfers.AnyAsync(
            t => t.ExternalReferenceId == incoming.EndToEndId && t.Channel == TransferChannel.FedNow, ct);
        if (alreadyExists)
        {
            logger.LogWarning("FedNow: duplicate incoming pacs.008 with EndToEndId '{EndToEndId}', skipping (idempotency guard)",
                incoming.EndToEndId);
            return;
        }

        var account = await db.Accounts.FirstOrDefaultAsync(
            a => a.AccountNumber == incoming.CreditorAccountNumber && a.Status == "active", ct);

        if (account is null)
        {
            logger.LogWarning("FedNow: incoming pacs.008 for unknown account '{AccountNumber}'. Sending RJCT. MsgId: {MsgId}",
                incoming.CreditorAccountNumber, incoming.MsgId);
            await SendPacs002ResponseAsync(incoming, "RJCT", config, mqGateway, ct);
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
            Channel = TransferChannel.FedNow,
            Status = TransferStatus.Completed,
            ExternalReferenceId = incoming.EndToEndId,
            Description = incoming.Description ?? $"Incoming FedNow from {incoming.DebtorName}",
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
        logger.LogInformation("FedNow: incoming transfer {TransferId} credited to account {AccountNumber}, amount {Amount} {Currency}",
            transfer.Id, account.AccountNumber, incoming.Amount, incoming.Currency);

        await SendPacs002ResponseAsync(incoming, "ACCP", config, mqGateway, ct);
    }

    private async Task HandlePain013Async(string xml, byte[] xmlBytes, CancellationToken ct)
    {
        IncomingPain013 incoming;
        try
        {
            incoming = pain013Parser.Parse(xml);
        }
        catch (Exception ex)
        {
            logger.LogWarning("FedNow: failed to parse incoming pain.013: {Error}. Content: {Content}",
                ex.Message, Truncate(xml));
            return;
        }

        var config = paymentConfig.Value.FedNow;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var mqGateway = scope.ServiceProvider.GetRequiredService<FedNowMqGateway>();

        var alreadyExists = await db.Transfers.AnyAsync(
            t => t.ExternalReferenceId == incoming.EndToEndId && t.Channel == TransferChannel.FedNow, ct);
        if (alreadyExists)
        {
            logger.LogWarning("FedNow: duplicate incoming pain.013 with EndToEndId '{EndToEndId}', skipping (idempotency guard)",
                incoming.EndToEndId);
            return;
        }

        var debtorAccount = await db.Accounts
            .Include(a => a.User)
            .FirstOrDefaultAsync(
                a => a.AccountNumber == incoming.DebtorAccountNumber && a.Status == "active", ct);

        if (debtorAccount is null)
        {
            logger.LogWarning("FedNow: pain.013 references unknown debtor account '{AccountNumber}'. Sending RJCT pain.014. MsgId: {MsgId}",
                incoming.DebtorAccountNumber, incoming.MsgId);
            await SendPain014ResponseAsync(incoming, "RJCT", mqGateway, ct);
            return;
        }

        var availableBalance = debtorAccount.Balance - debtorAccount.ReservedBalance;
        if (availableBalance < incoming.Amount)
        {
            logger.LogWarning("FedNow: pain.013 insufficient funds for account '{AccountNumber}'. Available: {Available}, Requested: {Amount}. Sending RJCT pain.014",
                incoming.DebtorAccountNumber, availableBalance, incoming.Amount);
            await SendPain014ResponseAsync(incoming, "RJCT", mqGateway, ct);
            return;
        }

        if (debtorAccount.User is null)
        {
            logger.LogWarning("FedNow: pain.013 debtor account '{AccountNumber}' has no associated user. Sending RJCT pain.014",
                incoming.DebtorAccountNumber);
            await SendPain014ResponseAsync(incoming, "RJCT", mqGateway, ct);
            return;
        }

        debtorAccount.ReservedBalance += incoming.Amount;

        var msgId = $"MSG-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}";

        var transfer = new Transfer
        {
            Id = Guid.NewGuid(),
            FromAccountId = debtorAccount.Id,
            ToAccountId = null,
            ToAccountNumber = incoming.CreditorAccountNumber,
            Amount = incoming.Amount,
            Currency = incoming.Currency,
            Channel = TransferChannel.FedNow,
            Status = TransferStatus.Pending,
            ExternalReferenceId = incoming.EndToEndId,
            Description = incoming.Description ?? $"Payment request from {incoming.CreditorName}",
            CreatedAt = DateTime.UtcNow
        };
        db.Transfers.Add(transfer);
        await db.SaveChangesAsync(ct);

        await SendPain014ResponseAsync(incoming, "ACCP", mqGateway, ct);

        var senderName = $"{debtorAccount.User.FirstName} {debtorAccount.User.LastName}";

        var pacs008Xml = pacs008Builder.Build(new Pacs008Data(
            MsgId: msgId,
            EndToEndId: incoming.EndToEndId,
            Amount: incoming.Amount,
            Currency: incoming.Currency,
            DebtorBankName: config.BankLegalName,
            DebtorBankRtn: config.BankRtn,
            DebtorName: senderName,
            DebtorAccountNumber: incoming.DebtorAccountNumber,
            CreditorBankName: incoming.CreditorBankName,
            CreditorBankRtn: incoming.CreditorBankRtn,
            CreditorName: incoming.CreditorName,
            CreditorAccountNumber: incoming.CreditorAccountNumber,
            Description: transfer.Description
        ));

        var (success, error) = await mqGateway.SendXmlAsync(Encoding.UTF8.GetBytes(pacs008Xml), ct);
        if (!success)
        {
            transfer.Status = TransferStatus.Failed;
            debtorAccount.ReservedBalance -= incoming.Amount;
            await db.SaveChangesAsync(ct);
            await SendPain014ResponseAsync(incoming, "RJCT", mqGateway, ct);
            logger.LogWarning("FedNow: failed to send pacs.008 for pain.013 EndToEndId '{EndToEndId}': {Error}. Sent pain.014 RJCT to compensate prior ACCP",
                incoming.EndToEndId, error);
            return;
        }

        logger.LogInformation("FedNow: pain.013 processed, pacs.008 sent for EndToEndId '{EndToEndId}', transfer {TransferId} pending",
            incoming.EndToEndId, transfer.Id);
    }

    private async Task SendPacs002ResponseAsync(IncomingPacs008 incoming, string status, FedNowConfig config, FedNowMqGateway mqGateway, CancellationToken ct)
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

        var (success, error) = await mqGateway.SendXmlAsync(Encoding.UTF8.GetBytes(responseXml), ct);
        if (!success)
        {
            logger.LogWarning("FedNow: failed to send pacs.002 {Status} response for MsgId '{MsgId}': {Error}",
                status, incoming.MsgId, error);
        }
        else
        {
            logger.LogInformation("FedNow: sent pacs.002 {Status} for MsgId '{MsgId}'", status, incoming.MsgId);
        }
    }

    private async Task SendPain014ResponseAsync(IncomingPain013 incoming, string status, FedNowMqGateway mqGateway, CancellationToken ct)
    {
        // pain.014 confirms receipt of payment request (pain.013), NOT settlement.
        // Does not change transfer status or book funds.
        var responseXml = pain014Builder.Build(new Pain014Data(
            MsgId: $"MSG-{DateTime.UtcNow:yyyyMMdd}-RPT-{Guid.NewGuid():N}",
            OriginalMsgId: incoming.MsgId,
            OriginalEndToEndId: incoming.EndToEndId,
            Status: status
        ));

        var (success, error) = await mqGateway.SendXmlAsync(Encoding.UTF8.GetBytes(responseXml), ct);
        if (!success)
        {
            logger.LogWarning("FedNow: failed to send pain.014 {Status} response for MsgId '{MsgId}': {Error}",
                status, incoming.MsgId, error);
        }
        else
        {
            logger.LogInformation("FedNow: sent pain.014 {Status} for MsgId '{MsgId}'", status, incoming.MsgId);
        }
    }

    private static Guid? ExtractTransferIdFromEndToEndId(string endToEndId)
    {
        if (!endToEndId.StartsWith("E2E-") || endToEndId.Length < 36)
            return null;

        var guidPart = endToEndId[4..];
        return Guid.TryParse(guidPart, out var id) ? id : null;
    }

    private static string Truncate(string value, int maxLength = 500)
        => value.Length <= maxLength ? value : value[..maxLength] + "...(truncated)";
}
