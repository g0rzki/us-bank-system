DotNetEnv.Env.TraversePath().Load();

var config = MockConfig.Load();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var webhookSender = new WebhookSender(config.ApiUrl);

var apps = new[]
{
    // Async: responds immediately, fires webhook after delay
    AsyncGateway.Build("ACH",    6001, config.AchDelaySeconds,    webhookSender, config.WebhookSecret),
    AsyncGateway.Build("SWIFT",  6004, config.SwiftDelaySeconds,  webhookSender, config.WebhookSecret),

    // Sync: waits, then responds with completed/failed
    SyncGateway.Build("RTP",     6002, config.RtpDelaySeconds),
    SyncGateway.Build("FEDNOW",  6003, config.FedNowDelaySeconds),

    // KLIK C2B mock: code generation, agent simulation, payment confirm
    KlikMockGateway.Build(6006, config.ApiUrl, config.KlikWebhookSecret),
};

await Task.WhenAll(apps.Select(a => a.RunAsync(cts.Token)));
