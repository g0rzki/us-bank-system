namespace UsBankSystem.Core.Domain.Common;

public static class AccountStatus
{
    public const string Active = "active";
    public const string Inactive = "inactive";
    public const string Blocked = "blocked";

    private static readonly HashSet<string> All = [Active, Inactive, Blocked];

    public static bool IsValid(string status) => All.Contains(status);
}
