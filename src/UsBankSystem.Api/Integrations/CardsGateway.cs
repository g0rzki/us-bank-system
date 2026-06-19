using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UsBankSystem.Api.Integrations;

public class CardsGateway(HttpClient httpClient, IConfiguration configuration, ILogger<CardsGateway> logger)
{
    private string ApiKey => configuration["Cards:ApiKey"] ?? "bank-key-us-a";
    private string HmacSecret => configuration["Cards:HmacSecret"] ?? "secret-us-a-hmac";
    private string AdminKey => configuration["Cards:AdminKey"] ?? "admin-secret-key-2026";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<CardsGatewayResult> IssueCardAsync(IssueCardGatewayRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var body = new Dictionary<string, object?>
            {
                ["user_id"] = request.UserId,
                ["account_id"] = request.AccountId,
                ["card_type"] = request.CardType,
                ["initial_balance"] = request.InitialBalance
            };

            var (signature, timestamp) = Sign(body);
            var response = await SendSignedAsync(HttpMethod.Post, "/api/v1/cards/issue", body, signature, timestamp, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("Cards gateway issue returned {StatusCode}: {Body}", response.StatusCode, errorBody);
                return CardsGatewayResult.Failure($"Gateway error: {response.StatusCode}");
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<IssueCardGatewayResponse>(responseBody, JsonOptions);
            return CardsGatewayResult.Success(result?.CardToken, result?.MaskedPan, result?.ExpiryMonth, result?.ExpiryYear);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cards gateway issue failed for account {AccountId}", request.AccountId);
            return CardsGatewayResult.Failure(ex.Message);
        }
    }

    public async Task<bool> BlockCardAsync(string cardToken, CancellationToken cancellationToken = default)
        => await SetCardStatusAsync(cardToken, "BLOCKED", "blocked by bank", cancellationToken);

    public async Task<bool> UnblockCardAsync(string cardToken, CancellationToken cancellationToken = default)
        => await SetCardStatusAsync(cardToken, "ACTIVE", "", cancellationToken);

    /// <summary>
    /// Przeprowadza kartę PREPAID przez pełny lifecycle: REQUESTED→PRODUCING→SHIPPED→ACTIVE.
    /// Wymaga X-Admin-Key (operator card-provider).
    /// </summary>
    public async Task<bool> ActivatePrepaidAsync(string cardToken, CancellationToken cancellationToken = default)
    {
        if (!await AdvanceLifecycleAsync(cardToken, "PRODUCING", cancellationToken)) return false;
        if (!await AdvanceLifecycleAsync(cardToken, "SHIPPED", cancellationToken)) return false;
        return await ActivateCardAsync(cardToken, cancellationToken);
    }

