namespace UsBankSystem.Core.Domain.Accounts;

public static class AccountType
{
    public const string Checking = "checking";
    public const string Savings = "savings";

    public static bool IsValid(string type) =>
        type == Checking || type == Savings;
}
