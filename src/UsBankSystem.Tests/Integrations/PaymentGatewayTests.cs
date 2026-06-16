using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using UsBankSystem.Api.Integrations;
using UsBankSystem.Core.Domain;
using UsBankSystem.Core.Domain.Transfers;
using UsBankSystem.Tests.Helpers;

namespace UsBankSystem.Tests.Integrations;

public class PaymentGatewayTests
{
    private static AchGateway CreateGateway(HttpStatusCode statusCode, string responseBody) =>
        AchTestHelpers.CreateGateway(statusCode, responseBody);

    private static PaymentGatewayRequest CreateRequest(Guid? transferId = null) => new(
        TransferId: transferId ?? Guid.NewGuid(),
        Amount: 100m,
        Currency: "USD",
        Description: "Test transfer",
        Metadata: new Dictionary<string, string>
        {
            ["toRoutingNumber"] = "021000021",
            ["toAccountNumber"] = "123456789",
            ["recipientName"] = "John Doe",
            ["accountType"] = "checking"
        }
    );

    [Fact]
    public async Task SendAsync_SuccessResponse_ReturnsSuccess()
    {
        var transferId = Guid.NewGuid();
        var gateway = CreateGateway(HttpStatusCode.OK, "NACHA_FILE_BYTES");
        var result = await gateway.SendAsync(CreateRequest(transferId));
        Assert.True(result.Success);
        Assert.Equal(AchGateway.ComputeFileId(transferId), result.ExternalReferenceId);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task SendAsync_AchHelperFailure_ReturnsFailure()
    {
        var gateway = CreateGateway(HttpStatusCode.BadRequest, """{"error":"Invalid routing number"}""");
        var result = await gateway.SendAsync(CreateRequest());
        Assert.False(result.Success);
        Assert.Null(result.ExternalReferenceId);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task SendAsync_AchHelperServerError_ReturnsFailure()
    {
        var gateway = CreateGateway(HttpStatusCode.InternalServerError, "");
        var result = await gateway.SendAsync(CreateRequest());
        Assert.False(result.Success);
    }

    [Fact]
    public async Task SendAsync_MissingMetadata_ReturnsValidationError()
    {
        var gateway = CreateGateway(HttpStatusCode.OK, "NACHA_FILE_BYTES");
        var request = new PaymentGatewayRequest(
            TransferId: Guid.NewGuid(), Amount: 100m, Currency: "USD", Description: null);
        var result = await gateway.SendAsync(request);
        Assert.False(result.Success);
        Assert.Contains("required", result.Error);
    }

    [Fact]
    public void AchGateway_Channel_IsAch()
    {
        var gateway = CreateGateway(HttpStatusCode.OK, "{}");
        Assert.Equal(TransferChannel.Ach, gateway.Channel);
    }
}
