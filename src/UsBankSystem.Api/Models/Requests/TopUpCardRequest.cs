using System.ComponentModel.DataAnnotations;

namespace UsBankSystem.Api.Models.Requests;

public class TopUpCardRequest
{
    [Required]
    [Range(1.00, 100000.00)]
    public decimal Amount { get; set; }
}
