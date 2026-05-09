using System.Text;
using System.Text.Json;

// Sends the async settlement webhook back to the bank API
class WebhookSender
{
    private readonly HttpClient _http;

    public WebhookSender(string apiUrl)
        : this(new HttpClient { BaseAddress = new Uri(apiUrl) }) { }

    internal WebhookSender(HttpClient httpClient)
    {
        _http = httpClient;
    }

    public async Task SendAsync(string? transferId, string referenceId, string status, string channel, string webhookSecret, ILogger logger)
    {
        if (string.IsNullOrEmpty(transferId))
        {
            logger.LogWarning("[{Channel}] no transferId in request body — cannot send webhook", channel);
            return;
        }

        var payload = JsonSerializer.Serialize(new { status, referenceId });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/transfers/{transferId}/webhook")
            {
                Content = content
            };
            req.Headers.Add("X-Webhook-Secret", webhookSecret);

            var response = await _http.SendAsync(req);
            if (response.IsSuccessStatusCode)
                logger.LogInformation("[{Channel}] webhook delivered transferId={Id} status={Status}", channel, transferId, status);
            else
                logger.LogWarning("[{Channel}] webhook failed {StatusCode} transferId={Id}", channel, response.StatusCode, transferId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{Channel}] webhook error transferId={Id}", channel, transferId);
        }
    }
}
