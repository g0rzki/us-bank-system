using System.Text.RegularExpressions;

namespace UsBankSystem.Core.Domain.Swift;

public static class BicValidator
{
    // BIC is 8 or 11 alphanumeric characters: 4 bank + 2 country + 2 location + optional 3 branch
    private static readonly Regex BicRegex = new(@"^[A-Z]{4}[A-Z]{2}[A-Z0-9]{2}([A-Z0-9]{3})?$", RegexOptions.Compiled);

    public static bool IsValid(string bic)
    {
        if (string.IsNullOrWhiteSpace(bic))
            return false;

        return BicRegex.IsMatch(bic.ToUpperInvariant());
    }
}
