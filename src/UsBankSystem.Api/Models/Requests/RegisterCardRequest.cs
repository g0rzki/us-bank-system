using System.ComponentModel.DataAnnotations;

namespace UsBankSystem.Api.Models.Requests;

public class RegisterCardRequest
{
    [Required]
    public string Type { get; set; } = null!;

    [Range(10.00, 10000.00)]
    public decimal? DailyLimit { get; set; }

    [Range(50.00, 100000.00)]
    public decimal? MonthlyLimit { get; set; }
}
