namespace UsBankSystem.Api.Configuration;

public class PaymentSessionConfig
{
    public AchConfig Ach { get; set; } = new();
    public FedNowConfig FedNow { get; set; } = new();
    public TimeoutConfig Rtp { get; set; } = new();
    public SwiftConfig Swift { get; set; } = new();
}

public class AchConfig
{
    public int BatchWindowMinutes { get; set; } = 180;
    public int CutoffHour { get; set; } = 17;
}

public class TimeoutConfig
{
    public int TimeoutSeconds { get; set; } = 20;
}

public class FedNowConfig
{
    public int TimeoutSeconds { get; set; } = 30;
    public int PollIntervalSeconds { get; set; } = 1;
    public string BankRtn { get; set; } = "040104018";
    public string BankLegalName { get; set; } = "Baguette Bank";
}

public class SwiftConfig : TimeoutConfig
{
    public decimal DailyLimitPerAccount { get; set; } = 50_000m;
}