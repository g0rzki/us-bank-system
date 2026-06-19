using Microsoft.EntityFrameworkCore;
using UsBankSystem.Api.Integrations.Sftp;
using UsBankSystem.Core.Domain.Transactions;
using UsBankSystem.Core.Domain.Transfers;
using UsBankSystem.Infrastructure.Persistence;

namespace UsBankSystem.Api.Services.Polling;

/// <summary>
/// Polls outbound/ on SFTP every 60s and processes:
///  - {fileId}.ack  — validation result for outgoing transfers we submitted
///  - processed_*.ach — incoming transfers from other banks, credited to our customers
/// </summary>
public class AchPollingService(
    ISftpService sftp,
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

        await TimeoutStalePendingTransfersAsync(ct);
    }

    private async Task ProcessAckAsync(string fileName, CancellationToken ct)
    {
        if (!IsSafeFileName(fileName))
        {
            logger.LogError("ACH ack: rejecting unsafe SFTP file name '{FileName}' to prevent path traversal", fileName);
            return;
        }

        try
        {
            var content = await sftp.DownloadAsync($"outbound/{fileName}", ct);
            if (content is null) return;

            var text = System.Text.Encoding.UTF8.GetString(content);
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2)
            {
                logger.LogWarning("ACH ack {File}: unexpected format ({Lines} lines), deleting to avoid retry loop", fileName, lines.Length);
                try { await sftp.DeleteAsync($"outbound/{fileName}", ct); }
                catch (Exception ex) { logger.LogError(ex, "ACH ack {File}: malformed and could not delete from SFTP", fileName); }
                return;
            }

            // FedSystems ACK format: lines[0] = file header, lines[1] = status.
            // "R," prefix = Record accepted (format valid); "E," prefix = Error (rejected).
            // Unknown prefix → log error and skip finalization to avoid false rejection.
            var statusLine = lines[1];
            bool isAccepted;
            if (statusLine.StartsWith("R,", StringComparison.Ordinal))
                isAccepted = true;
            else if (statusLine.StartsWith("E,", StringComparison.Ordinal))
                isAccepted = false;
            else
            {
                logger.LogError("ACH ack {File}: unknown status prefix in '{StatusLine}', skipping finalization to avoid false rejection", fileName, statusLine);
                try { await sftp.DeleteAsync($"outbound/{fileName}", ct); }
                catch (Exception ex) { logger.LogError(ex, "ACH ack {File}: could not delete after unknown format", fileName); }
                return;
            }

            // Filename is {fileId}.ack — fileId is stored as ExternalReferenceId
            var fileId = Path.GetFileNameWithoutExtension(fileName);
            await FinalizeTransferAsync(fileId, isAccepted, ct);

            try { await sftp.DeleteAsync($"outbound/{fileName}", ct); }
            catch (Exception ex) { logger.LogError(ex, "ACH ack {File} processed but SFTP delete failed — will retry next poll", fileName); }

            logger.LogInformation("ACH ack {File}: {Result} (status line: {StatusLine})", fileName, isAccepted ? "accepted" : "rejected", statusLine);
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
        await transferService.ProcessWebhookAsync(transfer.Id, status, fileId, ct);
        await tx.CommitAsync(ct);
    }

    private async Task ProcessIncomingAchFilesAsync(List<string> fileNames, CancellationToken ct)
    {
        var ourRtn = configuration["Ach:RoutingNumber"] ?? "110000000";
        if (ourRtn.Length < 9)
            throw new InvalidOperationException($"Ach:RoutingNumber '{ourRtn}' must be 9 digits (got {ourRtn.Length})");
        var ourDfiId = ourRtn[..8];

        var maxCreditCents = (long)(configuration.GetValue<decimal>("Ach:MaxIncomingCreditUsd", 1_000_000m) * 100);

        var entries = new List<IncomingTransferProcessor.IncomingEntry>();
        var downloadedFiles = new List<string>();

        foreach (var fileName in fileNames)
        {
            if (!IsSafeFileName(fileName))
            {
                logger.LogError("ACH incoming: rejecting unsafe SFTP file name '{FileName}' to prevent path traversal", fileName);
                continue;
            }

            try
            {
                var content = await sftp.DownloadAsync($"outbound/{fileName}", ct);
                if (content is null) continue;

                var text = System.Text.Encoding.UTF8.GetString(content);
                var before = entries.Count;
                entries.AddRange(ParseIncomingAch(text, ourDfiId, fileName, logger, maxCreditCents));
                if (entries.Count == before)
                    logger.LogError("ACH incoming file {File}: yielded 0 matching credit entries — check Ach:RoutingNumber config or file format", fileName);
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

    private static bool IsSafeFileName(string fileName) =>
        !string.IsNullOrEmpty(fileName)
        && !fileName.Contains('/')
        && !fileName.Contains('\\')
        && !fileName.Contains("..")
        && !fileName.Contains('\0');

    private async Task TimeoutStalePendingTransfersAsync(CancellationToken ct)
    {
        var timeoutHours = configuration.GetValue("Ach:PendingTimeoutHours", 48);
        var cutoff = DateTime.UtcNow.AddHours(-timeoutHours);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Both SQL statements run inside one transaction so a crash between them rolls back
        // the transfer status flip — next poll picks up the same stale transfers and retries.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Atomic CTE: marks transfers as Failed AND releases ReservedBalance in one round-trip.
        // Safe for multi-pod: the inner UPDATE WHERE Status='Pending' claims each row exactly
        // once — a second pod sees Status='Failed' and its CTE returns 0 rows.
        // Aggregated per account to handle multiple timed-out transfers from the same account.
        var accountsUpdated = await db.Database.ExecuteSqlAsync($"""
            WITH timed_out AS (
                UPDATE "Transfers"
                SET "Status" = 'Failed'
                WHERE "Status" = 'Pending'
                  AND "Channel" = 'ACH'
                  AND "RequiresApproval" = false
                  AND "CreatedAt" < {cutoff}
                RETURNING "Id", "FromAccountId", "Amount"
            ),
            aggregated AS (
                SELECT "FromAccountId", SUM("Amount") AS "TotalAmount"
                FROM timed_out
                GROUP BY "FromAccountId"
            )
            UPDATE "Accounts" a
            SET "ReservedBalance" = a."ReservedBalance" - agg."TotalAmount"
            FROM aggregated agg
            WHERE a."Id" = agg."FromAccountId"
            """);

        if (accountsUpdated == 0)
        {
            await tx.RollbackAsync();
            return;
        }

        // Fail associated pending debit transactions (idempotent — only touches Pending ones).
        await db.Database.ExecuteSqlAsync($"""
            UPDATE "Transactions"
            SET "Status" = 'Failed'
            WHERE "Type" = 'Debit'
              AND "Status" = 'Pending'
              AND "ReferenceId" IN (
                  SELECT CAST("Id" AS text)
                  FROM "Transfers"
                  WHERE "Status" = 'Failed'
                    AND "Channel" = 'ACH'
                    AND "RequiresApproval" = false
                    AND "CreatedAt" < {cutoff}
              )
            """);

        await tx.CommitAsync(ct);

        logger.LogWarning(
            "ACH timeout: stale Pending ACH transfers marked Failed and reserved balance released on {Count} account(s)",
            accountsUpdated);
    }

    // 22 = checking credit, 32 = savings credit (live entries only; prenotes 23/33 excluded)
    private static readonly HashSet<string> CreditCodes = ["22", "32"];
    // Debit codes — not yet handled; logged as warnings so they don't silently vanish
    private static readonly HashSet<string> DebitCodes = ["27", "28", "37", "38"];

    private static IEnumerable<IncomingTransferProcessor.IncomingEntry> ParseIncomingAch(
        string text, string ourDfiId, string fileName, ILogger logger, long maxCreditCents)
    {
        // NACHA fixed-width format, 94-char lines
        // Entry detail: pos 1='6', pos 2-3=tx code, pos 4-11=RDFI routing, pos 12=check digit,
        //               pos 13-29=account number, pos 30-39=amount cents, pos 55-76=individual name

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length < 94) continue;
            if (line[0] != '6') continue;

            var txCode = line.Substring(1, 2);
            var rdfi = line.Substring(3, 8);

            if (!rdfi.Equals(ourDfiId, StringComparison.Ordinal)) continue;

            if (DebitCodes.Contains(txCode))
            {
                logger.LogWarning(
                    "ACH {File}: debit entry (code {TxCode}) for our RDFI not yet handled — account {Account}, raw amount {Amount}",
                    fileName, txCode, line.Substring(12, 17).Trim(), line.Substring(29, 10).Trim());
                continue;
            }

            if (!CreditCodes.Contains(txCode)) continue;

            var accountNumber = line.Substring(12, 17).Trim();
            var amountStr = line.Substring(29, 10);
            var individualName = line.Substring(54, 22).Trim();
            var traceNumber = line.Substring(79, 15).Trim();

            if (!long.TryParse(amountStr, out var amountCents) || amountCents <= 0 || amountCents > maxCreditCents) continue;

            yield return new IncomingTransferProcessor.IncomingEntry(
                AccountNumber: accountNumber,
                Amount: amountCents / 100m,
                Description: $"ACH credit from {individualName}",
                ExternalRef: $"ACH:{fileName}:{traceNumber}"
            );
        }
    }
}
