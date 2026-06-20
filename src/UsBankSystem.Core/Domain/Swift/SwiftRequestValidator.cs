namespace UsBankSystem.Core.Domain.Swift;

public static class SwiftRequestValidator
{
    private static readonly HashSet<string> ValidChargeBearers = ["SHA", "OUR", "BEN"];

    // ISO 4217 — major currencies supported by SWIFT
    private static readonly HashSet<string> SupportedCurrencies =
    [
        "USD", "EUR", "GBP", "CHF", "JPY", "AUD", "CAD", "NOK", "SEK", "DKK",
        "NZD", "SGD", "HKD", "PLN", "CZK", "HUF", "RON", "BGN", "TRY", "ZAR"
    ];

    public static void Validate(string iban, string bic, string chargeBearer, string currency, DateOnly? valueDate)
    {
        if (!IbanValidator.IsValid(iban))
            throw new ArgumentException($"Invalid IBAN: '{iban}'");

        if (!BicValidator.IsValid(bic))
            throw new ArgumentException($"Invalid BIC: '{bic}'");

        if (!ValidChargeBearers.Contains(chargeBearer.ToUpperInvariant()))
            throw new ArgumentException($"Invalid ChargeBearer '{chargeBearer}'. Must be SHA, OUR or BEN");

        if (currency.ToUpperInvariant() != "USD")
            throw new ArgumentException("Outgoing SWIFT transfers must be in USD. Incoming transfers in other currencies are auto-converted on receipt.");

        if (valueDate.HasValue && valueDate.Value < DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ArgumentException("ValueDate cannot be in the past");
    }

    public static void ValidateDailyLimit(decimal todayTotal, decimal requestedAmount, decimal dailyLimit)
    {
        if (todayTotal + requestedAmount > dailyLimit)
            throw new ArgumentException($"Daily SWIFT limit exceeded. Limit: {dailyLimit}, used: {todayTotal}, requested: {requestedAmount}");
    }

    public static DateOnly ResolveValueDate(DateOnly? requested) =>
        requested ?? NextBusinessDay(DateOnly.FromDateTime(DateTime.UtcNow));

    private static DateOnly NextBusinessDay(DateOnly date)
    {
        var next = date.AddDays(1);
        while (next.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            next = next.AddDays(1);
        return next;
    }
}
