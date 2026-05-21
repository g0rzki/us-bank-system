using System.ComponentModel.DataAnnotations;

namespace UsBankSystem.Api.Models.Requests;

public class AddJuniorCardRequest
{
    [Required]
    [MaxLength(4)]
    public string Last4 { get; set; } = null!;

    [Required]
    public DateTime ExpiresAt { get; set; }

    public string? ExternalCardToken { get; set; }

    [Range(0.01, double.MaxValue)]
    public decimal? DailyLimit { get; set; }

    [Range(0.01, double.MaxValue)]
    public decimal? MonthlyLimit { get; set; }
}