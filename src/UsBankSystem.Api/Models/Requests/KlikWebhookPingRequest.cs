namespace UsBankSystem.Api.Models.Requests;

public class KlikWebhookPingRequest
{
    public DateTime Timestamp { get; set; }
    public string Nonce { get; set; } = null!;
}
