namespace UsBankSystem.Core.Domain.Common;

public static class CurrencyCode
{
    public const string USD = "USD";

    private static readonly HashSet<string> Supported = [USD];

    public static bool IsValid(string currency) =>
        Supported.Contains(currency.ToUpperInvariant());
}
