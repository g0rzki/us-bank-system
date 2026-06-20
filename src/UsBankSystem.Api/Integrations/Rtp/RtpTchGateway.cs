using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UsBankSystem.Api.Integrations.Rtp;

public class RtpTchGateway(HttpClient httpClient, RtpApiKeyStore keyStore, IConfiguration configuration, ILogger<RtpTchGateway> logger)
{
    private string ApiKey => keyStore.HasKey ? keyStore.ApiKey : (configuration["Rtp:ApiKey"] ?? "");

    public async Task<RtpSendResult> SendPacs008Async(string xml, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/transfers");
            request.Headers.Add("X-Api-Key", ApiKey);
            request.Content = new StringContent(xml, Encoding.UTF8, "application/xml");

            var response = await httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("RTP TCH /transfers returned {StatusCode}: {Body}", response.StatusCode, body);
                return new RtpSendResult(false, null, $"TCH error: {response.StatusCode} — {body}");
            }

            return new RtpSendResult(true, body, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RTP TCH /transfers failed");
            return new RtpSendResult(false, null, ex.Message);
        }
    }

    public async Task<List<RtpQueueMessage>> PollIncomingAsync(CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/queue/incoming?limit=10");
            request.Headers.Add("X-Api-Key", ApiKey);

            var response = await httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("RTP TCH /queue/incoming returned {StatusCode}: {Body}", response.StatusCode, body);
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var envelope = JsonSerializer.Deserialize<QueueResponse>(json, JsonOpts);
            return envelope?.Messages ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RTP TCH /queue/incoming poll failed");
            return [];
        }
    }

    public async Task<bool> SettleAsync(string pacs002Xml, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/transfers/settle");
            request.Headers.Add("X-Api-Key", ApiKey);
            request.Content = new StringContent(pacs002Xml, Encoding.UTF8, "application/xml");

            var response = await httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("RTP TCH /transfers/settle returned {StatusCode}: {Body}", response.StatusCode, body);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RTP TCH /transfers/settle failed");
            return false;
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private record QueueResponse(
        [property: JsonPropertyName("messages")] List<RtpQueueMessage>? Messages);
}

public record RtpSendResult(bool Success, string? ResponseXml, string? Error);

public record RtpQueueMessage(
    [property: JsonPropertyName("queue_id")] int QueueId,
    [property: JsonPropertyName("message_id")] string MessageId,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("payload")] string Payload);
