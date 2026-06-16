using System.Text;
using System.Text.Json;
using UsBankSystem.Api.Integrations.Sftp;
using UsBankSystem.Core.Domain;
using UsBankSystem.Core.Domain.Transfers;

namespace UsBankSystem.Api.Integrations;

// ACH uses SFTP/NACHA instead of REST, so it does not inherit PaymentGatewayBase
// (which is designed for HTTP-based gateways like RTP, FedNow, SWIFT, Cards).
public class AchGateway(HttpClient httpClient, ISftpService sftp, IAchTraceSequencer traceSequencer, IConfiguration configuration, ILogger<AchGateway> logger)
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

            var achContent = await GenerateAchFileAsync(request, toRtn, toAccount, recipientName, accountType, fileId, cancellationToken);
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
            // should monitor daily slot usage if SFTP outages or ACH-helper errors are frequent.
            logger.LogError(ex, "ACH gateway failed for transfer {TransferId} — a daily sequence slot may have been consumed", request.TransferId);
            return new PaymentGatewayResult(false, null, "ACH transfer submission failed");
        }
    }

    private async Task<byte[]> GenerateAchFileAsync(PaymentGatewayRequest request, string toRtn, string toAccount, string recipientName, string accountType, string fileId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var seq = await traceSequencer.NextAsync(ct);
        var achJson = new
        {
            data = new
            {
                header = new
                {
                    immediate_destination = FrbRtn,
                    immediate_origin = OurRtn,
                    immediate_destination_name = FrbName,
                    immediate_origin_name = OurLegalName,
                    // Per-day incrementing char (A→Z then 0→9) distinguishes multiple
                    // files from the same originator on the same business day.
                    file_id_modifier = AchTraceSequencer.FileIdModifier(seq).ToString()
                },
                batches = new[]
                {
                    new
                    {
                        header = new
                        {
                            company_name = OurLegalName,
                            company_identification = CompanyId,
                            standard_entry_class_code = "PPD",
                            company_entry_description = "TRANSFER",
                            effective_entry_date = now.AddDays(1).ToString("yyMMdd"),
                            originating_dfi_identification = OurDfiId
                        },
                        entries = new[]
                        {
                            new
                            {
                                // Code 22 = automated deposit to checking (PPD), 32 = savings.
                                transaction_code = accountType == "savings" ? "32" : "22",
                                receiving_dfi_rtn = toRtn,
                                dfi_account_number = toAccount,
                                amount_cents = (long)Math.Round(request.Amount * 100, MidpointRounding.AwayFromZero),
                                individual_name = recipientName[..Math.Min(22, recipientName.Length)],
                                trace_number = $"{OurDfiId}{seq:D7}"
                            }
                        }
                    }
                }
            }
        };

        var payload = JsonSerializer.Serialize(achJson);
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync("/json-to-ach", content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("ACH helper returned {StatusCode}: {Body}", response.StatusCode, body);
            throw new InvalidOperationException("ACH file generation failed");
        }

        return await response.Content.ReadAsByteArrayAsync(ct);
    }
}
