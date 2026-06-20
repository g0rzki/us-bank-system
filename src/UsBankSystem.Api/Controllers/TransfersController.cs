using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UsBankSystem.Api.Integrations;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Api.Services;
using UsBankSystem.Api.Services.Payments;

namespace UsBankSystem.Api.Controllers;

[ApiController]
[Route("transfers")]
[Tags("Transfers")]
public class TransfersController(
    TransferService transferService,
    InternalPaymentService internalPayment,
    AchPaymentService achPayment,
    RtpPaymentService rtpPayment,
    FedNowPaymentService fedNowPayment,
    SwiftPaymentService swiftPayment,
    IConfiguration configuration,
    ILogger<TransfersController> logger) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(List<TransferResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll() =>
        Ok(await transferService.GetAllAsync(UserId()));

    [HttpGet("pending-approval")]
    [ProducesResponseType(typeof(List<PendingApprovalTransferResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPendingApproval() =>
        Ok(await transferService.GetPendingApprovalAsync(UserId()));

    [HttpGet("{id:guid}/status")]
    [ProducesResponseType(typeof(TransferStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(Guid id) =>
        Ok(await transferService.GetStatusAsync(UserId(), id));

    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(typeof(TransferResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(Guid id) =>
        Ok(await transferService.ApproveAsync(UserId(), id));

    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType(typeof(TransferResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(Guid id) =>
        Ok(await transferService.RejectAsync(UserId(), id));

    [HttpPost("{id:guid}/webhook")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Webhook(Guid id, [FromBody] WebhookRequest request)
    {
        var expectedSecret = configuration["Webhook:Secret"];
        var providedSecret = Request.Headers["X-Webhook-Secret"].FirstOrDefault();
        if (string.IsNullOrEmpty(expectedSecret) || providedSecret != expectedSecret)
            return Unauthorized(new { message = "Invalid webhook secret" });

        await transferService.ProcessWebhookAsync(id, request.Status, request.ReferenceId);
        return Ok(new { message = "Transfer status updated" });
    }

    [HttpPost("internal")]
    [ProducesResponseType(typeof(TransferResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateInternal([FromBody] CreateInternalTransferRequest request) =>
        StatusCode(StatusCodes.Status201Created, await internalPayment.CreateAsync(UserId(), request));

    [HttpPost("ach")]
    [ProducesResponseType(typeof(TransferResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateAch([FromBody] CreateAchTransferRequest request) =>
        StatusCode(StatusCodes.Status201Created, await achPayment.CreateAsync(UserId(), request));

    [HttpPost("rtp")]
    [ProducesResponseType(typeof(TransferResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateRtp([FromBody] CreateRtpTransferRequest request) =>
        StatusCode(StatusCodes.Status201Created, await rtpPayment.CreateAsync(UserId(), request));

    [HttpPost("fednow")]
    [ProducesResponseType(typeof(TransferResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateFedNow([FromBody] CreateFedNowTransferRequest request) =>
        StatusCode(StatusCodes.Status201Created, await fedNowPayment.CreateAsync(UserId(), request));

    [HttpPost("swift")]
    [ProducesResponseType(typeof(TransferResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateSwift([FromBody] CreateSwiftTransferRequest request) =>
        StatusCode(StatusCodes.Status201Created, await swiftPayment.CreateAsync(UserId(), request));

    /// <summary>
    /// Called by SWIFT Middleware when forwarding an inbound transfer to our bank
    /// or when returning a rejected transfer (X-SWIFT-Message-Type: RETURN).
    /// </summary>
    [HttpPost("swift/receive")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SwiftReceive(
        [FromHeader(Name = "X-SWIFT-Message-Type")] string? messageType,
        [FromHeader(Name = "X-SWIFT-UETR")] string? uetrHeader,
        CancellationToken cancellationToken)
    {
        var webhookSecret = configuration["Swift:WebhookSecret"];
        var providedSecret = Request.Headers["X-SWIFT-Webhook-Secret"].FirstOrDefault();
        if (string.IsNullOrEmpty(webhookSecret))
        {
            logger.LogWarning("Swift:WebhookSecret is not configured — /transfers/swift/receive is unauthenticated");
        }
        else if (string.IsNullOrEmpty(providedSecret) ||
                 !CryptographicOperations.FixedTimeEquals(
                     Encoding.UTF8.GetBytes(providedSecret),
                     Encoding.UTF8.GetBytes(webhookSecret)))
        {
            return Unauthorized(new { message = "Invalid SWIFT webhook secret" });
        }

        string xml;
        using (var reader = new System.IO.StreamReader(Request.Body))
            xml = await reader.ReadToEndAsync(cancellationToken);

        var isReturn = string.Equals(messageType, "RETURN", StringComparison.OrdinalIgnoreCase);

        var uetr = uetrHeader ?? SwiftGateway.ExtractUetr(xml);
        if (string.IsNullOrWhiteSpace(uetr))
            return BadRequest(new { message = "Missing UETR in request" });

        await transferService.ProcessSwiftReceiveAsync(uetr, isReturn, xml, cancellationToken);

        return Ok(new { status = isReturn ? "return_received" : "received", uetr });
    }

    private Guid UserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
}
