using UsBankSystem.Core.Domain.Transfers;

namespace UsBankSystem.Api.Integrations;

public class AchGateway(HttpClient httpClient, ILogger<AchGateway> logger)
    : PaymentGatewayBase(httpClient, logger)
{
    public override string Channel => TransferChannel.Ach;
    protected override string Endpoint => "/transfers";
}