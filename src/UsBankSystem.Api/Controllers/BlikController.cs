using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Api.Services;

namespace UsBankSystem.Api.Controllers;

[ApiController]
[Route("blik")]
[Tags("Blik")]
public class BlikController(BlikService blikService) : ControllerBase
{
    [HttpPost("generate")]
    [ProducesResponseType(typeof(BlikCodeResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Generate([FromBody] BlikGenerateRequest request)
    {
        var result = await blikService.GenerateCodeAsync(UserId(), request.AccountId);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet("pending")]
    [ProducesResponseType(typeof(List<BlikAuthorizationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPending()
    {
        return Ok(await blikService.GetPendingAsync(UserId()));
    }

    [HttpPost("{authorizationId:guid}/approve")]
    [ProducesResponseType(typeof(BlikAuthorizationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(Guid authorizationId)
    {
        return Ok(await blikService.DecideAsync(UserId(), authorizationId, accepted: true));
    }

    [HttpPost("{authorizationId:guid}/reject")]
    [ProducesResponseType(typeof(BlikAuthorizationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reject(Guid authorizationId)
    {
        return Ok(await blikService.DecideAsync(UserId(), authorizationId, accepted: false));
    }

    [HttpGet("transactions")]
    [ProducesResponseType(typeof(List<BlikAuthorizationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetTransactions()
    {
        return Ok(await blikService.GetHistoryAsync(UserId()));
    }

    private Guid UserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
}
