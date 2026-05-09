namespace UsBankSystem.MockGateways.Tests;

public class MockConfigTests
{
    [Fact]
    public void Load_MissingConfigFile_UsesDefaults()
    {
        Environment.SetEnvironmentVariable("PAYMENT_CONFIG_PATH", "/nonexistent/path.json");
        Environment.SetEnvironmentVariable("API_URL", null);
        Environment.SetEnvironmentVariable("WEBHOOK_SECRET", null);

        var config = MockConfig.Load();

        Assert.Equal(10, config.AchDelaySeconds);
        Assert.Equal(15, config.SwiftDelaySeconds);
        Assert.Equal(3, config.RtpDelaySeconds);
        Assert.Equal(3, config.FedNowDelaySeconds);

        Environment.SetEnvironmentVariable("PAYMENT_CONFIG_PATH", null);
    }

    [Fact]
    public void Load_ValidConfigFile_ReadsTimeouts()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """
            {
              "PaymentSessions": {
                "Ach": { "BatchWindowMinutes": 2 },
                "Rtp": { "TimeoutSeconds": 7 },
                "FedNow": { "TimeoutSeconds": 8 },
                "Swift": { "TimeoutSeconds": 25 }
              }
            }
            """);
        Environment.SetEnvironmentVariable("PAYMENT_CONFIG_PATH", path);

        var config = MockConfig.Load();

        Assert.Equal(120, config.AchDelaySeconds);
        Assert.Equal(7, config.RtpDelaySeconds);
        Assert.Equal(8, config.FedNowDelaySeconds);
        Assert.Equal(25, config.SwiftDelaySeconds);

        Environment.SetEnvironmentVariable("PAYMENT_CONFIG_PATH", null);
        File.Delete(path);
    }

    [Fact]
    public void Load_AchBatchWindow_CappedAt300Seconds()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """
            {
              "PaymentSessions": {
                "Ach": { "BatchWindowMinutes": 180 },
                "Rtp": { "TimeoutSeconds": 10 },
                "FedNow": { "TimeoutSeconds": 10 },
                "Swift": { "TimeoutSeconds": 30 }
              }
            }
            """);
        Environment.SetEnvironmentVariable("PAYMENT_CONFIG_PATH", path);

        var config = MockConfig.Load();

        Assert.Equal(300, config.AchDelaySeconds);

        Environment.SetEnvironmentVariable("PAYMENT_CONFIG_PATH", null);
        File.Delete(path);
    }

    [Fact]
    public void Load_EnvVars_SetApiUrlAndSecret()
    {
        Environment.SetEnvironmentVariable("API_URL", "http://test-api:9999");
        Environment.SetEnvironmentVariable("WEBHOOK_SECRET", "my-secret");
        Environment.SetEnvironmentVariable("PAYMENT_CONFIG_PATH", "/nonexistent/path.json");

        var config = MockConfig.Load();

        Assert.Equal("http://test-api:9999", config.ApiUrl);
        Assert.Equal("my-secret", config.WebhookSecret);

        Environment.SetEnvironmentVariable("API_URL", null);
        Environment.SetEnvironmentVariable("WEBHOOK_SECRET", null);
        Environment.SetEnvironmentVariable("PAYMENT_CONFIG_PATH", null);
    }
}
