namespace UsBankSystem.Core.Entities;

public class ExchangeRate
{
    public string CurrencyCode { get; set; } = null!;
    public decimal RateToUsd { get; set; }
    public DateTime UpdatedAt { get; set; }
}
