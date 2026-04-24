namespace UsBankSystem.Core.Domain.Transfers;

public static class TransferStatus
{
    public const string PendingApproval = "pending_approval";
    public const string Completed = "completed";
    public const string Rejected = "rejected";
    public const string Failed = "failed";

    private static readonly HashSet<string> All = [PendingApproval, Completed, Rejected, Failed];

    public static bool IsValid(string status) => All.Contains(status);
}
