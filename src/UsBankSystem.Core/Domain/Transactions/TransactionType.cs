namespace UsBankSystem.Core.Domain.Transactions;

public static class TransactionType
{
    public const string Credit = "credit";
    public const string Debit = "debit";

    private static readonly HashSet<string> All = [Credit, Debit];

    public static bool IsValid(string type) => All.Contains(type);
}