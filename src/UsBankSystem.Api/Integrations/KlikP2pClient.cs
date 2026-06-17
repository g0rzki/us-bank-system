using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UsBankSystem.Api.Integrations;

public class KlikP2pClient(HttpClient http, ILogger<KlikP2pClient> logger, IConfiguration cfg) : IKlikP2pClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private string ApiKey => cfg["Integrations:KlikApiKey"] ?? "dev-key";

    public async Task<KlikP2pRegisterResult> RegisterAliasAsync(string phone, string routingNumber, string accountNumber, CancellationToken ct = default)
    {
        var body = new RegisterBody(
            phone,
            new AccountIdentifier("us_routing", routingNumber, accountNumber),
            "US");

        var (ok, responseBody, statusCode) = await PostAsync("/api/v1/aliases/register", body, ct);
        if (!ok)
        {
            var err = ParseError(responseBody);
            logger.LogWarning("KLIK P2P register failed ({Status}): {Error}", statusCode, err);
            ThrowForKlikError(err);
        }

        var res = JsonSerializer.Deserialize<RegisterResponse>(responseBody, JsonOpts)!;
        return new KlikP2pRegisterResult(res.AliasId, res.RegisteredAt.UtcDateTime);
    }

    public async Task<KlikP2pLookupResult> LookupAliasAsync(string phone, CancellationToken ct = default)
    {
        var encoded = Uri.EscapeDataString(phone);
        var (ok, responseBody, statusCode) = await GetAsync($"/api/v1/aliases/lookup/{encoded}", ct);
        if (!ok)
        {
            var err = ParseError(responseBody);
            logger.LogWarning("KLIK P2P lookup failed ({Status}): {Error}", statusCode, err);
            ThrowForKlikError(err);
        }

        var res = JsonSerializer.Deserialize<LookupResponse>(responseBody, JsonOpts)!;
        return new KlikP2pLookupResult(
            res.BankId,
            res.AccountIdentifier?.RoutingNumber,
            res.AccountIdentifier?.AccountNumber);
    }

    public async Task DeleteAliasAsync(string phone, CancellationToken ct = default)
    {
        var encoded = Uri.EscapeDataString(phone);
        var (ok, responseBody, statusCode) = await DeleteAsync($"/api/v1/aliases/{encoded}", ct);
        if (!ok)
        {
            var err = ParseError(responseBody);
            logger.LogWarning("KLIK P2P delete failed ({Status}): {Error}", statusCode, err);
            ThrowForKlikError(err);
        }
    }

    private async Task<(bool Ok, string Body, int StatusCode)> PostAsync(string path, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-KLIK-Bank-Api-Key", ApiKey);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        return await SendAsync(request, path, ct);
    }

    private async Task<(bool Ok, string Body, int StatusCode)> GetAsync(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-KLIK-Bank-Api-Key", ApiKey);

        return await SendAsync(request, path, ct);
    }

    private async Task<(bool Ok, string Body, int StatusCode)> DeleteAsync(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, path);
        request.Headers.Add("X-KLIK-Bank-Api-Key", ApiKey);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        return await SendAsync(request, path, ct);
    }

    private async Task<(bool Ok, string Body, int StatusCode)> SendAsync(HttpRequestMessage request, string path, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "KLIK P2P HTTP call to {Path} failed", path);
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

    private static void ThrowForKlikError(string err)
    {
        if (err.StartsWith("409_ALIAS_ALREADY_EXISTS"))         throw new InvalidOperationException(err);
        if (err.StartsWith("404_ALIAS_NOT_FOUND"))              throw new KeyNotFoundException(err);
        if (err.StartsWith("403_INSUFFICIENT_PERMISSIONS"))     throw new UnauthorizedAccessException(err);
        if (err.StartsWith("403_P2P_NOT_ENABLED"))              throw new InvalidOperationException(err);
        if (err.StartsWith("403_BANK_INACTIVE"))                throw new InvalidOperationException(err);
        if (err.StartsWith("400_INVALID_PHONE_FORMAT"))         throw new ArgumentException(err);
        if (err.StartsWith("400_INVALID_ACCOUNT_IDENTIFIER"))  throw new ArgumentException(err);
        if (err.StartsWith("422_ZONE_MISMATCH"))                throw new ArgumentException(err);
        throw new InvalidOperationException(err);
    }

    private record RegisterBody(string Phone, AccountIdentifier AccountIdentifier, string Zone);
    private record AccountIdentifier(string Type, string RoutingNumber, string AccountNumber);
    private record RegisterResponse(string AliasId, string Phone, DateTimeOffset RegisteredAt);
    private record LookupResponse(string Phone, string BankId, string BankCode, AccountIdentifier? AccountIdentifier);
    private record KlikErrorEnvelope(KlikError? Error);
    private record KlikError(string Code, string Message);
}
