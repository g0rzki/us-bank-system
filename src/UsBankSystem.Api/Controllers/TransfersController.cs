using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Api.Services;

namespace UsBankSystem.Api.Controllers;

[ApiController]
[Route("transfers")]
[Tags("Transfers")]
public class TransfersController(TransferService transferService, IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(List<TransferResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
        return Ok(await transferService.GetAllAsync(userId));
    }

    [HttpPost("internal")]
    [ProducesResponseType(typeof(TransferResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateInternal([FromBody] CreateInternalTransferRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
        var result = await transferService.CreateInternalAsync(userId, request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("ach")]
    [ProducesResponseType(typeof(TransferResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateAch([FromBody] CreateAchTransferRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
        var result = await transferService.CreateAchAsync(userId, request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("rtp")]
    [ProducesResponseType(typeof(TransferResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateRtp([FromBody] CreateRtpTransferRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
        var result = await transferService.CreateRtpAsync(userId, request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("fednow")]
    [ProducesResponseType(typeof(TransferResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateFedNow([FromBody] CreateFedNowTransferRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
        var result = await transferService.CreateFedNowAsync(userId, request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("swift")]
    [ProducesResponseType(typeof(TransferResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateSwift([FromBody] CreateSwiftTransferRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
        var result = await transferService.CreateSwiftAsync(userId, request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet("{id:guid}/status")]
    [ProducesResponseType(typeof(TransferStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
        var result = await transferService.GetStatusAsync(userId, id);
        return Ok(result);
    }

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
}
