using System.Text;
using System.Text.Json;

namespace UsBankSystem.Api.Integrations;

public class CardsGateway(HttpClient httpClient, ILogger<CardsGateway> logger)
{
    public async Task<CardsGatewayResult> RegisterCardAsync(RegisterCardGatewayRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = JsonSerializer.Serialize(request, JsonOptions);
            var content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync("/cards/register", content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Cards gateway returned {StatusCode}: {Body}", response.StatusCode, body);
                return CardsGatewayResult.Failure($"Gateway error: {response.StatusCode}");
            }

            var result = JsonSerializer.Deserialize<RegisterCardGatewayResponse>(body, JsonOptions);
            return CardsGatewayResult.Success(result?.CardToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cards gateway registration failed");
            return CardsGatewayResult.Failure(ex.Message);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}

public record RegisterCardGatewayRequest(
    string AccountId,
    string Last4,
    string Type,
    string ExpiresAt,
    decimal? DailyLimit,
    decimal? MonthlyLimit);

public record RegisterCardGatewayResponse(string? CardToken);

public record CardsGatewayResult(bool IsSuccess, string? CardToken, string? Error)
{
    public static CardsGatewayResult Success(string? cardToken) => new(true, cardToken, null);
    public static CardsGatewayResult Failure(string error) => new(false, null, error);
}
