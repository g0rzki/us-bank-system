using System.ComponentModel.DataAnnotations;

namespace UsBankSystem.Api.Models.Requests;

public class SendToPhoneRequest
{
    [Required]
    public Guid FromAccountId { get; set; }

    [Required]
    public string Phone { get; set; } = null!;

    [Required]
    [Range(0.01, double.MaxValue)]
    public decimal Amount { get; set; }

    public string Currency { get; set; } = "USD";
    public string? Description { get; set; }
}
