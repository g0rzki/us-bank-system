using UsBankSystem.Core.Domain.Transfers;

namespace UsBankSystem.Api.Integrations;

public class FedNowGateway(HttpClient httpClient, ILogger<FedNowGateway> logger)
    : PaymentGatewayBase(httpClient, logger)
{
    public override string Channel => TransferChannel.FedNow;
    protected override string Endpoint => "/transfers";
}