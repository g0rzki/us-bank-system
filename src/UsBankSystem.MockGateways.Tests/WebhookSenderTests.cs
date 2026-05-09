using System.Net;
using Microsoft.Extensions.Logging.Abstractions;

namespace UsBankSystem.MockGateways.Tests;

public class WebhookSenderTests
{
    private static WebhookSender CreateSender(HttpStatusCode responseCode, out List<CapturedRequest> captured)
    {
        var requests = new List<CapturedRequest>();
        captured = requests;
        var handler = new CapturingHandler(responseCode, requests);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5100") };
        return new WebhookSender(http);
    }

    internal record CapturedRequest(HttpMethod Method, Uri? Uri, string Body, IEnumerable<string> WebhookSecretValues);

    [Fact]
    public async Task SendAsync_ValidTransferId_PostsToCorrectUrl()
    {
        var sender = CreateSender(HttpStatusCode.OK, out var captured);

        await sender.SendAsync("abc-123", "REF-001", "Completed", "ACH", "secret", NullLogger.Instance);

        Assert.Single(captured);
        Assert.Equal("/transfers/abc-123/webhook", captured[0].Uri!.PathAndQuery);
        Assert.Equal(HttpMethod.Post, captured[0].Method);
    }

    [Fact]
    public async Task SendAsync_ValidTransferId_SetsWebhookSecretHeader()
    {
        var sender = CreateSender(HttpStatusCode.OK, out var captured);

        await sender.SendAsync("abc-123", "REF-001", "Completed", "ACH", "my-secret", NullLogger.Instance);

        Assert.Equal("my-secret", captured[0].WebhookSecretValues.First());
    }

    [Fact]
    public async Task SendAsync_ValidTransferId_BodyContainsStatusAndReferenceId()
    {
        var sender = CreateSender(HttpStatusCode.OK, out var captured);

        await sender.SendAsync("abc-123", "REF-001", "Completed", "ACH", "secret", NullLogger.Instance);

        Assert.Contains("Completed", captured[0].Body);
        Assert.Contains("REF-001", captured[0].Body);
    }

    [Fact]
    public async Task SendAsync_NullTransferId_DoesNotSendRequest()
    {
        var sender = CreateSender(HttpStatusCode.OK, out var captured);

        await sender.SendAsync(null, "REF-001", "Completed", "ACH", "secret", NullLogger.Instance);

        Assert.Empty(captured);
    }

    [Fact]
    public async Task SendAsync_ServerReturns500_DoesNotThrow()
    {
        var sender = CreateSender(HttpStatusCode.InternalServerError, out _);

        var ex = await Record.ExceptionAsync(() =>
            sender.SendAsync("abc-123", "REF-001", "Completed", "ACH", "secret", NullLogger.Instance));

        Assert.Null(ex);
    }

    private class CapturingHandler(HttpStatusCode statusCode, List<CapturedRequest> captured) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken) : "";
            var secret = request.Headers.TryGetValues("X-Webhook-Secret", out var vals) ? vals : [];
            captured.Add(new CapturedRequest(request.Method, request.RequestUri, body, secret));
            return new HttpResponseMessage(statusCode);
        }
    }
}
