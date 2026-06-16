using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Api.Services;

namespace UsBankSystem.Api.Controllers;

[ApiController]
[Tags("P2P")]
public class P2pController(PhoneAliasService phoneAliasService) : ControllerBase
{
    [HttpPost("accounts/{accountId:guid}/phone-alias")]
    [ProducesResponseType<PhoneAliasResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RegisterAlias(Guid accountId, [FromBody] RegisterPhoneAliasRequest req)
    {
        var result = await phoneAliasService.RegisterAliasAsync(UserId(), accountId, req.Phone);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpDelete("accounts/{accountId:guid}/phone-alias")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAlias(Guid accountId)
    {
        await phoneAliasService.DeleteAliasAsync(UserId(), accountId);
        return NoContent();
    }

    [HttpPost("transfers/p2p")]
    [ProducesResponseType<TransferResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SendToPhone([FromBody] SendToPhoneRequest req)
    {
        var result = await phoneAliasService.SendToPhoneAsync(
            UserId(), req.FromAccountId, req.Phone, req.Amount, req.Currency, req.Description);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    private Guid UserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
}
