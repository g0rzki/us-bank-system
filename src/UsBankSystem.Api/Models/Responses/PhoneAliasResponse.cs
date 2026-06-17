namespace UsBankSystem.Api.Models.Responses;

public class PhoneAliasResponse
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Phone { get; set; } = null!;
    public string KlikAliasId { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime RegisteredAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
