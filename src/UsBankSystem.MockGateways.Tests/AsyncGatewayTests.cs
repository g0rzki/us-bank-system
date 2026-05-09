namespace UsBankSystem.MockGateways.Tests;

public class AsyncGatewayTests
{
    [Fact]
    public void TryExtractTransferId_PascalCase_ReturnsId()
    {
        var body = """{"TransferId":"abc-123","Amount":100}""";
        Assert.Equal("abc-123", AsyncGateway.TryExtractTransferId(body));
    }

    [Fact]
    public void TryExtractTransferId_CamelCase_ReturnsId()
    {
        var body = """{"transferId":"abc-123","Amount":100}""";
        Assert.Equal("abc-123", AsyncGateway.TryExtractTransferId(body));
    }

    [Fact]
    public void TryExtractTransferId_MissingField_ReturnsNull()
    {
        var body = """{"Amount":100,"Currency":"USD"}""";
        Assert.Null(AsyncGateway.TryExtractTransferId(body));
    }

    [Fact]
    public void TryExtractTransferId_InvalidJson_ReturnsNull()
    {
        Assert.Null(AsyncGateway.TryExtractTransferId("not-json"));
    }

    [Fact]
    public void TryExtractTransferId_EmptyBody_ReturnsNull()
    {
        Assert.Null(AsyncGateway.TryExtractTransferId(""));
    }
}
