using System.Text.Json;

// ACH / SWIFT: responds immediately with referenceId, fires webhook after delay
static class AsyncGateway
{
    public static WebApplication Build(string channel, int port, int webhookDelaySeconds, WebhookSender webhookSender, string webhookSecret)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
        ConfigureLogging(builder);

        var app = builder.Build();
        var logger = app.Services.GetRequiredService<ILogger<WebApplication>>();

        app.MapPost("/transfers", async (HttpRequest req) =>
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            var fail = req.Query.ContainsKey("fail");

            var referenceId = $"{channel}-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
            logger.LogInformation("[{Channel}] accepted transfer referenceId={Ref}, scheduling webhook in {Delay}s (fail={Fail})",
                channel, referenceId, webhookDelaySeconds, fail);

            var transferId = TryExtractTransferId(body);

            // Fire-and-forget the delayed webhook — response goes back immediately
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(webhookDelaySeconds));
                var status = fail ? "failed" : "completed";
                await webhookSender.SendAsync(transferId, referenceId, status, channel, webhookSecret, logger);
            });

            return Results.Ok(new { referenceId });
        });

        app.MapGet("/health", () => Results.Ok(new
        {
            channel, port, mode = "async", webhookDelaySeconds
        }));

        return app;
    }

    internal static string? TryExtractTransferId(string body)
    {
        try
        {
            var doc = JsonDocument.Parse(body);
            // PaymentGatewayBase serializes with PascalCase (record properties)
            if (doc.RootElement.TryGetProperty("TransferId", out var v) ||
                doc.RootElement.TryGetProperty("transferId", out v))
                return v.GetString();
        }
        catch (JsonException) { }
        return null;
    }

    private static void ConfigureLogging(WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
    }
}