    public async Task<CardGatewayStatus?> GetCardStatusAsync(string cardToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.GetAsync($"/api/v1/cards/{cardToken}", cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<CardGatewayStatus>(body, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetCardStatus failed for token {Token}", cardToken);
            return null;
        }
    }

    private async Task<bool> AdvanceLifecycleAsync(string cardToken, string newStatus, CancellationToken cancellationToken)
    {
        try
        {
            var body = new Dictionary<string, object?> { ["new_status"] = newStatus, ["changed_by"] = "us_bank_a" };
            using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/cards/{cardToken}/lifecycle");
            request.Headers.Add("X-Admin-Key", AdminKey);
            request.Content = new StringContent(JsonSerializer.Serialize(body, SignJsonOptions), Encoding.UTF8, "application/json");
            var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("Lifecycle {Status} failed for {Token}: {Body}", newStatus, cardToken, err);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lifecycle {Status} failed for token {Token}", newStatus, cardToken);
            return false;
        }
    }

    private async Task<bool> ActivateCardAsync(string cardToken, CancellationToken cancellationToken)
    {
        try
        {
            var body = new Dictionary<string, object?> { ["activated_by"] = "customer" };
            var (signature, timestamp) = Sign(body);
            var response = await SendSignedAsync(HttpMethod.Post, $"/api/v1/cards/{cardToken}/activate", body, signature, timestamp, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("Activate failed for {Token}: {Body}", cardToken, err);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Activate failed for token {Token}", cardToken);
            return false;
        }
    }

    public async Task<bool> TopUpAsync(string cardToken, decimal amount, CancellationToken cancellationToken = default)
    {
        try
        {
            var body = new Dictionary<string, object?> { ["amount"] = (double)amount, ["currency"] = "USD" };
            var (signature, timestamp) = Sign(body);
            var response = await SendSignedAsync(HttpMethod.Post, $"/api/v1/cards/{cardToken}/topup", body, signature, timestamp, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("Cards topup returned {StatusCode}: {Body}", response.StatusCode, err);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cards gateway topup failed for token {Token}", cardToken);
            return false;
        }
    }

    private async Task<bool> SetCardStatusAsync(string cardToken, string status, string reason, CancellationToken cancellationToken)
    {
        try
        {
            var body = new Dictionary<string, object?> { ["status"] = status, ["reason"] = reason };
            var (signature, timestamp) = Sign(body);
            var response = await SendSignedAsync(HttpMethod.Patch, $"/api/v1/cards/{cardToken}/status", body, signature, timestamp, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("Cards status update returned {StatusCode}: {Body}", response.StatusCode, err);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cards gateway status update failed for token {Token}", cardToken);
            return false;
        }
    }

    private async Task<HttpResponseMessage> SendSignedAsync(
        HttpMethod method, string path, Dictionary<string, object?> body,
        string signature, string timestamp, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-API-Key", ApiKey);
        request.Headers.Add("X-Signature", signature);
        request.Headers.Add("X-Timestamp", timestamp);
        // Musi być identyczny JSON jak użyty do podpisu — sortowane klucze, bez spacji
        var sortedBody = body.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value);
        request.Content = new StringContent(JsonSerializer.Serialize(sortedBody, SignJsonOptions), Encoding.UTF8, "application/json");
        return await httpClient.SendAsync(request, cancellationToken);
    }

    private (string signature, string timestamp) Sign(Dictionary<string, object?> body)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        // Identyczne z payment-gateway: sort_keys=True, separators=(',',':')
        var sortedBody = body.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value);
        var bodyJson = JsonSerializer.Serialize(sortedBody, SignJsonOptions);
        var payload = timestamp + bodyJson;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(HmacSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var signature = Convert.ToHexString(hash).ToLowerInvariant();
        return (signature, timestamp);
    }

    // Opcje do podpisywania — brak spacji, brak null, klucze bez transformacji (już są snake_case)
    private static readonly JsonSerializerOptions SignJsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}

public record IssueCardGatewayRequest(
    string UserId,
    string AccountId,
    string CardType,
    double InitialBalance = 0.0);

public record IssueCardGatewayResponse(
    [property: JsonPropertyName("card_token")] string? CardToken,
    [property: JsonPropertyName("masked_pan")] string? MaskedPan,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("card_type")] string? CardType,
    [property: JsonPropertyName("expiry_month")] int? ExpiryMonth,
    [property: JsonPropertyName("expiry_year")] int? ExpiryYear);

public record CardsGatewayResult(bool IsSuccess, string? CardToken, string? MaskedPan, int? ExpiryMonth, int? ExpiryYear, string? Error)
{
    public static CardsGatewayResult Success(string? cardToken, string? maskedPan, int? expiryMonth, int? expiryYear)
        => new(true, cardToken, maskedPan, expiryMonth, expiryYear, null);
    public static CardsGatewayResult Failure(string error) => new(false, null, null, null, null, error);
}

public record CardGatewayStatus(
    [property: JsonPropertyName("card_token")] string? CardToken,
    [property: JsonPropertyName("masked_pan")] string? MaskedPan,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("card_type")] string? CardType,
    [property: JsonPropertyName("balance")] double? Balance,
    [property: JsonPropertyName("daily_limit")] double? DailyLimit,
    [property: JsonPropertyName("bank_id")] string? BankId);
