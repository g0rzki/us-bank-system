using System.Text.Json;

class MockConfig
{
    public string ApiUrl { get; init; } = "http://localhost:5100";
    public string WebhookSecret { get; init; } = "";

    // Delay before webhook fires (ACH simulates batch window, SWIFT simulates settlement)
    public int AchDelaySeconds { get; init; } = 10;
    public int SwiftDelaySeconds { get; init; } = 15;

    // Delay before sync response (real-time rails)
    public int RtpDelaySeconds { get; init; } = 3;
    public int FedNowDelaySeconds { get; init; } = 3;

    public static MockConfig Load()
    {
        var apiUrl = Environment.GetEnvironmentVariable("API_URL") ?? "http://localhost:5100";
        var webhookSecret = Environment.GetEnvironmentVariable("WEBHOOK_SECRET") ?? "";

        var configPath = Environment.GetEnvironmentVariable("PAYMENT_CONFIG_PATH")
            ?? Path.Combine(AppContext.BaseDirectory, "payment-config.json");

        int achDelay = 10, swiftDelay = 15, rtpDelay = 3, fedNowDelay = 3;

        if (File.Exists(configPath))
        {
            using var stream = File.OpenRead(configPath);
            var root = JsonSerializer.Deserialize<ConfigRoot>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (root?.PaymentSessions is { } ps)
            {
                // ACH: BatchWindowMinutes drives the simulated batch delay, capped at 300s for dev
                achDelay = Math.Min(ps.Ach.BatchWindowMinutes * 60, 300);
                swiftDelay = ps.Swift.TimeoutSeconds;
                rtpDelay = ps.Rtp.TimeoutSeconds;
                fedNowDelay = ps.FedNow.TimeoutSeconds;
            }
        }
        else
        {
            Console.Error.WriteLine($"[MockGateways] payment-config.json not found at {configPath}, using defaults");
        }

        return new MockConfig
        {
            ApiUrl = apiUrl,
            WebhookSecret = webhookSecret,
            AchDelaySeconds = achDelay,
            SwiftDelaySeconds = swiftDelay,
            RtpDelaySeconds = rtpDelay,
            FedNowDelaySeconds = fedNowDelay,
        };
    }

    private record ConfigRoot(PaymentSessions PaymentSessions);
    private record PaymentSessions(AchEntry Ach, TimeoutEntry Rtp, TimeoutEntry FedNow, TimeoutEntry Swift);
    private record AchEntry(int BatchWindowMinutes);
    private record TimeoutEntry(int TimeoutSeconds);
}
