namespace UsBankSystem.Api.Integrations;

public interface IKlikP2pClient
{
    Task<KlikP2pRegisterResult> RegisterAliasAsync(string phone, string routingNumber, string accountNumber, CancellationToken ct = default);
    Task<KlikP2pLookupResult>  LookupAliasAsync(string phone, CancellationToken ct = default);
    Task                       DeleteAliasAsync(string phone, CancellationToken ct = default);
}

public record KlikP2pRegisterResult(string AliasId, DateTime RegisteredAt);
public record KlikP2pLookupResult(string BankId, string? RoutingNumber, string? AccountNumber);
