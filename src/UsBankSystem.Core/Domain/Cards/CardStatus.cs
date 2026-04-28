namespace UsBankSystem.Core.Domain.Cards;

public static class CardStatus
{
    public const string Active = "active";
    public const string Blocked = "blocked";

    private static readonly HashSet<string> All = [Active, Blocked];

    public static bool IsValid(string status) => All.Contains(status);
}
