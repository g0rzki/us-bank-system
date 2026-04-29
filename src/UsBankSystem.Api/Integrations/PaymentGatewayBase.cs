using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using UsBankSystem.Core.Domain;

namespace UsBankSystem.Api.Integrations;

public abstract class PaymentGatewayBase(HttpClient httpClient, ILogger logger) : IPaymentGateway
{
    public abstract string Channel { get; }
    protected abstract string Endpoint { get; }

    public async Task<PaymentGatewayResult> SendAsync(PaymentGatewayRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = JsonSerializer.Serialize(request);
            var content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync(Endpoint, content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Payment gateway {Channel} returned {StatusCode}: {Body}", Channel, response.StatusCode, body);
                return new PaymentGatewayResult(false, null, $"Gateway error: {response.StatusCode}");
            }

            var result = JsonSerializer.Deserialize<GatewayResponse>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return new PaymentGatewayResult(true, result?.ReferenceId, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Payment gateway {Channel} failed", Channel);
            return new PaymentGatewayResult(false, null, ex.Message);
        }
    }

    private record GatewayResponse(string? ReferenceId);
}