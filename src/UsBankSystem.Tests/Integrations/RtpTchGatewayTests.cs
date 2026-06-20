using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using UsBankSystem.Api.Integrations.Rtp;
using UsBankSystem.Tests.Helpers;

namespace UsBankSystem.Tests.Integrations;

public class RtpTchGatewayTests
{
    private static RtpTchGateway CreateGateway(HttpStatusCode status, string body = "<xml/>") =>
        new(new HttpClient(new MockHttpMessageHandler(status, body))
            { BaseAddress = new Uri("http://localhost:8200") },
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Rtp:ApiKey"] = "test" }).Build(),
        NullLogger<RtpTchGateway>.Instance);

    [Fact]
    public async Task SendPacs008_Success_ReturnsTrue()
    {
        var gateway = CreateGateway(HttpStatusCode.OK, "<response/>");
        var result = await gateway.SendPacs008Async("<xml/>");
        Assert.True(result.Success);
        Assert.Equal("<response/>", result.ResponseXml);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task SendPacs008_Failure_ReturnsFalse()
    {
        var gateway = CreateGateway(HttpStatusCode.BadRequest, "error");
        var result = await gateway.SendPacs008Async("<xml/>");
        Assert.False(result.Success);
        Assert.Contains("TCH error", result.Error);
    }

    [Fact]
    public async Task PollIncoming_Success_ReturnsMessages()
    {
        var json = """{"messages":[{"queue_id":1,"message_id":"m1","type":"pacs.002","payload":"<xml/>"}]}""";
        var gateway = CreateGateway(HttpStatusCode.OK, json);
        var messages = await gateway.PollIncomingAsync();
        Assert.Single(messages);
        Assert.Equal(1, messages[0].QueueId);
        Assert.Equal("m1", messages[0].MessageId);
        Assert.Equal("pacs.002", messages[0].Type);
        Assert.Equal("<xml/>", messages[0].Payload);
    }

    [Fact]
    public async Task PollIncoming_Failure_ReturnsEmptyList()
    {
        var gateway = CreateGateway(HttpStatusCode.InternalServerError);
        var messages = await gateway.PollIncomingAsync();
        Assert.Empty(messages);
    }

    [Fact]
    public async Task Settle_Success_ReturnsTrue()
    {
        var gateway = CreateGateway(HttpStatusCode.OK);
        var ok = await gateway.SettleAsync("<xml/>");
        Assert.True(ok);
    }

    [Fact]
    public async Task Settle_Failure_ReturnsFalse()
    {
        var gateway = CreateGateway(HttpStatusCode.BadRequest);
        var ok = await gateway.SettleAsync("<xml/>");
        Assert.False(ok);
    }
}
