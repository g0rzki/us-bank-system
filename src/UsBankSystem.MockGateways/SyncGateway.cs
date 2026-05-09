// RTP / FedNow: real-time rail — waits then responds synchronously with final status
static class SyncGateway
{
    public static WebApplication Build(string channel, int port, int delaySeconds)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
        ConfigureLogging(builder);

        var app = builder.Build();
        var logger = app.Services.GetRequiredService<ILogger<WebApplication>>();

        app.MapPost("/transfers", async (HttpRequest req) =>
        {
            var fail = req.Query.ContainsKey("fail");
            logger.LogInformation("[{Channel}] processing transfer, settling in {Delay}s", channel, delaySeconds);

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, delaySeconds - 1)));

            if (fail)
            {
                logger.LogWarning("[{Channel}] simulating failure", channel);
                return Results.BadRequest(new { error = "Simulated gateway failure" });
            }

            var referenceId = $"{channel}-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
            logger.LogInformation("[{Channel}] settled referenceId={Ref}", channel, referenceId);

            return Results.Ok(new { referenceId, status = "completed" });
        });

        app.MapGet("/health", () => Results.Ok(new
        {
            channel, port, mode = "sync", delaySeconds
        }));

        return app;
    }

    private static void ConfigureLogging(WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
    }
}
