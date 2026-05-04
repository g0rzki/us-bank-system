using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using UsBankSystem.Api.Integrations;
using UsBankSystem.Core.Domain;
using UsBankSystem.Core.Domain.Transfers;
using UsBankSystem.Tests.Helpers;

namespace UsBankSystem.Tests.Integrations;

public class PaymentGatewayTests
{
    private static AchGateway CreateGateway(HttpStatusCode statusCode, string responseBody)
    {
        var handler = new MockHttpMessageHandler(statusCode, responseBody);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:6001") };
        return new AchGateway(httpClient, NullLogger<AchGateway>.Instance);
    }

    private static PaymentGatewayRequest CreateRequest() => new(
        TransferId: Guid.NewGuid(),
        Amount: 100m,
        Currency: "USD",
        Description: "Test transfer"
    );

    [Fact]
    public async Task SendAsync_SuccessResponse_ReturnsSuccess()
    {
        var gateway = CreateGateway(HttpStatusCode.OK, """{"referenceId":"REF-001"}""");
        var result = await gateway.SendAsync(CreateRequest());
        Assert.True(result.Success);
        Assert.Equal("REF-001", result.ExternalReferenceId);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task SendAsync_FailureResponse_ReturnsFailure()
    {
        var gateway = CreateGateway(HttpStatusCode.BadRequest, """{"error":"Invalid request"}""");
        var result = await gateway.SendAsync(CreateRequest());
        Assert.False(result.Success);
        Assert.Null(result.ExternalReferenceId);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task SendAsync_ServerError_ReturnsFailure()
    {
        var gateway = CreateGateway(HttpStatusCode.InternalServerError, "");
        var result = await gateway.SendAsync(CreateRequest());
        Assert.False(result.Success);
    }

    [Fact]
    public void AchGateway_Channel_IsAch()
    {
        var gateway = CreateGateway(HttpStatusCode.OK, "{}");
        Assert.Equal(TransferChannel.Ach, gateway.Channel);
    }
}
