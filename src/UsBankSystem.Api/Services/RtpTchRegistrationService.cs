using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using UsBankSystem.Api.Configuration;
using UsBankSystem.Api.Integrations.Rtp;

namespace UsBankSystem.Api.Services;

public class RtpTchRegistrationService(
    RtpApiKeyStore keyStore,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IOptions<PaymentSessionConfig> paymentConfig,
    ILogger<RtpTchRegistrationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configured = configuration["Rtp:ApiKey"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            keyStore.Set(configured);
            logger.LogInformation("RTP TCH: using pre-configured API key");
            return;
        }

        // Give the rest of the app a moment to finish starting before hitting TCH
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        var rtp = paymentConfig.Value.Rtp;
        var baseUrl = configuration["Integrations:RtpTchUrl"] ?? "http://localhost:8000";

        using var http = httpClientFactory.CreateClient();
        http.BaseAddress = new Uri(baseUrl);

        await RegisterAsync(http, rtp, stoppingToken);
    }

    private async Task RegisterAsync(HttpClient http, RtpConfig rtp, CancellationToken ct)
    {
        try
        {
            var response = await http.PostAsJsonAsync("/banks", new
            {
                bank_code = rtp.BankCode,
                routing_number = rtp.BankRtn,
                balance = 1_000_000.0,
                debt_limit = 500_000.0
            }, ct);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<RegisterResponse>(cancellationToken: ct);
                keyStore.Set(body!.ApiKey);
                logger.LogInformation("RTP TCH: registered bank {BankCode}", rtp.BankCode);
                return;
            }

            // Already exists — reset key so we have a valid one
            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                var reset = await http.PostAsync($"/banks/{rtp.BankCode}/reset-key", null, ct);
                if (reset.IsSuccessStatusCode)
                {
                    var body = await reset.Content.ReadFromJsonAsync<ResetResponse>(cancellationToken: ct);
                    keyStore.Set(body!.NewApiKey);
                    logger.LogInformation("RTP TCH: refreshed API key for bank {BankCode}", rtp.BankCode);
                    return;
                }
            }

            var err = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("RTP TCH: registration failed ({Status}): {Body}", response.StatusCode, err);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "RTP TCH: registration skipped — service not reachable at {Url}", http.BaseAddress);
        }
    }

    private record RegisterResponse([property: JsonPropertyName("api_key")] string ApiKey);
    private record ResetResponse([property: JsonPropertyName("new_api_key")] string NewApiKey);
}
