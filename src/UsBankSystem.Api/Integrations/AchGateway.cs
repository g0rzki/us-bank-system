using System.Text;
using System.Text.Json;
using UsBankSystem.Api.Integrations.Sftp;
using UsBankSystem.Core.Domain;
using UsBankSystem.Core.Domain.Transfers;

namespace UsBankSystem.Api.Integrations;

// ACH uses SFTP/NACHA instead of REST, so it does not inherit PaymentGatewayBase
// (which is designed for HTTP-based gateways like RTP, FedNow, SWIFT, Cards).
public class AchGateway(HttpClient httpClient, SftpService sftp, AchTraceSequencer traceSequencer, IConfiguration configuration, ILogger<AchGateway> logger)
    : IPaymentGateway
{
    public string Channel => TransferChannel.Ach;

    private string OurRtn => configuration["Ach:RoutingNumber"] ?? "110000000";
    private string OurLegalName => configuration["Ach:LegalName"] ?? "US Bank A";
    private string FrbRtn => configuration["Ach:FrbRoutingNumber"] ?? "090000515";
    private string FrbName => configuration["Ach:FrbName"] ?? "FRB Tungsten";
    // NACHA company_identification should be a 10-char EIN or company ID, not the RTN.
    // Defaults to RTN for backward compatibility — set Ach:CompanyId in production.
    private string CompanyId => configuration["Ach:CompanyId"] ?? OurRtn;

    public async Task<PaymentGatewayResult> SendAsync(PaymentGatewayRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var toRtn = request.Metadata?["toRoutingNumber"] ?? throw new ArgumentException("toRoutingNumber required");
            var toAccount = request.Metadata?["toAccountNumber"] ?? throw new ArgumentException("toAccountNumber required");
            var recipientName = request.Metadata?["recipientName"] ?? throw new ArgumentException("recipientName required");
            var fileId = request.TransferId.ToString("N")[..16].ToUpperInvariant();
            var fileName = $"{fileId}.ach";

            var achContent = await GenerateAchFileAsync(request, toRtn, toAccount, recipientName, fileId, cancellationToken);
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
            logger.LogError(ex, "ACH gateway failed for transfer {TransferId}", request.TransferId);
            return new PaymentGatewayResult(false, null, "ACH transfer submission failed");
        }
    }

    private async Task<byte[]> GenerateAchFileAsync(PaymentGatewayRequest request, string toRtn, string toAccount, string recipientName, string fileId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var seq = traceSequencer.Next();
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
                            originating_dfi_identification = OurRtn[..8]
                        },
                        entries = new[]
                        {
                            new
                            {
                                // Code 22 = automated deposit to checking account (PPD).
                                // Savings accounts require code 32. Current API has no account-type
                                // parameter, so all outgoing transfers use checking. Revisit before
                                // enabling savings-to-savings ACH in production.
                                transaction_code = "22",
                                receiving_dfi_rtn = toRtn,
                                dfi_account_number = toAccount,
                                amount_cents = (long)Math.Round(request.Amount * 100, MidpointRounding.AwayFromZero),
                                individual_name = recipientName[..Math.Min(22, recipientName.Length)],
                                trace_number = $"{OurRtn[..8]}{seq:D7}"
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
