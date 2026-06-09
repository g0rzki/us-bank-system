namespace UsBankSystem.Api.Integrations;

public interface IKlikApiClient
{
    Task<KlikGenerateCodeResult> GenerateCodeAsync(string userId, CancellationToken ct = default);
    Task<KlikConfirmResult> ConfirmPaymentAsync(string transactionId, bool accepted, string? rejectReason, CancellationToken ct = default);
}

public record KlikGenerateCodeResult(bool Success, string? Code, DateTime? ExpiresAt, string? Error);
public record KlikConfirmResult(bool Success, string? Error);
