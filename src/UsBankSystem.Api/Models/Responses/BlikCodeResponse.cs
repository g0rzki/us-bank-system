namespace UsBankSystem.Api.Models.Responses;

public class BlikCodeResponse
{
    public string Code { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public int ExpiresIn { get; set; }
}
