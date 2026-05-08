using System.Text.RegularExpressions;

namespace UsBankSystem.Core.Domain.Swift;

public static class IbanValidator
{
    public static bool IsValid(string iban)
    {
        if (string.IsNullOrWhiteSpace(iban))
            return false;

        var normalized = iban.Replace(" ", "").ToUpperInvariant();

        if (!Regex.IsMatch(normalized, @"^[A-Z]{2}\d{2}[A-Z0-9]{1,30}$"))
            return false;

        // MOD-97 checksum: move first 4 chars to end, convert letters to digits
        var rearranged = normalized[4..] + normalized[..4];
        var numeric = string.Concat(rearranged.Select(c => char.IsLetter(c) ? (c - 'A' + 10).ToString() : c.ToString()));

        // Calculate MOD 97 in chunks to avoid integer overflow
        var remainder = 0;
        foreach (var ch in numeric)
            remainder = (remainder * 10 + (ch - '0')) % 97;

        return remainder == 1;
    }
}
