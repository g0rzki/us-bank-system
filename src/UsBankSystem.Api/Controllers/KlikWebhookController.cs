using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Services;

namespace UsBankSystem.Api.Controllers;

[ApiController]
[Route("klik/webhook")]
[AllowAnonymous]
[Tags("KlikWebhook")]
public class KlikWebhookController(BlikService blikService, IConfiguration cfg) : ControllerBase
{
    [HttpPost("authorize")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Authorize([FromBody] KlikWebhookAuthorizeRequest request)
    {
        if (!ValidateSecret())
            return Unauthorized();

        var result = await blikService.HandleAuthorizeWebhookAsync(request);
        return Ok(result);
    }

    [HttpPost("ping")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Ping([FromBody] KlikWebhookPingRequest request)
    {
        if (!ValidateSecret())
            return Unauthorized();

        return Ok(new { request.Timestamp, request.Nonce, pong = true });
    }

    private bool ValidateSecret()
    {
        var secret = cfg["Klik:WebhookSecret"];
        if (string.IsNullOrEmpty(secret))
            return true;

        return Request.Headers.TryGetValue("X-Webhook-Secret", out var hdr) && hdr == secret;
    }
}
