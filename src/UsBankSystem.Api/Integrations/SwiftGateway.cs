using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using UsBankSystem.Api.Configuration;
using UsBankSystem.Core.Domain;
using UsBankSystem.Core.Domain.Transfers;

namespace UsBankSystem.Api.Integrations;

public class SwiftGateway(
    HttpClient httpClient,
    IMemoryCache cache,
    IOptions<SwiftOptions> swiftOptions,
    ILogger<SwiftGateway> logger)
    : IPaymentGateway
{
    private const string TokenCacheKey = "swift_bearer_token";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public string Channel => TransferChannel.Swift;

    public async Task<PaymentGatewayResult> SendAsync(PaymentGatewayRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var token = await GetTokenAsync(cancellationToken);
            var xml = BuildPacs008Xml(request);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/swift/message")
            {
                Content = new StringContent(xml, Encoding.UTF8, "application/xml")
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await httpClient.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("SWIFT /swift/message returned {StatusCode}: {Body}", response.StatusCode, body);
                return new PaymentGatewayResult(false, null, $"SWIFT error {(int)response.StatusCode}: {body}");
            }

            var result = JsonSerializer.Deserialize<SwiftMessageResponse>(body, JsonOpts);
            var uetr = result?.Uetr ?? throw new InvalidOperationException("SWIFT response missing uetr");

            logger.LogInformation("SWIFT transfer {TransferId} accepted, UETR={Uetr}", request.TransferId, uetr);
            return new PaymentGatewayResult(true, uetr, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SWIFT gateway failed for transfer {TransferId}", request.TransferId);
            return new PaymentGatewayResult(false, null, ex.Message);
        }
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(TokenCacheKey, out string? cached))
            return cached!;

        var opts = swiftOptions.Value;
        var form = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("client_id", opts.ClientId),
            new KeyValuePair<string, string>("client_secret", opts.ClientSecret),
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        ]);

        var response = await httpClient.PostAsync("/auth/token", form, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"SWIFT /auth/token failed {(int)response.StatusCode}: {body}");

        var tokenData = JsonSerializer.Deserialize<TokenResponse>(body, JsonOpts)
            ?? throw new InvalidOperationException("Empty SWIFT token response");
        var token = tokenData.AccessToken
            ?? throw new InvalidOperationException("SWIFT token response missing access_token");

        // Cache for 55 min — token TTL is 1 h
        cache.Set(TokenCacheKey, token, TimeSpan.FromMinutes(55));
        logger.LogDebug("SWIFT OAuth2 token refreshed");
        return token;
    }

    private string BuildPacs008Xml(PaymentGatewayRequest request)
    {
        var m = request.Metadata ?? [];
        var uetr = Guid.NewGuid().ToString().ToLowerInvariant();
        var now = DateTime.UtcNow;
        var msgId = $"MSG-{request.TransferId:N}"[..24];
        var instrId = $"INST-{request.TransferId:N}"[..25];

        var opts = swiftOptions.Value;
        var currency = request.Currency.ToUpperInvariant();
        var amount = request.Amount.ToString("F2", CultureInfo.InvariantCulture);

        m.TryGetValue("iban", out var recipientIban);
        m.TryGetValue("bic", out var recipientBic);
        m.TryGetValue("beneficiaryName", out var beneficiaryName);
        m.TryGetValue("chargeBearer", out var chargeBearerRaw);
        m.TryGetValue("remittanceInfo", out var remittanceInfo);
        m.TryGetValue("fromAccountNumber", out var fromIban);
        m.TryGetValue("fromAccountName", out var senderName);
        m.TryGetValue("valueDate", out var valueDateStr);

        var chargeBearer = MapChargeBearer(chargeBearerRaw);
        var settlementDate = ParseSettlementDate(valueDateStr, now);

        var ns = XNamespace.Get("urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08");
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(ns + "Document",
                new XElement(ns + "FIToFICstmrCdtTrf",
                    new XElement(ns + "GrpHdr",
                        new XElement(ns + "MsgId", msgId),
                        new XElement(ns + "CreDtTm", now.ToString("yyyy-MM-ddTHH:mm:ssZ")),
                        new XElement(ns + "NbOfTxs", "1")
                    ),
                    new XElement(ns + "CdtTrfTxInf",
                        new XElement(ns + "PmtId",
                            new XElement(ns + "InstrId", instrId),
                            new XElement(ns + "UETR", uetr)
                        ),
                        new XElement(ns + "IntrBkSttlmAmt",
                            new XAttribute("Ccy", currency),
                            amount
                        ),
                        new XElement(ns + "IntrBkSttlmDt", settlementDate),
                        new XElement(ns + "InstdAmt",
                            new XAttribute("Ccy", currency),
                            amount
                        ),
                        new XElement(ns + "ChrgBr", chargeBearer),
                        new XElement(ns + "Dbtr",
                            string.IsNullOrWhiteSpace(senderName) ? null : new XElement(ns + "Nm", senderName)
                        ),
                        new XElement(ns + "DbtrAcct",
                            new XElement(ns + "Id",
                                new XElement(ns + "IBAN", fromIban ?? "")
                            )
                        ),
                        new XElement(ns + "DbtrAgt",
                            new XElement(ns + "FinInstnId",
                                new XElement(ns + "BICFI", opts.Bic)
                            )
                        ),
                        new XElement(ns + "Cdtr",
                            new XElement(ns + "Nm", beneficiaryName ?? "")
                        ),
                        new XElement(ns + "CdtrAgt",
                            new XElement(ns + "FinInstnId",
                                new XElement(ns + "BICFI", recipientBic ?? "")
                            )
                        ),
                        new XElement(ns + "CdtrAcct",
                            new XElement(ns + "Id",
                                new XElement(ns + "Othr",
                                    new XElement(ns + "Id", recipientIban ?? "")
                                )
                            )
                        ),
                        new XElement(ns + "RmtInf",
                            new XElement(ns + "Ustrd",
                                string.IsNullOrWhiteSpace(remittanceInfo)
                                    ? $"Transfer {request.TransferId}"
                                    : remittanceInfo
                            )
                        )
                    )
                )
            )
        );

        using var ms = new MemoryStream();
        using var xw = XmlWriter.Create(ms, new XmlWriterSettings { Encoding = Encoding.UTF8, Indent = false });
        doc.Save(xw);
        xw.Flush();
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string MapChargeBearer(string? value) =>
        (value ?? "SHAR").ToUpperInvariant() switch
        {
            "SHA" or "SHAR" => "SHAR",
            "OUR" or "DEBT" => "DEBT",
            "BEN" or "CRED" => "CRED",
            _ => "SHAR"
        };

    private static string ParseSettlementDate(string? valueDateStr, DateTime fallback)
    {
        if (!string.IsNullOrEmpty(valueDateStr) && valueDateStr.Length == 8
            && DateTime.TryParseExact(valueDateStr, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.ToString("yyyy-MM-dd");
        return fallback.ToString("yyyy-MM-dd");
    }

    // Extracts UETR from pacs.008 XML — used by the /receive endpoint.
    public static string? ExtractUetr(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            XNamespace ns = "urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08";
            return doc.Descendants(ns + "UETR").FirstOrDefault()?.Value;
        }
        catch
        {
            return null;
        }
    }

    public record IncomingSwiftPayment(string Uetr, decimal Amount, string Currency, string? CreditorAccount, string? Description);

    // Parses a pacs.008 XML forwarded by the SWIFT middleware (incoming transfer to us).
    public static IncomingSwiftPayment? ParseIncoming(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            XNamespace ns = "urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08";

            var txInfo = doc.Descendants(ns + "CdtTrfTxInf").FirstOrDefault();
            if (txInfo is null) return null;

            var uetr = txInfo.Descendants(ns + "UETR").FirstOrDefault()?.Value;
            if (string.IsNullOrWhiteSpace(uetr)) return null;

            var amtElem = txInfo.Element(ns + "IntrBkSttlmAmt") ?? txInfo.Element(ns + "InstdAmt");
            if (amtElem is null || !decimal.TryParse(amtElem.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var amount))
                return null;

            var currency = amtElem.Attribute("Ccy")?.Value ?? "USD";

            // Creditor account: try Othr/Id first, then IBAN
            var creditorAccount =
                txInfo.Descendants(ns + "CdtrAcct").FirstOrDefault()
                    ?.Descendants(ns + "Id").FirstOrDefault()
                    ?.Elements()
                    .Select(e => e.Element(ns + "Id")?.Value ?? e.Value)
                    .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

            var description = txInfo.Descendants(ns + "Ustrd").FirstOrDefault()?.Value;

            return new IncomingSwiftPayment(uetr, amount, currency, creditorAccount, description);
        }
        catch
        {
            return null;
        }
    }

    private record TokenResponse([property: JsonPropertyName("access_token")] string? AccessToken);
    private record SwiftMessageResponse(string? Uetr, List<string>? Route, string? Status);
}
