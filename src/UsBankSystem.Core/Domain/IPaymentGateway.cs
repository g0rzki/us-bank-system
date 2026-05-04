namespace UsBankSystem.Core.Domain;

public interface IPaymentGateway
{
    string Channel { get; }
    Task<PaymentGatewayResult> SendAsync(PaymentGatewayRequest request, CancellationToken cancellationToken = default);
}

public record PaymentGatewayRequest(
    Guid TransferId,
    decimal Amount,
    string Currency,
    string? Description,
    Dictionary<string, string>? Metadata = null
);

public record PaymentGatewayResult(
    bool Success,
    string? ExternalReferenceId,
    string? Error
);