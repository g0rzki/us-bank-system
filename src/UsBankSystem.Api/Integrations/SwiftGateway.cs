using UsBankSystem.Core.Domain.Transfers;

namespace UsBankSystem.Api.Integrations;

public class SwiftGateway(HttpClient httpClient, ILogger<SwiftGateway> logger)
    : PaymentGatewayBase(httpClient, logger)
{
    public override string Channel => TransferChannel.Swift;
    protected override string Endpoint => "/transfers";
}