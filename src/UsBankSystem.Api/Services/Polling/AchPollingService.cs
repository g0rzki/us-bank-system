using Microsoft.EntityFrameworkCore;
using UsBankSystem.Api.Integrations.Sftp;
using UsBankSystem.Core.Domain.Transfers;
using UsBankSystem.Infrastructure.Persistence;

namespace UsBankSystem.Api.Services.Polling;

/// <summary>
/// Polls outbound/ on SFTP every 60s and processes:
///  - {fileId}.ack  — validation result for outgoing transfers we submitted
///  - processed_*.ach — incoming transfers from other banks, credited to our customers
/// </summary>
public class AchPollingService(
    SftpService sftp,
    IServiceScopeFactory scopeFactory,
    IncomingTransferProcessor incomingProcessor,
    IConfiguration configuration,
    ILogger<AchPollingService> logger) : SettlementPollingBase(logger)
{
    protected override TimeSpan Interval => TimeSpan.FromSeconds(
        configuration.GetValue("Ach:PollIntervalSeconds", 60));

    protected override async Task PollAsync(CancellationToken ct)
    {
        var files = (await sftp.ListFilesAsync("outbound", ct)).ToList();

        var ackFiles = files.Where(f => f.EndsWith(".ack", StringComparison.OrdinalIgnoreCase)).ToList();
        var achFiles = files.Where(f => f.StartsWith("processed_", StringComparison.OrdinalIgnoreCase)
                                     && f.EndsWith(".ach", StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var ack in ackFiles)
            await ProcessAckAsync(ack, ct);

        if (achFiles.Count > 0)
            await ProcessIncomingAchFilesAsync(achFiles, ct);
    }

    private async Task ProcessAckAsync(string fileName, CancellationToken ct)
    {
        try
        {
            var content = await sftp.DownloadAsync($"outbound/{fileName}", ct);
            if (content is null) return;

            var text = System.Text.Encoding.UTF8.GetString(content);
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2)
            {
                logger.LogWarning("ACH ack {File}: unexpected format ({Lines} lines), skipping finalization", fileName, lines.Length);
                return;
            }

            // FedSystems ACK format: line 2 starting with "R," means Rejected
            var isAccepted = !lines[1].StartsWith("R,", StringComparison.Ordinal);

            // Filename is {fileId}.ack — fileId is stored as ExternalReferenceId
            var fileId = Path.GetFileNameWithoutExtension(fileName);
            await FinalizeTransferAsync(fileId, isAccepted, ct);

            try { await sftp.DeleteAsync($"outbound/{fileName}", ct); }
            catch (Exception ex) { logger.LogError(ex, "ACH ack {File} processed but SFTP delete failed — will retry next poll", fileName); }

            logger.LogInformation("ACH ack {File}: {Result}", fileName, isAccepted ? "accepted" : "rejected");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process ACH ack {File}", fileName);
        }
    }

    private async Task FinalizeTransferAsync(string fileId, bool accepted, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var transferService = scope.ServiceProvider.GetRequiredService<TransferService>();

        // Serializable isolation prevents duplicate processing if multiple polling pods
        // race on the same ACK. EF Core returns tracked entities from cache, so the
        // Status != Pending guard inside ProcessWebhookAsync would not re-query the DB —
        // the transaction-level conflict ensures only one pod commits.
        await using var tx = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, ct);

        var transfer = await db.Transfers
            .FirstOrDefaultAsync(t => t.ExternalReferenceId == fileId && t.Status == TransferStatus.Pending, ct);

        if (transfer is null)
        {
            logger.LogWarning("ACH ack: no pending transfer found with ExternalReferenceId={FileId}", fileId);
            return;
        }

        var status = accepted ? TransferStatus.Completed : TransferStatus.Failed;
        await transferService.ProcessWebhookAsync(transfer.Id, status, fileId);
        await tx.CommitAsync(ct);
    }

    private async Task ProcessIncomingAchFilesAsync(List<string> fileNames, CancellationToken ct)
    {
        var ourRtn = configuration["Ach:RoutingNumber"] ?? "110000000";
        var ourDfiId = ourRtn[..8]; // 8-char DFI portion

        var entries = new List<IncomingTransferProcessor.IncomingEntry>();
        var downloadedFiles = new List<string>();

        foreach (var fileName in fileNames)
        {
            try
            {
                var content = await sftp.DownloadAsync($"outbound/{fileName}", ct);
                if (content is null) continue;

                var text = System.Text.Encoding.UTF8.GetString(content);
                entries.AddRange(ParseIncomingAch(text, ourDfiId, fileName));
                downloadedFiles.Add(fileName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to read incoming ACH file {File}", fileName);
            }
        }

        // Save to DB first — if this throws, files remain on SFTP and will be retried next poll.
        // IncomingTransferProcessor is idempotent so re-processing is safe.
        if (entries.Count > 0)
            await incomingProcessor.ProcessAsync(entries, ct);

        foreach (var fileName in downloadedFiles)
        {
            try { await sftp.DeleteAsync($"outbound/{fileName}", ct); }
            catch (Exception ex) { logger.LogError(ex, "Failed to delete processed ACH file {File} from SFTP", fileName); }
        }
    }

    private static IEnumerable<IncomingTransferProcessor.IncomingEntry> ParseIncomingAch(string text, string ourDfiId, string fileName)
    {
        // NACHA fixed-width format, 94-char lines
        // Entry detail: pos 1='6', pos 2-3=tx code, pos 4-11=RDFI routing, pos 12=check digit,
        //               pos 13-29=account number, pos 30-39=amount cents, pos 55-76=individual name
        // 22/32 = live credit entries (checking/savings). Prenote codes 23/33 have zero amount
        // and are excluded — they don't transfer money and would be filtered by amountCents <= 0 anyway.
        var creditCodes = new HashSet<string> { "22", "32" };

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length < 94) continue;
            if (line[0] != '6') continue;

            var txCode = line.Substring(1, 2);
            if (!creditCodes.Contains(txCode)) continue;

            var rdfi = line.Substring(3, 8);
            if (!rdfi.Equals(ourDfiId, StringComparison.Ordinal)) continue;

            var accountNumber = line.Substring(12, 17).Trim();
            var amountStr = line.Substring(29, 10);
            var individualName = line.Substring(54, 22).Trim();
            var traceNumber = line.Substring(79, 15).Trim();

            if (!long.TryParse(amountStr, out var amountCents) || amountCents <= 0) continue;

            yield return new IncomingTransferProcessor.IncomingEntry(
                AccountNumber: accountNumber,
                Amount: amountCents / 100m,
                Description: $"ACH credit from {individualName}",
                ExternalRef: $"ACH:{fileName}:{traceNumber}"
            );
        }
    }
}
