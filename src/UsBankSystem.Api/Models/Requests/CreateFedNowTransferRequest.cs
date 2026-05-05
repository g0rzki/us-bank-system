using System.ComponentModel.DataAnnotations;

namespace UsBankSystem.Api.Models.Requests;

public class CreateFedNowTransferRequest
{
    [Required]
    public Guid FromAccountId { get; set; }

    [Required]
    public Guid ToAccountId { get; set; }

    [Required]
    [Range(0.01, double.MaxValue)]
    public decimal Amount { get; set; }

    public string Currency { get; set; } = "USD";
    public string? Description { get; set; }
}