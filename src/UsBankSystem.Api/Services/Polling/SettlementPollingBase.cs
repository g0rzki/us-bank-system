namespace UsBankSystem.Api.Services.Polling;

/// <summary>
/// Base BackgroundService that polls on a configurable interval.
/// Subclasses implement PollAsync; exceptions are logged and swallowed so the loop never dies.
/// </summary>
public abstract class SettlementPollingBase(ILogger logger) : BackgroundService
{
    protected abstract TimeSpan Interval { get; }
    protected abstract Task PollAsync(CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("{Service} polling started (interval: {Interval})", GetType().Name, Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{Service} poll cycle failed", GetType().Name);
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
