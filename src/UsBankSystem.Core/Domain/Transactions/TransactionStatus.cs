namespace UsBankSystem.Core.Domain.Transactions;

public static class TransactionStatus
{
    public const string Pending = "pending";
    public const string Completed = "completed";
    public const string Failed = "failed";

    private static readonly HashSet<string> All = [Pending, Completed, Failed];

    public static bool IsValid(string status) => All.Contains(status);
}