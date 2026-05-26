using System.ComponentModel.DataAnnotations;

namespace UsBankSystem.Api.Models.Requests;

public class UpdateCardStatusRequest
{
    [Required]
    public string Status { get; set; } = null!;
}
