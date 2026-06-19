using System.Text;
using UsBankSystem.Api.Integrations.Sftp;
using UsBankSystem.Core.Domain;
using UsBankSystem.Core.Domain.Transfers;

namespace UsBankSystem.Api.Integrations;

// ACH uses SFTP/NACHA instead of REST, so it does not inherit PaymentGatewayBase
// (which is designed for HTTP-based gateways like RTP, FedNow, SWIFT, Cards).
public class AchGateway(ISftpService sftp, IAchTraceSequencer traceSequencer, IConfiguration configuration, ILogger<AchGateway> logger)
    : IPaymentGateway
{
    public string Channel => TransferChannel.Ach;

    // Single authoritative formula for the SFTP filename / ExternalReferenceId.
    // Both AchGateway (upload) and AchPaymentService (DB persist) must use this.
    public static string ComputeFileId(Guid transferId) =>
        transferId.ToString("N")[..16].ToUpperInvariant();

    private string OurRtn => configuration["Ach:RoutingNumber"] ?? "110000000";
    private string OurLegalName => configuration["Ach:LegalName"] ?? "US Bank A";
    private string FrbRtn => configuration["Ach:FrbRoutingNumber"] ?? "090000515";
    private string FrbName => configuration["Ach:FrbName"] ?? "FRB Tungsten";
    // NACHA company_identification should be a 10-char EIN or company ID, not the RTN.
    // Defaults to RTN for backward compatibility — set Ach:CompanyId in production.
    private string CompanyId => configuration["Ach:CompanyId"] ?? OurRtn;
    // ABA routing numbers are always 9 digits; first 8 are the DFI identifier used in NACHA.
    private string OurDfiId => OurRtn.Length >= 9
        ? OurRtn[..8]
        : throw new InvalidOperationException($"Ach:RoutingNumber '{OurRtn}' must be 9 digits (got {OurRtn.Length})");

    public async Task<PaymentGatewayResult> SendAsync(PaymentGatewayRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var toRtn = request.Metadata?["toRoutingNumber"] ?? throw new ArgumentException("toRoutingNumber required");
            var toAccount = request.Metadata?["toAccountNumber"] ?? throw new ArgumentException("toAccountNumber required");
            var recipientName = request.Metadata?["recipientName"] ?? throw new ArgumentException("recipientName required");
            var accountType = request.Metadata?.GetValueOrDefault("accountType") ?? "checking";
            var fileId = ComputeFileId(request.TransferId);
            var fileName = $"{fileId}.ach";

            var achContent = await GenerateNachaFileAsync(request, toRtn, toAccount, recipientName, accountType, cancellationToken);
            await sftp.UploadAsync($"inbound/{fileName}", achContent, cancellationToken);

            logger.LogInformation("ACH: uploaded {File} for transfer {TransferId}", fileName, request.TransferId);
            return new PaymentGatewayResult(true, fileId, null);
        }
        catch (ArgumentException ex)
        {
            // Our own validation failures — safe to return as-is (no internal details)
            return new PaymentGatewayResult(false, null, ex.Message);
        }
        catch (Exception ex)
        {
            // The daily sequence counter is incremented before file generation (seq is embedded
            // in the NACHA content). A failure here permanently burns that slot — operators
            // should monitor daily slot usage if SFTP outages are frequent.
            logger.LogError(ex, "ACH gateway failed for transfer {TransferId} — a daily sequence slot may have been consumed", request.TransferId);
            return new PaymentGatewayResult(false, null, "ACH transfer submission failed");
        }
    }

    private async Task<byte[]> GenerateNachaFileAsync(
        PaymentGatewayRequest request, string toRtn, string toAccount,
        string recipientName, string accountType, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var easternZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var nowEt = TimeZoneInfo.ConvertTimeFromUtc(now, easternZone);
        var seq = await traceSequencer.NextAsync(ct);
        var modifier = AchTraceSequencer.FileIdModifier(seq);
        logger.LogDebug("ACH: allocated sequence slot {Seq} (modifier={Modifier}) for transfer {TransferId}",
            seq, modifier, request.TransferId);

        var amountCents = (long)Math.Round(request.Amount * 100, MidpointRounding.AwayFromZero);
        var txCode = accountType == "savings" ? "32" : "22";
        var rdfi8 = toRtn[..8];
        var rdfiCheckDigit = toRtn[8].ToString();
        var creationDate = nowEt.ToString("yyMMdd");
        var creationTime = nowEt.ToString("HHmm");
        var effectiveDate = nowEt.AddDays(1).ToString("yyMMdd");
        var batchNumStr = seq.ToString().PadLeft(7, '0');
        var amountStr = amountCents.ToString().PadLeft(10, '0');
        var creditStr = amountCents.ToString().PadLeft(12, '0');
        // Entry hash = last 10 digits of the sum of all RDFI 8-digit routing numbers in the batch
        var entryHash = (long.Parse(rdfi8) % 10_000_000_000L).ToString().PadLeft(10, '0');
        var traceNumber = OurDfiId + seq.ToString("D7");
        var name22 = Pad(recipientName[..Math.Min(22, recipientName.Length)], 22);

        // NACHA fixed-width records: each exactly 94 chars, CRLF-separated.
        // Blocking factor = 10; file record count must be a multiple of 10 (padded with '9' records).
        var records = new List<string>
        {
            // Record Type 1: File Header
            R("1"
              + "01"
              + (" " + FrbRtn).PadRight(10)[..10]
              + (" " + OurRtn).PadRight(10)[..10]
              + creationDate + creationTime + modifier
              + "094" + "10" + "1"
              + Pad(FrbName, 23)
              + Pad(OurLegalName, 23)
              + "        "),

            // Record Type 5: Batch Header
            R("5"
              + "220"
              + Pad(OurLegalName, 16)
              + new string(' ', 20)
              + Pad(CompanyId, 10)
              + "PPD"
              + Pad("TRANSFER", 10)
              + "      "
              + effectiveDate
              + "   "
              + "1"
              + OurDfiId
              + batchNumStr),

            // Record Type 6: Entry Detail
            R("6"
              + txCode
              + rdfi8
              + rdfiCheckDigit
              + Pad(toAccount, 17)
              + amountStr
              + new string(' ', 15)
              + name22
              + "  "
              + "0"
              + traceNumber),

            // Record Type 8: Batch Control
            R("8"
              + "220"
              + "000001"
              + entryHash
              + "000000000000"
              + creditStr
              + Pad(CompanyId, 10)
              + new string(' ', 19)
              + "      "
              + OurDfiId
              + batchNumStr),

            // Record Type 9: File Control
            R("9"
              + "000001"
              + "000001"
              + "00000001"
              + entryHash
              + "000000000000"
              + creditStr
              + new string(' ', 39)),
        };

        // Pad file to next multiple of 10 records (NACHA spec §2.7 blocking)
        var paddingCount = (10 - records.Count % 10) % 10;
        records.AddRange(Enumerable.Repeat(new string('9', 94), paddingCount));

        return Encoding.UTF8.GetBytes(string.Join("\n", records));
    }

    // Pad or truncate string to exactly `len` characters (space-padded on the right)
    private static string Pad(string? s, int len)
    {
        s ??= "";
        return s.Length >= len ? s[..len] : s + new string(' ', len - s.Length);
    }

    // Defensive length check: catches NACHA format bugs at generation time rather than at FedSystems
    private static string R(string record)
    {
        if (record.Length != 94)
            throw new InvalidOperationException(
                $"NACHA record length is {record.Length}, expected 94: '{record[..Math.Min(20, record.Length)]}...'");
        return record;
    }
}
