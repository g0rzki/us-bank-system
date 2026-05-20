using System.ComponentModel.DataAnnotations;

namespace UsBankSystem.Api.Models.Requests;

public class UpdateJuniorLimitRequest
{
    [Range(0.01, double.MaxValue)]
    public decimal? DailyLimit { get; set; }

    [Range(0.01, double.MaxValue)]
    public decimal? MonthlyLimit { get; set; }
}