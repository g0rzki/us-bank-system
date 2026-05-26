using System.ComponentModel.DataAnnotations;

namespace UsBankSystem.Api.Models.Requests;

public class AddJuniorCardRequest
{
    [Range(10.00, 10000.00)]
    public decimal? DailyLimit { get; set; }

    [Range(50.00, 100000.00)]
    public decimal? MonthlyLimit { get; set; }
}