namespace UsBankSystem.Core.Domain.Cards;

public static class CardType
{
    public const string Debit = "debit";
    public const string Prepaid = "prepaid";

    private static readonly HashSet<string> All = [Debit, Prepaid];

    public static bool IsValid(string type) => All.Contains(type);
}
