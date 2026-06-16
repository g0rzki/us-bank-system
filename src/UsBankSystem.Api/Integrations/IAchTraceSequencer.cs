namespace UsBankSystem.Api.Integrations;

public interface IAchTraceSequencer
{
    Task<int> NextAsync(CancellationToken ct = default);
}
