using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Services;

namespace UsBankSystem.Api.Controllers;

[ApiController]
[Route("klik/webhook")]
[AllowAnonymous]
[Tags("KlikWebhook")]
public class KlikWebhookController(BlikService blikService, IConfiguration cfg, ILogger<KlikWebhookController> logger) : ControllerBase
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
        var allowUnsigned = string.Equals(cfg["Klik:AllowUnsignedWebhooks"], "true", StringComparison.OrdinalIgnoreCase);

        // When AllowUnsignedWebhooks=true, accept webhooks from the KLIK orchestrator.
        // The orchestrator sends X-KLIK-Webhook-Source instead of X-Webhook-Secret;
        // validate the header value to prevent arbitrary callers from spoofing.
        if (allowUnsigned && Request.Headers.TryGetValue("X-KLIK-Webhook-Source", out var sourceHdr))
        {
            var expectedSource = cfg["Klik:WebhookSourceName"] ?? "klik-orchestrator";
            if (string.Equals(sourceHdr, expectedSource, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogDebug("Accepted KLIK orchestrator webhook via X-KLIK-Webhook-Source");
                return true;
            }
            logger.LogWarning("X-KLIK-Webhook-Source header present but value '{Actual}' != '{Expected}'", sourceHdr.ToString(), expectedSource);
        }

        var secret = cfg["Klik:WebhookSecret"];
        if (string.IsNullOrEmpty(secret))
        {
            if (allowUnsigned)
            {
                logger.LogWarning("Webhook secret not configured but AllowUnsignedWebhooks=true — accepting unsigned webhook");
                return true;
            }
            return false;
        }

        return Request.Headers.TryGetValue("X-Webhook-Secret", out var hdr) && hdr == secret;
    }
}
