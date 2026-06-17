using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UsBankSystem.Api.Integrations;

public class KlikApiClient(HttpClient http, ILogger<KlikApiClient> logger, IConfiguration cfg) : IKlikApiClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<KlikGenerateCodeResult> GenerateCodeAsync(string userId, CancellationToken ct = default)
    {
        var payload = new GenerateRequest(userId, "US");
        var (ok, body, statusCode) = await PostAsync("/api/v1/codes/generate", payload, ct);

        if (!ok)
        {
            var err = ParseError(body);
            logger.LogWarning("KLIK generate failed ({Status}): {Error}", statusCode, err);
            return new KlikGenerateCodeResult(false, null, null, err);
        }

        var res = JsonSerializer.Deserialize<GenerateResponse>(body, JsonOpts);
        return new KlikGenerateCodeResult(true, res!.Code, res.ExpiresAt.UtcDateTime, null);
    }

    public async Task<KlikConfirmResult> ConfirmPaymentAsync(string transactionId, bool accepted, string? rejectReason, CancellationToken ct = default)
    {
        var payload = new ConfirmRequest(transactionId, accepted ? "ACCEPTED" : "REJECTED", rejectReason);
        var (ok, body, statusCode) = await PostAsync("/api/v1/payments/confirm", payload, ct);

        if (!ok)
        {
            var err = ParseError(body);
            logger.LogWarning("KLIK confirm failed ({Status}): {Error}", statusCode, err);
            return new KlikConfirmResult(false, err);
        }

        return new KlikConfirmResult(true, null);
    }

    private async Task<(bool Ok, string Body, int StatusCode)> PostAsync(string path, object payload, CancellationToken ct)
    {
        var apiKey = cfg["Integrations:KlikApiKey"] ?? "dev-key";
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-KLIK-Bank-Api-Key", apiKey);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "KLIK HTTP call to {Path} failed", path);
            return (false, ex.Message, 0);
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        return (response.IsSuccessStatusCode, body, (int)response.StatusCode);
    }

    private static string ParseError(string body)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<KlikErrorEnvelope>(body, JsonOpts);
            if (envelope?.Error is { } e)
                return $"{e.Code}: {e.Message}";
        }
        catch { /* ignore parse failure */ }
        return body;
    }

    private record GenerateRequest(string UserId, string Zone);
    private record GenerateResponse(string Code, int ExpiresIn, DateTimeOffset ExpiresAt);
    private record ConfirmRequest(string TransactionId, string Status, string? RejectReason);
    private record KlikErrorEnvelope(KlikError Error);
    private record KlikError(string Code, string Message);
}
