namespace UsBankSystem.Core.Domain.Cards;

public static class CardStatus
{
    public const string Active = "active";
    public const string Blocked = "blocked";
    public const string Expired = "expired";

    private static readonly HashSet<string> All = [Active, Blocked, Expired];
    private static readonly HashSet<string> UserSettable = [Active, Blocked];

    public static bool IsValid(string status) => All.Contains(status);
    public static bool IsUserSettable(string status) => UserSettable.Contains(status);
}
