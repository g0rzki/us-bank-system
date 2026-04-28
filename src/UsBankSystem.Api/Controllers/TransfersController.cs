using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Api.Services;

namespace UsBankSystem.Api.Controllers;

[ApiController]
[Route("transfers")]
[Tags("Transfers")]
public class TransfersController(TransferService transferService) : ControllerBase
{
    [HttpPost("internal")]
    [ProducesResponseType(typeof(TransferResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateInternal([FromBody] CreateInternalTransferRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
        var (success, error, statusCode, result) = await transferService.CreateInternalAsync(userId, request);
        return statusCode switch
        {
            400 => BadRequest(new { message = error }),
            404 => NotFound(new { message = error }),
            _ => StatusCode(201, result)
        };
    }
}