using UsBankSystem.Core.Domain.Transfers;

namespace UsBankSystem.Api.Integrations;

public class RtpGateway(HttpClient httpClient, ILogger<RtpGateway> logger)
    : PaymentGatewayBase(httpClient, logger)
{
    public override string Channel => TransferChannel.Rtp;
    protected override string Endpoint => "/transfers";
}