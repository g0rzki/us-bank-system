namespace UsBankSystem.Core.Domain.Transfers;

public static class TransferChannel
{
    public const string Internal = "internal";
    public const string Ach = "ach";
    public const string Rtp = "rtp";
    public const string FedNow = "fednow";
    public const string Swift = "swift";
    public const string BlikP2p = "blik_p2p";

    private static readonly HashSet<string> All = [Internal, Ach, Rtp, FedNow, Swift, BlikP2p];

    public static bool IsValid(string channel) => All.Contains(channel);
}
