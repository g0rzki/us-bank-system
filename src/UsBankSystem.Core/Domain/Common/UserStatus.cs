namespace UsBankSystem.Core.Domain.Common;

public static class UserStatus
{
    public const string Active = "active";
    public const string Inactive = "inactive";

    private static readonly HashSet<string> All = [Active, Inactive];

    public static bool IsValid(string status) => All.Contains(status);
}
