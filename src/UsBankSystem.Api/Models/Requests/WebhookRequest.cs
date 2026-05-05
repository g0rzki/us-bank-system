using System.ComponentModel.DataAnnotations;

namespace UsBankSystem.Api.Models.Requests;

public class WebhookRequest
{
    [Required] public string Status { get; set; } = null!;
    public string? ReferenceId { get; set; }
}
