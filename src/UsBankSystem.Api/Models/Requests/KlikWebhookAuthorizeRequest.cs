using System.Text.Json.Serialization;

namespace UsBankSystem.Api.Models.Requests;

public class KlikWebhookAuthorizeRequest
{
    [JsonPropertyName("transaction_id")] public string TransactionId { get; set; } = null!;
    [JsonPropertyName("user_id")]        public string UserId { get; set; } = null!;
    [JsonPropertyName("amount")]         public decimal Amount { get; set; }
    [JsonPropertyName("currency")]       public string Currency { get; set; } = null!;
    [JsonPropertyName("merchant_name")]  public string MerchantName { get; set; } = null!;
    [JsonPropertyName("is_on_us")]       public bool IsOnUs { get; set; }
    [JsonPropertyName("expiry_time")]    public DateTime ExpiryTime { get; set; }
    [JsonPropertyName("zone")]           public string Zone { get; set; } = null!;
}
