using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UsBankSystem.Core.Domain.Cards;
using UsBankSystem.Core.Entities;
using UsBankSystem.Infrastructure.Persistence;

namespace UsBankSystem.Api.Controllers;

/// <summary>
/// Endpoint wywoływany przez Card Provider (payment-gateway) po settlement transakcji kartowej.
/// Zgodnie z kontraktem: POST {bank_url}/capture
/// </summary>
[ApiController]
[Route("capture")]
[AllowAnonymous]
[Tags("Cards")]
public class CaptureController(AppDbContext db, ILogger<CaptureController> logger) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Capture([FromBody] CaptureRequest request)
    {
        if (request.Amount <= 0)
        {
            logger.LogWarning("Capture: invalid amount {Amount}", request.Amount);
            return BadRequest(new { error = "Amount must be greater than zero" });
        }

        if (request.Currency is not null && request.Currency != "USD")
        {
            logger.LogWarning("Capture: unsupported currency {Currency} for tx {TransactionId}",
                request.Currency, request.TransactionId);
            return BadRequest(new { error = $"Unsupported currency '{request.Currency}'" });
        }

        logger.LogInformation(
            "Card settlement capture received: txId={TransactionId} authCode={AuthCode} amount={Amount} token={CardToken}",
            request.TransactionId, request.AuthorizationCode, request.Amount, request.CardToken);

        var card = request.CardToken is not null
            ? await db.Cards.Include(c => c.Account).FirstOrDefaultAsync(c => c.ExternalCardToken == request.CardToken)
            : null;

        if (card is null)
        {
            logger.LogWarning("Capture: card not found for token {Token}", request.CardToken);
            // Zwracamy 200 — nie chcemy blokować settlement w card-provider gdy karta nie jest znana
            return Ok(new { status = "SETTLED" });
        }

        if (card.Status == CardStatus.Expired)
        {
            logger.LogWarning("Capture: card {CardId} is expired, token {Token}", card.Id, request.CardToken);
            return Ok(new { status = "SETTLED" });
        }

        var maskedPan = card.MaskedPan is not null ? $" ({card.MaskedPan})" : string.Empty;
        var merchantPart = request.MerchantId is not null ? $" · {request.MerchantId}" : string.Empty;

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            AccountId = card.AccountId,
            Amount = (decimal)request.Amount,
            Type = "debit",
            Status = "completed",
            Description = $"Card payment{maskedPan}{merchantPart}",
            ReferenceId = request.AuthorizationCode,
            CreatedAt = DateTime.UtcNow
        };

        db.Transactions.Add(transaction);
        if (card.Type == CardType.Debit)
            card.Account.Balance -= transaction.Amount;
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Card settlement recorded: txId={TransactionId} accountId={AccountId} amount={Amount}",
            transaction.Id, card.AccountId, transaction.Amount);

        return Ok(new { status = "SETTLED" });
    }
}

public record CaptureRequest(
    [property: JsonPropertyName("transaction_id")] string? TransactionId,
    [property: JsonPropertyName("authorization_code")] string? AuthorizationCode,
    [property: JsonPropertyName("amount")] double Amount,
    [property: JsonPropertyName("currency")] string? Currency,
    [property: JsonPropertyName("merchant_id")] string? MerchantId,
    [property: JsonPropertyName("card_token")] string? CardToken);
