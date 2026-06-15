using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using UsBankSystem.Api.Integrations;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Models.Responses;
using UsBankSystem.Api.Services;

namespace UsBankSystem.Api.Controllers;

[ApiController]
[Route("accounts/{accountId:guid}/cards")]
[Tags("Cards")]
public class CardsController(CardService cardService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(List<CardResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCards(Guid accountId)
    {
        var userId = UserId();
        return Ok(await cardService.GetCardsAsync(userId, accountId));
    }

    [HttpPost]
    [ProducesResponseType(typeof(CardResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RegisterCard(Guid accountId, [FromBody] RegisterCardRequest request)
    {
        var userId = UserId();
        var result = await cardService.RegisterCardAsync(userId, accountId, request);
        return CreatedAtAction(nameof(GetCards), new { accountId }, result);
    }

    [HttpGet("{cardId:guid}")]
    [ProducesResponseType(typeof(CardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCard(Guid accountId, Guid cardId)
    {
        var userId = UserId();
        return Ok(await cardService.GetCardAsync(userId, accountId, cardId));
    }

    [HttpPatch("{cardId:guid}/status")]
    [ProducesResponseType(typeof(CardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateCardStatus(Guid accountId, Guid cardId, [FromBody] UpdateCardStatusRequest request)
    {
        var userId = UserId();
        return Ok(await cardService.UpdateCardStatusAsync(userId, accountId, cardId, request));
    }

    [HttpPatch("{cardId:guid}/limits")]
    [ProducesResponseType(typeof(CardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateCardLimits(Guid accountId, Guid cardId, [FromBody] UpdateCardLimitsRequest request)
    {
        var userId = UserId();
        return Ok(await cardService.UpdateCardLimitsAsync(userId, accountId, cardId, request));
    }

    [HttpGet("{cardId:guid}/external-status")]
    [ProducesResponseType(typeof(CardGatewayStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetExternalStatus(Guid accountId, Guid cardId)
    {
        var userId = UserId();
        var status = await cardService.GetExternalCardStatusAsync(userId, accountId, cardId);
        return status is null ? NoContent() : Ok(status);
    }

    [HttpPost("{cardId:guid}/topup")]
    [ProducesResponseType(typeof(CardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TopUpCard(Guid accountId, Guid cardId, [FromBody] TopUpCardRequest request)
    {
        var userId = UserId();
        return Ok(await cardService.TopUpCardAsync(userId, accountId, cardId, request));
    }

    private Guid UserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
}
