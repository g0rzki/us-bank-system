using System.Text;

namespace UsBankSystem.Api.Integrations.FedNow;

public class FedNowMqGateway(HttpClient httpClient, ILogger<FedNowMqGateway> logger)
{
    public async Task<(bool Success, string? Error)> SendXmlAsync(byte[] xml, CancellationToken cancellationToken = default)
    {
        try
        {
            var filename = $"pacs_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.xml";
            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(xml);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/xml");
            content.Add(fileContent, "file", filename);

            var response = await httpClient.PostAsync("/send", content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("FedNow MQ /send returned {StatusCode}: {Body}", response.StatusCode, body);
                return (false, $"MQ error: {response.StatusCode} — {body}");
            }

            logger.LogInformation("FedNow MQ /send accepted file {Filename}", filename);
            return (true, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FedNow MQ /send failed");
            return (false, ex.Message);
        }
    }

    public async Task<byte[]?> PollIncomingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.GetAsync("/FIFO/out", cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("FedNow MQ /FIFO/out returned {StatusCode}: {Body}", response.StatusCode, body);
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FedNow MQ /FIFO/out poll failed");
            return null;
        }
    }
}
